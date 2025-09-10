using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace yetanotherffxivcomboplugin.ui.Components;

internal static class Check
{
    public static void Box(string label, ref bool value, System.Action<bool>? onChanged = null)
    {
        using var _ = ImRaii.PushId(label);
        bool v = value;
        if (ImGui.Checkbox(label, ref v))
        {
            value = v;
            onChanged?.Invoke(v);
        }
    }

    // Overload with font size. Uses crisp Axis game font; falls back to default if unavailable.
    public static void Box(string label, Fonts.Size fontSize, ref bool value, System.Action<bool>? onChanged = null)
    {
        using var _ = ImRaii.PushId(label);
        using (Fonts.Push(fontSize))
        {
            bool v = value;
            if (ImGui.Checkbox(label, ref v))
            {
                value = v;
                onChanged?.Invoke(v);
            }
        }
    }
}
