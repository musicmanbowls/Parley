using System.IO.Compression;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Parley.UiHarness;

/// <summary>One texture of the font atlas, as the draw commands refer to it.</summary>
internal sealed class Texture(int width, int height, uint[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;

    /// <summary>0xAABBGGRR, the same packing ImGui uses for colours.</summary>
    public uint[] Pixels { get; } = pixels;
}

/// <summary>
/// A plain software rasteriser for ImGui's draw data: textured, vertex-coloured
/// triangles, clipped and alpha-blended. It stands in for the graphics card so
/// that a frame can be turned into a picture with nothing else running.
/// </summary>
internal sealed unsafe class Canvas
{
    private readonly float[] pixels;

    public Canvas(int width, int height)
    {
        Width = width;
        Height = height;
        pixels = new float[width * height * 3];
    }

    public int Width { get; }
    public int Height { get; }

    public void Clear(Vector3 colour)
    {
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = colour.X;
            pixels[i + 1] = colour.Y;
            pixels[i + 2] = colour.Z;
        }
    }

    /// <summary>A checkerboard, so a translucent window can be told from an opaque one.</summary>
    public void ClearChecker(Vector3 a, Vector3 b, int size)
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var colour = ((x / size) + (y / size)) % 2 == 0 ? a : b;
                var i = ((y * Width) + x) * 3;
                pixels[i] = colour.X;
                pixels[i + 1] = colour.Y;
                pixels[i + 2] = colour.Z;
            }
        }
    }

    public void Draw(ImDrawDataPtr data, IReadOnlyDictionary<ulong, Texture> textures)
    {
        var offset = data.DisplayPos;
        for (var n = 0; n < data.CmdListsCount; n++)
        {
            var list = data.Handle->CmdLists[n];
            var vertices = list->VtxBuffer.Data;
            var indices = list->IdxBuffer.Data;

            for (var c = 0; c < list->CmdBuffer.Size; c++)
            {
                ref var command = ref list->CmdBuffer.Data[c];
                if (command.UserCallback != null) continue;
                if (!textures.TryGetValue(command.TextureId.Handle, out var texture)) continue;

                var clipMinX = Math.Max(0, (int)MathF.Floor(command.ClipRect.X - offset.X));
                var clipMinY = Math.Max(0, (int)MathF.Floor(command.ClipRect.Y - offset.Y));
                var clipMaxX = Math.Min(Width, (int)MathF.Ceiling(command.ClipRect.Z - offset.X));
                var clipMaxY = Math.Min(Height, (int)MathF.Ceiling(command.ClipRect.W - offset.Y));
                if (clipMaxX <= clipMinX || clipMaxY <= clipMinY) continue;

                for (var i = 0u; i + 2 < command.ElemCount; i += 3)
                {
                    var a = vertices[command.VtxOffset + indices[command.IdxOffset + i]];
                    var b = vertices[command.VtxOffset + indices[command.IdxOffset + i + 1]];
                    var d = vertices[command.VtxOffset + indices[command.IdxOffset + i + 2]];
                    a.Pos -= offset;
                    b.Pos -= offset;
                    d.Pos -= offset;
                    Triangle(a, b, d, texture, clipMinX, clipMinY, clipMaxX, clipMaxY);
                }
            }
        }
    }

    public void SavePng(string path) => SavePng(path, 0, 0, Width, Height);

    /// <summary>Saves one rectangle of the canvas.</summary>
    public void SavePng(string path, int left, int top, int width, int height)
    {
        var raw = new byte[height * ((width * 3) + 1)];
        var at = 0;
        for (var y = top; y < top + height; y++)
        {
            raw[at++] = 0;
            for (var x = left * 3; x < (left + width) * 3; x++)
                raw[at++] = (byte)Math.Clamp((int)MathF.Round(pixels[(y * Width * 3) + x] * 255f), 0, 255);
        }

        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        WriteInt(header, 0, width);
        WriteInt(header, 4, height);
        header[8] = 8;  // bits per channel
        header[9] = 2;  // truecolour
        Chunk(file, "IHDR", header);

        using (var packed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(raw);
            Chunk(file, "IDAT", packed.ToArray());
        }

        Chunk(file, "IEND", []);
    }

    private void Triangle(ImDrawVert a, ImDrawVert b, ImDrawVert c, Texture texture, int clipMinX, int clipMinY, int clipMaxX, int clipMaxY)
    {
        // Wound so that the inside is on the positive side of every edge.
        var area = Edge(a.Pos, b.Pos, c.Pos);
        if (area == 0f) return;
        if (area < 0f)
        {
            (b, c) = (c, b);
            area = -area;
        }

        var minX = Math.Max(clipMinX, (int)MathF.Floor(MathF.Min(a.Pos.X, MathF.Min(b.Pos.X, c.Pos.X))));
        var minY = Math.Max(clipMinY, (int)MathF.Floor(MathF.Min(a.Pos.Y, MathF.Min(b.Pos.Y, c.Pos.Y))));
        var maxX = Math.Min(clipMaxX, (int)MathF.Ceiling(MathF.Max(a.Pos.X, MathF.Max(b.Pos.X, c.Pos.X))));
        var maxY = Math.Min(clipMaxY, (int)MathF.Ceiling(MathF.Max(a.Pos.Y, MathF.Max(b.Pos.Y, c.Pos.Y))));
        if (maxX <= minX || maxY <= minY) return;

        // Two triangles sharing an edge must not both colour the pixels on
        // it, or anything translucent shows a seam. The usual rule: a pixel
        // exactly on an edge belongs to the triangle only if that edge is its
        // top or its left.
        var ownsBc = Owns(b.Pos, c.Pos);
        var ownsCa = Owns(c.Pos, a.Pos);
        var ownsAb = Owns(a.Pos, b.Pos);

        var colourA = Unpack(a.Col);
        var colourB = Unpack(b.Col);
        var colourC = Unpack(c.Col);
        var flat = a.Col == b.Col && b.Col == c.Col;
        var solid = a.Uv == b.Uv && b.Uv == c.Uv;
        var texel = solid ? Sample(texture, a.Uv) : default;

        for (var y = minY; y < maxY; y++)
        {
            for (var x = minX; x < maxX; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var wa = Edge(b.Pos, c.Pos, p);
                var wb = Edge(c.Pos, a.Pos, p);
                var wc = Edge(a.Pos, b.Pos, p);
                if (wa < 0f || wb < 0f || wc < 0f) continue;
                if ((wa == 0f && !ownsBc) || (wb == 0f && !ownsCa) || (wc == 0f && !ownsAb)) continue;

                wa /= area;
                wb /= area;
                wc /= area;

                var colour = flat ? colourA : (colourA * wa) + (colourB * wb) + (colourC * wc);
                var source = colour * (solid ? texel : Sample(texture, (a.Uv * wa) + (b.Uv * wb) + (c.Uv * wc)));
                if (source.W <= 0f) continue;

                var i = ((y * Width) + x) * 3;
                var keep = 1f - source.W;
                pixels[i] = (source.X * source.W) + (pixels[i] * keep);
                pixels[i + 1] = (source.Y * source.W) + (pixels[i + 1] * keep);
                pixels[i + 2] = (source.Z * source.W) + (pixels[i + 2] * keep);
            }
        }
    }

    private static float Edge(Vector2 a, Vector2 b, Vector2 p) => ((b.X - a.X) * (p.Y - a.Y)) - ((b.Y - a.Y) * (p.X - a.X));

    /// <summary>With y pointing down and this winding, an edge heading up is a left edge and a level one heading right is a top edge.</summary>
    private static bool Owns(Vector2 from, Vector2 to)
    {
        var dy = to.Y - from.Y;
        return dy < 0f || (dy == 0f && to.X > from.X);
    }

    private static Vector4 Unpack(uint colour) => new(
        (colour & 0xFF) / 255f, ((colour >> 8) & 0xFF) / 255f, ((colour >> 16) & 0xFF) / 255f, ((colour >> 24) & 0xFF) / 255f);

    private static Vector4 Sample(Texture texture, Vector2 uv)
    {
        var x = (uv.X * texture.Width) - 0.5f;
        var y = (uv.Y * texture.Height) - 0.5f;
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var fx = x - x0;
        var fy = y - y0;

        var top = Vector4.Lerp(Texel(texture, x0, y0), Texel(texture, x0 + 1, y0), fx);
        var bottom = Vector4.Lerp(Texel(texture, x0, y0 + 1), Texel(texture, x0 + 1, y0 + 1), fx);
        return Vector4.Lerp(top, bottom, fy);
    }

    private static Vector4 Texel(Texture texture, int x, int y)
    {
        x = Math.Clamp(x, 0, texture.Width - 1);
        y = Math.Clamp(y, 0, texture.Height - 1);
        return Unpack(texture.Pixels[(y * texture.Width) + x]);
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteInt(length, 0, data.Length);
        stream.Write(length);

        var body = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++) body[i] = (byte)type[i];
        data.CopyTo(body, 4);
        stream.Write(body);

        var crc = new byte[4];
        WriteInt(crc, 0, (int)Crc(body));
        stream.Write(crc);
    }

    private static void WriteInt(byte[] buffer, int at, int value)
    {
        buffer[at] = (byte)(value >> 24);
        buffer[at + 1] = (byte)(value >> 16);
        buffer[at + 2] = (byte)(value >> 8);
        buffer[at + 3] = (byte)value;
    }

    private static uint Crc(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
