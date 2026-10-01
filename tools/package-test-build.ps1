<#
.SYNOPSIS
    Packs Parley and its Umbra companion into a zip for testers, with the
    install guide as a single web page and, when Microsoft Edge is present, a PDF.

.DESCRIPTION
    Builds the solution in Release, redraws the guide's pictures with the UI
    harness (which needs Dalamud installed, for its ImGui build and fonts),
    and writes dist\Parley-test-<date>.zip:

        Parley\
            Install guide.html   the guide, pictures included
            Install guide.pdf    the same, ready to print or send
            Plugin\              Parley.dll, Parley.json, Parley.deps.json
            Umbra widget\        Umbra.Parley.dll

.PARAMETER Output
    Where the zip and the folder it was made from go. dist\ under the repository by default.

.PARAMETER SkipPictures
    Use the pictures already in docs\images instead of drawing them again.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\package-test-build.ps1
#>
param(
    [string]$Output,
    [switch]$SkipPictures
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Output) { $Output = Join-Path $root 'dist' }

# The x64 SDK. On some machines the dotnet on PATH is a 32-bit one with no SDK.
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

Write-Host 'Building Parley and the Umbra companion (Release)...'
& $dotnet build (Join-Path $root 'Parley.sln') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'The build failed.' }

$images = Join-Path $root 'docs\images'
if (-not $SkipPictures) {
    Write-Host 'Drawing the pictures for the guide...'
    $harness = Join-Path $root 'tests\Parley.UiHarness\Parley.UiHarness.csproj'
    & $dotnet build $harness -c Debug --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'The UI harness did not build.' }
    & (Join-Path $root 'tests\Parley.UiHarness\bin\Debug\Parley.UiHarness.exe') $images guide | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'The UI harness could not draw the guide pictures.' }
}

$plugin = Join-Path $root 'Parley\bin\Release'
$companion = Join-Path $root 'Umbra.Parley\bin\Release\Umbra.Parley.dll'
$version = (Get-Item (Join-Path $plugin 'Parley.dll')).VersionInfo.FileVersion
$stamp = Get-Date -Format 'yyyy-MM-dd'
$friendlyDate = (Get-Date).ToString('d MMMM yyyy', [Globalization.CultureInfo]::InvariantCulture)

$name = "Parley-test-$stamp"
$work = Join-Path $Output $name
$package = Join-Path $work 'Parley'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force (Join-Path $package 'Plugin') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $package 'Umbra widget') | Out-Null

foreach ($file in 'Parley.dll', 'Parley.json', 'Parley.deps.json') {
    Copy-Item (Join-Path $plugin $file) (Join-Path $package 'Plugin')
}
Copy-Item $companion (Join-Path $package 'Umbra widget')

# The guide as one file: every picture folded into the page itself, so it
# can be opened, printed or passed along on its own.
Write-Host 'Writing the guide...'
$html = [IO.File]::ReadAllText((Join-Path $root 'docs\install-guide.html'))
$html = $html.Replace('{{BUILD_DATE}}', $friendlyDate).Replace('{{VERSION}}', $version)
$html = [regex]::Replace($html, 'src="images/([^"]+\.png)"', {
    param($match)
    $bytes = [IO.File]::ReadAllBytes((Join-Path $images $match.Groups[1].Value))
    'src="data:image/png;base64,' + [Convert]::ToBase64String($bytes) + '"'
})
$guide = Join-Path $package 'Install guide.html'
[IO.File]::WriteAllText($guide, $html, (New-Object Text.UTF8Encoding($false)))

# A PDF of the same, if Edge is there to print it.
$edge = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($edge) {
    Write-Host 'Printing the guide to PDF...'
    $pdf = Join-Path $package 'Install guide.pdf'
    $profile = Join-Path ([IO.Path]::GetTempPath()) ('parley-pdf-' + [Guid]::NewGuid().ToString('N'))
    $uri = ([Uri]$guide).AbsoluteUri
    # One string, quoted by hand: Start-Process does not quote paths with spaces in them.
    $arguments = "--headless --disable-gpu --no-pdf-header-footer `"--user-data-dir=$profile`" `"--print-to-pdf=$pdf`" `"$uri`""
    Start-Process -FilePath $edge -ArgumentList $arguments -Wait -WindowStyle Hidden
    Remove-Item -Recurse -Force $profile -ErrorAction SilentlyContinue
    if (-not (Test-Path $pdf)) { Write-Warning 'Edge did not produce a PDF; the HTML guide is still in the package.' }
}
else {
    Write-Warning 'Microsoft Edge was not found, so there is no PDF; the HTML guide is still in the package.'
}

# Written entry by entry rather than with Compress-Archive, which on Windows
# PowerShell stores paths with backslashes; unzip tools elsewhere (Linux,
# a Steam Deck) then make files with backslashes in their names.
$zip = Join-Path $Output "$name.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $package -Recurse -File) {
        $entry = 'Parley/' + $file.FullName.Substring($package.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal)
    }
}
finally {
    $archive.Dispose()
}

Write-Host ''
Write-Host "Parley $version packed into $zip"
