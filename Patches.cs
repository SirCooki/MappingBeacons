using HarmonyLib;

namespace ScoutingBeacons
{
    [HarmonyPatch]
    internal static class Patches
    {
        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
        [HarmonyPostfix]
        private static void CheckFuel() => Settings.CheckFuelNames();

        // Opening a beacon copies what it scouted onto your map.
        [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
        [HarmonyPostfix]
        private static void Interact(Container __instance, Humanoid character, bool hold, bool __result)
        {
            if (!__result || hold || character != Player.m_localPlayer) return;
            var beacon = __instance.GetComponent<Beacon>();
            if (!beacon) return;
            if (beacon.TransferMap() > 0)
                Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, $"{beacon.Tier.DisplayName}: map updated");
            Markers.Request(beacon);
        }

        // Marker requests and answers, once the network is up (also on a dedicated server).
        [HarmonyPatch(typeof(Game), nameof(Game.Start))]
        [HarmonyPostfix]
        private static void RegisterRpcs() => Markers.Register();

        // Placing a beacon: remember which rings the builder's map already shows.
        [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
        [HarmonyPostfix]
        private static void SetCreator(Piece __instance, long uid)
        {
            if (!Player.m_localPlayer || uid != Player.m_localPlayer.GetPlayerID()) return;
            var beacon = __instance.GetComponent<Beacon>();
            if (beacon) beacon.SnapshotKnownMap();
        }

        [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
        [HarmonyPostfix]
        private static void GetHoverText(Container __instance, ref string __result)
        {
            var beacon = __instance.GetComponent<Beacon>();
            if (beacon) __result = beacon.GetHoverText(__result);
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        [HarmonyPostfix]
        private static void Show(InventoryGui __instance) => BeaconPanel.Ensure(__instance);

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
        [HarmonyPostfix]
        private static void GuiUpdate(InventoryGui __instance) => BeaconPanel.Refresh(__instance);

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.CloseContainer))]
        [HarmonyPrefix]
        private static void CloseContainer(InventoryGui __instance)
        {
            if (!Settings.ConvertOnClose.Value) return;
            var beacon = BeaconPanel.OpenBeacon(__instance);
            if (beacon) beacon.Feed(dropOthers: true);
        }

        // Destroyed by damage (health gone): no build materials back unless the setting allows it.
        // Taking it down with the hammer keeps its health, so that still returns them.
        [HarmonyPatch(typeof(Piece), nameof(Piece.DropResources))]
        [HarmonyPrefix]
        private static bool DropResources(Piece __instance)
        {
            if (Settings.DropResourcesOnDestroy.Value || !__instance.GetComponent<Beacon>()) return true;
            var wnt = __instance.GetComponent<WearNTear>();
            var zdo = wnt && wnt.m_nview && wnt.m_nview.IsValid() ? wnt.m_nview.GetZDO() : null;
            return zdo == null || zdo.GetFloat(ZDOVars.s_health, wnt.m_health) > 0f;
        }
    }
}
