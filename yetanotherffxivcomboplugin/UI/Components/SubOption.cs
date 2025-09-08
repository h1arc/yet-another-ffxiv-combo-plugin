using System;
using Dalamud.Bindings.ImGui;

namespace yetanotherffxivcomboplugin.UI.Components;

internal static class SubOption
{
    private const float IndentWidth = 20f;
    public readonly struct Scope : IDisposable
    {
        private readonly float _indentPx;
        private readonly bool _pushedAlpha;

        public Scope(int levels = 1, bool dim = false, float dimFactor = 0.5f)
        {
            if (levels < 0) levels = 0;
            _indentPx = IndentWidth * levels;
            if (_indentPx > 0) ImGui.Indent(_indentPx);

            if (dim)
            {
                var currentAlpha = ImGui.GetStyle().Alpha;
                // Cascade alpha by indentation level: alpha *= dimFactor^levels
                var effective = levels > 0 ? MathF.Pow(dimFactor, levels) : dimFactor;
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, currentAlpha * effective);
                _pushedAlpha = true;
            }
            else _pushedAlpha = false;
        }

        public void Dispose()
        {
            if (_pushedAlpha) ImGui.PopStyleVar();
            if (_indentPx > 0) ImGui.Unindent(_indentPx);
        }
    }

    public static Scope Begin(int levels = 1, bool dim = false, float dimFactor = 0.5f)
        => new Scope(levels, dim, dimFactor);
}
