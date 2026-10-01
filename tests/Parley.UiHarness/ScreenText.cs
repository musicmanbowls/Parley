using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;

namespace Parley.UiHarness;

/// <summary>A piece of text as it was drawn: what it says and where on screen it is.</summary>
internal sealed class TextRun
{
    public required string Text { get; init; }
    public required Vector2 Min { get; init; }
    public required Vector2 Max { get; init; }
    public required uint Colour { get; init; }

    /// <summary>The clip rectangle it was drawn under. Text outside this is not actually visible.</summary>
    public required Vector4 Clip { get; init; }

    /// <summary>True for glyphs from the icon font, whose "text" is a private-use codepoint.</summary>
    public required bool Icon { get; init; }

    /// <summary>Left edge of each character, and one past the last, for finding part of a run.</summary>
    public required float[] Edges { get; init; }

    public Vector2 Centre => (Min + Max) * 0.5f;

    public bool Visible => Centre.X >= Clip.X && Centre.X <= Clip.Z && Centre.Y >= Clip.Y && Centre.Y <= Clip.W;

    public override string ToString() => $"\"{Text}\" at ({Min.X:0},{Min.Y:0})-({Max.X:0},{Max.Y:0})";
}

/// <summary>
/// Reads the text back out of a frame's draw data.
///
/// ImGui draws each character as a rectangle textured with that character's
/// cell of the font atlas. Knowing which cell belongs to which character turns
/// the rectangles back into strings, with their positions. That lets a test
/// say "click on Linkshells" or "this message has not moved" without knowing
/// anything about how the window is laid out.
/// </summary>
internal sealed unsafe class ScreenReader
{
    private readonly record struct Glyph(ushort Codepoint, float Advance, float X0, float X1, float Y0, bool Icon, float Space);

    private readonly Dictionary<(ulong Texture, int U, int V), Glyph> glyphs = [];
    private readonly int atlasWidth;
    private readonly int atlasHeight;

    public ScreenReader(int atlasWidth, int atlasHeight)
    {
        this.atlasWidth = atlasWidth;
        this.atlasHeight = atlasHeight;
    }

    /// <param name="textureIds">The id given to each of the atlas's textures, by index.</param>
    public void Register(ImFontPtr font, bool icons, IReadOnlyList<ulong> textureIds)
    {
        var space = font.Handle->FindGlyph(' ');
        var spaceAdvance = space != null ? space->AdvanceX : font.Handle->FontSize * 0.25f;

        var all = font.Handle->Glyphs;
        for (var i = 0; i < all.Size; i++)
        {
            ref var glyph = ref all.Data[i];
            if (glyph.Visible == 0) continue;
            if (glyph.TextureIndex >= textureIds.Count) continue;

            var key = (textureIds[(int)glyph.TextureIndex], Texel(glyph.U0, atlasWidth), Texel(glyph.V0, atlasHeight));
            glyphs.TryAdd(key, new Glyph((ushort)glyph.Codepoint, glyph.AdvanceX, glyph.X0, glyph.X1, glyph.Y0, icons, spaceAdvance));
        }
    }

    public List<TextRun> Read(ImDrawDataPtr data)
    {
        var runs = new List<TextRun>();
        for (var n = 0; n < data.CmdListsCount; n++)
        {
            var list = data.Handle->CmdLists[n];
            for (var c = 0; c < list->CmdBuffer.Size; c++)
            {
                ref var command = ref list->CmdBuffer.Data[c];
                if (command.UserCallback != null) continue;
                ReadCommand(list, ref command, runs);
            }
        }
        return runs;
    }

    private void ReadCommand(ImDrawList* list, ref ImDrawCmd command, List<TextRun> runs)
    {
        var vertices = list->VtxBuffer.Data;
        var indices = list->IdxBuffer.Data + command.IdxOffset;
        var texture = command.TextureId.Handle;

        var text = new StringBuilder();
        var edges = new List<float>();
        var min = Vector2.Zero;
        var max = Vector2.Zero;
        var colour = 0u;
        var icon = false;
        var pen = 0f;       // where the next character would start if it followed straight on
        var top = 0f;       // top of the line the run is on
        var open = false;

        void Close(Vector4 clip)
        {
            if (open && text.Length > 0)
            {
                edges.Add(max.X);
                runs.Add(new TextRun
                {
                    Text = text.ToString(), Min = min, Max = max, Colour = colour, Clip = clip, Icon = icon, Edges = [.. edges],
                });
            }
            text.Clear();
            edges.Clear();
            open = false;
        }

        for (var i = 0u; i + 5 < command.ElemCount + 0u;)
        {
            // A rectangle is two triangles sharing a diagonal: a b c, a c d.
            var a = indices[i];
            var isQuad = indices[i + 3] == a && indices[i + 4] == indices[i + 2];
            if (!isQuad)
            {
                Close(command.ClipRect);
                i += 3;
                continue;
            }

            var first = vertices[command.VtxOffset + a];
            var third = vertices[command.VtxOffset + indices[i + 2]];
            i += 6;

            var key = (texture, Texel(first.Uv.X, atlasWidth), Texel(first.Uv.Y, atlasHeight));
            if (!glyphs.TryGetValue(key, out var glyph))
            {
                Close(command.ClipRect);
                continue;
            }

            var designWidth = glyph.X1 - glyph.X0;
            var scale = designWidth > 0f ? (third.Pos.X - first.Pos.X) / designWidth : 1f;
            var start = first.Pos.X - (glyph.X0 * scale);
            var lineTop = first.Pos.Y - (glyph.Y0 * scale);

            var sameLine = open && glyph.Icon == icon && first.Col == colour && MathF.Abs(lineTop - top) < 1.5f && start >= pen - (2f * scale);
            if (sameLine)
            {
                // Spaces are not drawn, only skipped over. Put back however many fit in the gap.
                var spaces = (int)MathF.Round((start - pen) / (glyph.Space * scale));
                if (spaces > 8) sameLine = false;
                else
                {
                    for (var s = 0; s < spaces; s++)
                    {
                        edges.Add(pen + (s * glyph.Space * scale));
                        text.Append(' ');
                    }
                }
            }

            if (!sameLine)
            {
                Close(command.ClipRect);
                open = true;
                min = first.Pos;
                max = third.Pos;
                colour = first.Col;
                icon = glyph.Icon;
                top = lineTop;
            }

            edges.Add(start);
            text.Append((char)glyph.Codepoint);
            min = Vector2.Min(min, first.Pos);
            max = Vector2.Max(max, third.Pos);
            pen = start + (glyph.Advance * scale);
        }

        Close(command.ClipRect);
    }

    private static int Texel(float uv, int size) => (int)MathF.Round(uv * size);
}
