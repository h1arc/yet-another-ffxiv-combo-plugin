using System;
using Dalamud.Bindings.ImGui;

namespace yetanotherffxivcomboplugin.UI.Components;

/// <summary>
/// Clean font helper that only uses crisp FFXIV Axis game fonts.
/// No scaling fallbacks - either uses proper game font or default font.
/// </summary>
internal static class Fonts
{
    /// <summary>
    /// Available FFXIV Axis game font sizes.
    /// </summary>
    public enum Size
    {
        Small = 12,       // 12px - Small text
        SmallMedium = 14, // 14px - Small-medium text
        Default = 16,     // 16px - Default size
        Medium = 18,      // 18px - Medium text
        Large = 20,       // 20px - Large text
        ExtraLarge = 24,  // 24px - Extra large text
        VeryLarge = 32    // 32px - Very large text
    }

    public readonly struct FontScope(bool pushed) : IDisposable
    {
        private readonly bool _pushed = pushed;
        public void Dispose() { if (_pushed) ImGui.PopFont(); }
    }

    /// <summary>
    /// Attempts to push a crisp Axis game font at the specified size.
    /// Returns true if successful, false if game font unavailable.
    /// </summary>
    public static bool TryPushGameFont(Size size, out FontScope scope)
    {
        return TryPushGameFont((float)size, out scope);
    }

    /// <summary>
    /// Attempts to push a crisp Axis game font at the specified pixel size.
    /// Returns true if successful, false if game font unavailable.
    /// </summary>
    public static bool TryPushGameFont(float sizePx, out FontScope scope)
    {
        var uiBuilder = Plugin.PluginInterface?.UiBuilder;
        if (uiBuilder == null)
        {
            scope = new FontScope(false);
            return false;
        }

        var uiType = uiBuilder.GetType();
        var styleType = Type.GetType("Dalamud.Interface.GameFonts.GameFontStyle, Dalamud");
        var familyType = Type.GetType("Dalamud.Interface.GameFonts.GameFontFamily, Dalamud");
        if (styleType == null || familyType == null)
        {
            scope = new FontScope(false);
            return false;
        }

        var axis = Enum.Parse(familyType, "Axis");
        if (axis == null)
        {
            scope = new FontScope(false);
            return false;
        }

        var style = Activator.CreateInstance(styleType, [axis, (int)sizePx]);
        var getHandle = uiType.GetMethod("GetGameFontHandle");
        if (getHandle == null || style == null)
        {
            scope = new FontScope(false);
            return false;
        }

        var handle = getHandle.Invoke(uiBuilder, [style]);
        if (handle == null)
        {
            scope = new FontScope(false);
            return false;
        }

        var imFontPtrProp = handle.GetType().GetProperty("ImFontPtr");
        if (imFontPtrProp == null)
        {
            scope = new FontScope(false);
            return false;
        }

        var imFont = (IntPtr?)imFontPtrProp.GetValue(handle);
        if (imFont is { } f && f != IntPtr.Zero)
        {
            unsafe { ImGui.PushFont((ImFont*)f); }
            scope = new FontScope(true);
            return true;
        }

        scope = new FontScope(false);
        return false;
    }

    /// <summary>
    /// Push a specific font size; uses crisp game font or falls back to default font (no scaling).
    /// </summary>
    public static IDisposable Push(Size size)
    {
        if (TryPushGameFont(size, out var fontScope))
        {
            return fontScope;
        }
        // No fallback - use default font
        return new FontScope(false);
    }

    /// <summary>
    /// Convenience methods for common sizes.
    /// </summary>
    public static IDisposable PushSmall() => Push(Size.Small);
    public static IDisposable PushDefault() => Push(Size.Default);
    public static IDisposable PushLarge() => Push(Size.Large);
}
