using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Parley.UiHarness;

/// <summary>
/// Dalamud's standard ImGui style, copied out of StyleModelV1.DalamudStandard,
/// so that a window drawn here looks the way it does to someone who has not
/// changed Dalamud's appearance.
/// </summary>
internal static class DalamudStyle
{
    public static void Apply(float scale)
    {
        var style = ImGui.GetStyle();

        style.Alpha = 1f;
        style.WindowPadding = new Vector2(8, 8);
        style.WindowRounding = 4;
        style.WindowBorderSize = 0;
        style.WindowTitleAlign = new Vector2(0, 0.5f);
        style.WindowMenuButtonPosition = ImGuiDir.Right;
        style.ChildRounding = 0;
        style.ChildBorderSize = 1;
        style.PopupRounding = 0;
        style.PopupBorderSize = 0;
        style.FramePadding = new Vector2(4, 3);
        style.FrameRounding = 4;
        style.FrameBorderSize = 0;
        style.ItemSpacing = new Vector2(8, 4);
        style.ItemInnerSpacing = new Vector2(4, 4);
        style.CellPadding = new Vector2(4, 2);
        style.TouchExtraPadding = new Vector2(0, 0);
        style.IndentSpacing = 21;
        style.ScrollbarSize = 16;
        style.ScrollbarRounding = 9;
        style.GrabMinSize = 13;
        style.GrabRounding = 3;
        style.LogSliderDeadzone = 4;
        style.TabRounding = 4;
        style.TabBorderSize = 0;
        style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
        style.SelectableTextAlign = new Vector2(0, 0);
        style.DisplaySafeAreaPadding = new Vector2(3, 3);

        var colours = style.Colors;
        Set(colours, ImGuiCol.Text, 1, 1, 1, 1);
        Set(colours, ImGuiCol.TextDisabled, 0.5f, 0.5f, 0.5f, 1);
        Set(colours, ImGuiCol.WindowBg, 0.06f, 0.06f, 0.06f, 0.93f);
        Set(colours, ImGuiCol.ChildBg, 0, 0, 0, 0);
        Set(colours, ImGuiCol.PopupBg, 0.08f, 0.08f, 0.08f, 0.94f);
        Set(colours, ImGuiCol.Border, 0.43f, 0.43f, 0.5f, 0.5f);
        Set(colours, ImGuiCol.BorderShadow, 0, 0, 0, 0);
        Set(colours, ImGuiCol.FrameBg, 0.29f, 0.29f, 0.29f, 0.54f);
        Set(colours, ImGuiCol.FrameBgHovered, 0.54f, 0.54f, 0.54f, 0.4f);
        Set(colours, ImGuiCol.FrameBgActive, 0.64f, 0.64f, 0.64f, 0.67f);
        Set(colours, ImGuiCol.TitleBg, 0.022624433f, 0.022624206f, 0.022624206f, 0.85067874f);
        Set(colours, ImGuiCol.TitleBgActive, 0.38914025f, 0.10917056f, 0.10917056f, 0.8280543f);
        Set(colours, ImGuiCol.TitleBgCollapsed, 0, 0, 0, 0.51f);
        Set(colours, ImGuiCol.MenuBarBg, 0.14f, 0.14f, 0.14f, 1);
        Set(colours, ImGuiCol.ScrollbarBg, 0, 0, 0, 0);
        Set(colours, ImGuiCol.ScrollbarGrab, 0.31f, 0.31f, 0.31f, 1);
        Set(colours, ImGuiCol.ScrollbarGrabHovered, 0.41f, 0.41f, 0.41f, 1);
        Set(colours, ImGuiCol.ScrollbarGrabActive, 0.51f, 0.51f, 0.51f, 1);
        Set(colours, ImGuiCol.CheckMark, 0.86f, 0.86f, 0.86f, 1);
        Set(colours, ImGuiCol.SliderGrab, 0.54f, 0.54f, 0.54f, 1);
        Set(colours, ImGuiCol.SliderGrabActive, 0.67f, 0.67f, 0.67f, 1);
        Set(colours, ImGuiCol.Button, 0.71f, 0.71f, 0.71f, 0.4f);
        Set(colours, ImGuiCol.ButtonHovered, 0.3647059f, 0.078431375f, 0.078431375f, 0.94509804f);
        Set(colours, ImGuiCol.ButtonActive, 0.48416287f, 0.10077597f, 0.10077597f, 0.94509804f);
        Set(colours, ImGuiCol.Header, 0.59f, 0.59f, 0.59f, 0.31f);
        Set(colours, ImGuiCol.HeaderHovered, 0.5f, 0.5f, 0.5f, 0.8f);
        Set(colours, ImGuiCol.HeaderActive, 0.6f, 0.6f, 0.6f, 1);
        Set(colours, ImGuiCol.Separator, 0.43f, 0.43f, 0.5f, 0.5f);
        Set(colours, ImGuiCol.SeparatorHovered, 0.3647059f, 0.078431375f, 0.078431375f, 0.78280544f);
        Set(colours, ImGuiCol.SeparatorActive, 0.3647059f, 0.078431375f, 0.078431375f, 0.94509804f);
        Set(colours, ImGuiCol.ResizeGrip, 0.79f, 0.79f, 0.79f, 0.25f);
        Set(colours, ImGuiCol.ResizeGripHovered, 0.78f, 0.78f, 0.78f, 0.67f);
        Set(colours, ImGuiCol.ResizeGripActive, 0.3647059f, 0.078431375f, 0.078431375f, 0.94509804f);
        Set(colours, ImGuiCol.Tab, 0.23f, 0.23f, 0.23f, 0.86f);
        Set(colours, ImGuiCol.TabHovered, 0.58371043f, 0.30374074f, 0.30374074f, 0.7647059f);
        Set(colours, ImGuiCol.TabActive, 0.47963798f, 0.15843244f, 0.15843244f, 0.7647059f);
        Set(colours, ImGuiCol.TabUnfocused, 0.068f, 0.10199998f, 0.14800003f, 0.9724f);
        Set(colours, ImGuiCol.TabUnfocusedActive, 0.13599998f, 0.26199996f, 0.424f, 1);
        Set(colours, ImGuiCol.TableHeaderBg, 0.19f, 0.19f, 0.2f, 1);
        Set(colours, ImGuiCol.TableBorderStrong, 0.31f, 0.31f, 0.35f, 1);
        Set(colours, ImGuiCol.TableBorderLight, 0.23f, 0.23f, 0.25f, 1);
        Set(colours, ImGuiCol.TableRowBg, 0, 0, 0, 0);
        Set(colours, ImGuiCol.TableRowBgAlt, 1, 1, 1, 0.06f);
        Set(colours, ImGuiCol.TextSelectedBg, 0.26f, 0.59f, 0.98f, 0.35f);
        Set(colours, ImGuiCol.DragDropTarget, 1, 1, 0, 0.9f);
        Set(colours, ImGuiCol.NavHighlight, 0.26f, 0.59f, 0.98f, 1);
        Set(colours, ImGuiCol.NavWindowingHighlight, 1, 1, 1, 0.7f);
        Set(colours, ImGuiCol.NavWindowingDimBg, 0.8f, 0.8f, 0.8f, 0.2f);
        Set(colours, ImGuiCol.ModalWindowDimBg, 0.8f, 0.8f, 0.8f, 0.35f);

        if (scale != 1f) style.ScaleAllSizes(scale);
    }

    private static void Set(Span<Vector4> colours, ImGuiCol slot, float r, float g, float b, float a) =>
        colours[(int)slot] = new Vector4(r, g, b, a);
}
