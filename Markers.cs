using System.Collections.Generic;
using UnityEngine;

namespace ScoutingBeacons
{
    /// Pins for points of interest on a beacon's scouted land. Only the server knows every location in the world and
    /// every ore deposit that exists, so the opening player asks it (routed RPC) and pins the answer on their own map.
    public static class Markers
    {
        private const string RequestRpc = "ScoutingBeacons_RequestMarkers";
        private const string ReplyRpc = "ScoutingBeacons_Markers";
        private static readonly HashSet<string> s_unmatchedLogged = new HashSet<string>();
        private static Beacon s_waiting; // the beacon whose answer we expect (one at a time is enough: one window)

        public static void Register()
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.Register<ZPackage>(RequestRpc, OnRequest);
            ZRoutedRpc.instance.Register<ZPackage>(ReplyRpc, OnReply);
            if (ZNetScene.instance)
                foreach (var name in Settings.OreMarkers.Keys)
                    if (!ZNetScene.instance.GetPrefab(name))
                        Jotunn.Logger.LogWarning($"Ore marker '{name}' doesn't exist in this game, ignored");
        }

        private static bool s_typesLogged;

        /// Server, once (at the first request, when the world is loaded): every location type placed in this world that
        /// nothing marks, so the lists can be completed. (ZoneSystem.m_locations misses the later biomes' lists.)
        private static void LogLocationTypes()
        {
            if (s_typesLogged || !ZoneSystem.instance) return;
            s_typesLogged = true;
            var unmatched = new SortedSet<string>();
            foreach (var instance in ZoneSystem.instance.m_locationInstances.Values)
            {
                string name = Name(instance);
                if (name.Length > 0 && Settings.LocationLabel(name) == null && Settings.TraderFor(name) == null &&
                    Settings.OreLocationLabel(name) == null) unmatched.Add(name);
            }
            Jotunn.Logger.LogInfo($"Location types without a marker ({unmatched.Count}): {string.Join(", ", unmatched)}");
        }

        private static string Name(ZoneSystem.LocationInstance instance) =>
            instance.m_location?.m_prefabName ?? instance.m_location?.m_name ?? "";

        /// Called when the local player opens a beacon (after its map is copied).
        public static void Request(Beacon beacon)
        {
            var tier = beacon.Tier;
            if (tier == null || ZRoutedRpc.instance == null) return;
            bool locations = tier.MarkLocations.Value, ores = tier.MarkOres.Value;
            if (!locations && !ores) return;
            s_waiting = beacon;
            var pkg = new ZPackage();
            pkg.Write(beacon.transform.position);
            pkg.Write((beacon.Ring + 2) * beacon.Land.Width); // search radius: the scouted rings plus a margin
            pkg.Write(locations);
            pkg.Write(ores);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RequestRpc, pkg);
        }

        // ---- server ----

        private static void OnRequest(long sender, ZPackage pkg)
        {
            if (!ZNet.instance || !ZNet.instance.IsServer()) return;
            LogLocationTypes();
            Vector3 centre = pkg.ReadVector3();
            float radius = pkg.ReadSingle();
            bool locations = pkg.ReadBool(), ores = pkg.ReadBool();
            var found = new List<KeyValuePair<string, Vector3>>();
            if (locations) FindLocations(centre, radius, found);
            if (ores) { FindOres(centre, radius, found); FindOreLocations(centre, radius, found); }

            var reply = new ZPackage();
            reply.Write(found.Count);
            foreach (var kv in found)
            {
                reply.Write(kv.Key);
                reply.Write(kv.Value);
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, ReplyRpc, reply);
        }

        private static bool Near(Vector3 a, Vector3 b, float radius)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz <= radius * radius;
        }

        private static void FindLocations(Vector3 centre, float radius, List<KeyValuePair<string, Vector3>> found)
        {
            var zones = ZoneSystem.instance;
            if (!zones) return;
            // Traders: once the game has placed one (someone came near it), only the placed ones; before that every
            // possible spot, as "<name> (possible)"
            var placedTraders = new HashSet<Settings.Trader>();
            foreach (var instance in zones.m_locationInstances.Values)
            {
                var trader = Settings.TraderFor(Name(instance));
                if (trader != null && instance.m_placed) placedTraders.Add(trader);
            }
            foreach (var instance in zones.m_locationInstances.Values)
            {
                if (!Near(instance.m_position, centre, radius)) continue;
                string name = Name(instance);
                var trader = Settings.TraderFor(name);
                if (trader != null)
                {
                    if (!trader.Enabled.Value) continue;
                    if (placedTraders.Contains(trader))
                    {
                        if (instance.m_placed) found.Add(new KeyValuePair<string, Vector3>(trader.Label, instance.m_position));
                    }
                    else found.Add(new KeyValuePair<string, Vector3>(trader.Label + " (possible)", instance.m_position));
                    continue;
                }
                string label = Settings.LocationLabel(name);
                if (label != null) found.Add(new KeyValuePair<string, Vector3>(label, instance.m_position));
                else if (Settings.OreLocationLabel(name) == null && s_unmatchedLogged.Add(name))
                    Jotunn.Logger.LogInfo($"Location '{name}' isn't marked (add it to 06 - Markers / Locations to pin it)");
            }
        }

        /// Ore that comes with a location (flametal in lava leviathans, brains in giant skeletons): every location is
        /// known for the whole world, so these are exact even where nobody has been.
        private static void FindOreLocations(Vector3 centre, float radius, List<KeyValuePair<string, Vector3>> found)
        {
            var zones = ZoneSystem.instance;
            if (!zones) return;
            foreach (var instance in zones.m_locationInstances.Values)
            {
                if (!Near(instance.m_position, centre, radius)) continue;
                string label = Settings.OreLocationLabel(Name(instance));
                if (label != null) found.Add(new KeyValuePair<string, Vector3>(label, instance.m_position));
            }
        }

        private static readonly List<ZDO> s_zdos = new List<ZDO>();

        private static void FindOres(Vector3 centre, float radius, List<KeyValuePair<string, Vector3>> found)
        {
            if (ZDOMan.instance == null) return;
            foreach (var kv in Settings.OreMarkers)
            {
                s_zdos.Clear();
                int index = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(kv.Key, s_zdos, ref index)) { }
                foreach (var zdo in s_zdos)
                    if (Near(zdo.GetPosition(), centre, radius))
                        found.Add(new KeyValuePair<string, Vector3>(kv.Value, zdo.GetPosition()));
            }
            s_zdos.Clear();
            if (Settings.PredictOres.Value) PredictOres(centre, radius, found);
        }

        public const string PredictedSuffix = ""; // predicted pins look like real ones (user, 2026-10-28)

        /// Zones in range nobody has been near yet: their ore doesn't exist yet, so predict it (same label as real ones).
        private static void PredictOres(Vector3 centre, float radius, List<KeyValuePair<string, Vector3>> found)
        {
            var zones = ZoneSystem.instance;
            if (!zones) return;
            var predicted = new List<KeyValuePair<string, Vector3>>();
            var min = ZoneSystem.GetZone(centre - new Vector3(radius, 0f, radius));
            var max = ZoneSystem.GetZone(centre + new Vector3(radius, 0f, radius));
            for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
            {
                var zone = new Vector2s(x, y);
                if (zones.IsZoneGenerated(zone)) continue; // real deposits were found above
                predicted.Clear();
                OrePredictor.Predict(zone, Settings.OreMarkers, predicted);
                foreach (var kv in predicted)
                    if (Near(kv.Value, centre, radius))
                        found.Add(new KeyValuePair<string, Vector3>(kv.Key + PredictedSuffix, kv.Value));
            }
        }

        // ---- client ----

        private const float SameOreDistance = 24f; // ore pins closer than this are merged (a vein is several rocks)

        private static void OnReply(long sender, ZPackage pkg)
        {
            var beacon = s_waiting;
            var map = Minimap.instance;
            if (!beacon || !map || !Player.m_localPlayer) return;
            int count = pkg.ReadInt();
            var placed = new List<KeyValuePair<string, Vector3>>();
            int added = 0;
            for (int i = 0; i < count; i++)
            {
                string label = pkg.ReadString();
                Vector3 pos = pkg.ReadVector3();
                if (!beacon.Land.Scouted(pos, beacon.Ring)) continue; // only on land this beacon has scouted
                bool ore = Settings.OreMarkers.ContainsValue(label) || Settings.OreLocations.ContainsValue(label);
                float merge = ore ? SameOreDistance : 5f;
                if (placed.Exists(p => p.Key == label && Near(p.Value, pos, merge)) || HasPin(map, label, pos, merge)) continue;
                bool predicted = PredictedSuffix.Length > 0 && label.EndsWith(PredictedSuffix);
                string real = predicted ? label.Substring(0, label.Length - PredictedSuffix.Length) : label;
                if (predicted && HasPin(map, real, pos, merge)) continue; // the real deposit is already pinned
                if (ore && !predicted && PredictedSuffix.Length > 0) RemovePins(map, label + PredictedSuffix, pos, merge); // real one replaces the guess
                placed.Add(new KeyValuePair<string, Vector3>(label, pos));
                map.AddPin(pos, ore ? Minimap.PinType.Icon3 : Minimap.PinType.Icon2, label, true, false, 0L, default);
                added++;
            }
            if (added > 0)
                Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, $"{beacon.Tier.DisplayName}: {added} new markers");
        }

        private static void RemovePins(Minimap map, string label, Vector3 pos, float distance)
        {
            foreach (var pin in new List<Minimap.PinData>(map.m_pins))
                if (pin.m_name == label && Near(pin.m_pos, pos, distance)) map.RemovePin(pin);
        }

        private static bool HasPin(Minimap map, string label, Vector3 pos, float distance)
        {
            foreach (var pin in map.m_pins)
                if (pin.m_name == label && Near(pin.m_pos, pos, distance)) return true;
            return false;
        }
    }
}
