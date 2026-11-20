using System.Collections.Generic;
using HarmonyLib;

namespace ScoutingBeacons
{
    /// SirCookie's pieces first in the hammer's build tabs: the tribute tables (Meadows -> Ashlands), then the beacons
    /// (bronze -> black metal). Death & Taxes and Valdar both carry this same patch with the same list, so the result
    /// is identical whichever of them runs, or both.
    [HarmonyPatch(typeof(PieceTable), nameof(PieceTable.UpdateAvailable))]
    internal static class MiscOrder
    {
        private static readonly string[] Order =
        {
            "tribute_meadows", "tribute_blackforest", "tribute_swamp", "tribute_mountains", "tribute_plains",
            "tribute_mistlands", "tribute_ashlands", "sb_beacon_bronze", "sb_beacon_silver", "sb_beacon_blackmetal",
        };

        private static void Postfix(PieceTable __instance)
        {
            if (__instance.m_availablePiecesByCategory == null) return;
            foreach (var list in __instance.m_availablePiecesByCategory)
            {
                if (list == null || list.Count == 0) continue;
                var ours = new List<Piece>();
                foreach (var name in Order)
                {
                    var p = list.Find(x => x && x.gameObject.name == name);
                    if (p) ours.Add(p);
                }
                if (ours.Count == 0) continue;
                list.RemoveAll(ours.Contains);
                list.InsertRange(0, ours);
            }
        }
    }
}
