using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace ScoutingBeacons
{
    /// Optional support for other mods (same approach as Death & Taxes). None of them is required.
    /// With "Block automatic feeding" on (default), mods that move items between containers leave beacons alone:
    /// nothing is stored into a beacon automatically, and nothing is taken out of it to fuel smelters, fires or tames.
    public static class Compat
    {
        public static bool ChestButlerInstalled => Chainloader.PluginInfos.ContainsKey("eksolutions.chestbutler");

        private static bool IsBeacon(string prefabName) => prefabName != null && BeaconTiers.ForPiece(Utils.GetPrefabName(prefabName)) != null;

        // ---- OttoFuel (every client): fuels smelters, kilns, fires ... from every container in range ----

        public const string OttoFuelGuid = "potto007.OttoFuel";

        public static void PatchOttoFuel(Harmony harmony)
        {
            if (!Chainloader.PluginInfos.ContainsKey(OttoFuelGuid)) return;
            try
            {
                var utils = AccessTools.TypeByName("OttoFuel.TastyUtils");
                var nearby = utils != null ? AccessTools.Method(utils, "GetNearbyContainers") : null;
                if (nearby == null || nearby.ReturnType != typeof(List<Container>))
                {
                    Jotunn.Logger.LogWarning("OttoFuel found but not the expected code; it may take fuel out of beacons");
                    return;
                }
                harmony.Patch(nearby, postfix: new HarmonyMethod(typeof(Compat), nameof(NoOttoFuelFromBeacons)));
                Jotunn.Logger.LogInfo("OttoFuel: beacons skipped while \"Block automatic feeding\" is on");
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning($"Couldn't adjust OttoFuel: {e.Message}");
            }
        }

        private static void NoOttoFuelFromBeacons(List<Container> __result)
        {
            if (__result == null || !Settings.BlockAutoFeeding.Value) return;
            __result.RemoveAll(c => c && IsBeacon(c.gameObject.name));
        }

        // ---- ServersideQoL (server only): AutoStore puts items into containers, AutoProcess/TameAssist take out ----

        public const string ServersideQoLGuid = "ArgusMagnus.ServersideQoL";

        /// Called from Plugin.Start, after every plugin has loaded. ServersideQoL keeps its state per container, so a
        /// changed setting applies after a server restart.
        public static void PatchServersideQoL(Harmony harmony)
        {
            if (!Chainloader.PluginInfos.ContainsKey(ServersideQoLGuid)) return;
            try
            {
                var registry = AccessTools.TypeByName("ServersideQoL.Processors.ContainerRegistryProcessor");
                var processor = AccessTools.TypeByName("ServersideQoL.Processor");
                var getState = registry != null ? AccessTools.Method(registry, "GetStateCore") : null;
                var initPrefab = processor != null ? AccessTools.Method(processor, "InitializePrefabInfoInternal") : null;
                if (getState == null || initPrefab == null)
                {
                    Jotunn.Logger.LogWarning("ServersideQoL found but not the expected code; AutoStore may store into beacons");
                    return;
                }
                harmony.Patch(getState, postfix: new HarmonyMethod(typeof(Compat), nameof(NoAutoStoreIntoBeacons)));
                harmony.Patch(initPrefab, prefix: new HarmonyMethod(typeof(Compat), nameof(NoAutoSortOfBeacons)));
                Jotunn.Logger.LogInfo("ServersideQoL: beacons excluded from AutoStore, AutoProcess and TameAssist while \"Block automatic feeding\" is on");
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning($"Couldn't adjust ServersideQoL: {e.Message}");
            }
        }

        /// A beacon's container state gets pickup and feed ranges 0 (what ServersideQoL's chest signs "🧲0" / "↔️0" do).
        private static void NoAutoStoreIntoBeacons(Container container, object __result)
        {
            if (__result == null || !container || !Settings.BlockAutoFeeding.Value || !IsBeacon(container.gameObject.name)) return;
            SetRangeZero(__result, "AutoStorePickupRange");
            SetRangeZero(__result, "AutoProcessFeedRange");
            SetRangeZero(__result, "TameAssistFeedRange");
        }

        private static void SetRangeZero(object state, string name)
        {
            var range = AccessTools.Property(state.GetType(), name);
            if (range == null || !range.CanWrite || Equals(range.GetValue(state), 0f)) return;
            range.SetValue(state, range.PropertyType == typeof(float) ? (object)0f : (float?)0f);
        }

        /// AutoStore's sorting is never attached to beacons.
        private static bool NoAutoSortOfBeacons(object __instance, object prefabInfo, ref bool __result)
        {
            if (!Settings.BlockAutoFeeding.Value || __instance == null ||
                __instance.GetType().FullName != "ServersideQoL.AutoStore.ContainerProcessor") return true;
            string name = Traverse.Create(prefabInfo).Property("PrefabName").GetValue<string>();
            if (!IsBeacon(name)) return true;
            __result = false;
            return false;
        }
    }
}
