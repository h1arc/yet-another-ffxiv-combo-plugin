using System;
using System.Collections.Frozen;
using System.Reflection;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace yetanotherffxivcomboplugin.src.Snapshot;

public sealed partial class GameSnapshot
{
    public static void InitializeRules(FrozenSet<uint> dispellableStatuses)
    {
        if (s_rulesInited) return;
        if (dispellableStatuses == null || dispellableStatuses.Count == 0) return;
        s_dispellableStatuses = dispellableStatuses;
        var lut = new bool[ushort.MaxValue + 1];
        int count = 0;
        foreach (var id in dispellableStatuses)
        {
            if (id <= ushort.MaxValue) { lut[(ushort)id] = true; count++; }
        }
        s_dispellableLut = lut;
        s_dispellableLutCount = count;
        s_lutInited = true;
        s_rulesInited = true;
    }

    internal static bool IsDispellableStatus(uint statusId)
    {
        if (s_lutInited && s_dispellableLut is { Length: > 0 } lut)
            return statusId <= ushort.MaxValue && lut[(ushort)statusId];
        return s_dispellableStatuses.Contains(statusId);
    }

    public static bool DispellableLutInitialized => s_lutInited;
    public static int DispellableLutCount => s_dispellableLutCount;

    public static bool TryGetDispellableLutSample(Span<ushort> buffer, out int filled)
    {
        filled = 0;
        if (!s_lutInited || s_dispellableLut is not { Length: > 0 } lut || buffer.Length == 0)
            return false;
        int f = 0;
        for (int i = 0; i < lut.Length && f < buffer.Length; i++)
        {
            if (lut[i]) buffer[f++] = (ushort)i;
        }
        filled = f;
        return f > 0;
    }

    public static bool IsInSanctuary => s_inSanctuary;

    public static void UpdateSanctuary(IDataManager data, ushort territoryId)
    {
        if (territoryId == 0 || data == null) { s_inSanctuary = false; return; }
        var sheet = data.GetExcelSheet<TerritoryType>();
        if (sheet == null) { s_inSanctuary = false; return; }
        var row = sheet.GetRow(territoryId);
        if (row.RowId == 0) { s_inSanctuary = false; return; }
        bool isTown = TryGetBoolProperty(row, "IsTown");
        bool isResidential = TryGetBoolProperty(row, "IsResidentialArea") || TryGetBoolProperty(row, "IsHousing") || TryGetBoolProperty(row, "IsResidential");
        s_inSanctuary = isTown || isResidential;
    }

    private static bool TryGetBoolProperty(object row, string propName)
    {
        var t = row.GetType();
        var p = t.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p == null) return false;
        var v = p.GetValue(row);
        if (v is bool b) return b;
        if (v is byte by) return by != 0;
        if (v is sbyte sb) return sb != 0;
        if (v is int i) return i != 0;
        if (v is uint ui) return ui != 0;
        return false;
    }
}
