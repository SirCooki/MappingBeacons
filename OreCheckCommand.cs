using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using UnityEngine;

namespace ScoutingBeacons
{
    /// Test command (single player / hosting): "sb_orecheck [radius]". For the zones around you that ARE generated,
    /// predicts the ore as if they weren't and compares with the deposits that really exist there. Also lists the
    /// ore-like vegetation names of this game version, to check the "06 - Markers / Ores" list.
    public class OreCheckCommand : ConsoleCommand
    {
        public override string Name => "sb_orecheck";
        public override string Help => "Scouting Beacons: compare predicted ore with real ore around you [radius, default 300]";
        public override bool IsCheat => true;

        private const float Match = 3f;

        public override void Run(string[] args)
        {
            var player = Player.m_localPlayer;
            var zones = ZoneSystem.instance;
            if (!player || !zones || !ZNet.instance || !ZNet.instance.IsServer())
            {
                Print("Run this in single player or as the host (needs the server's world data).");
                return;
            }
            float radius = args.Length > 0 && float.TryParse(args[0], out float r) ? r : 300f;
            var ores = Settings.OreMarkers;

            var names = zones.m_vegetation.Where(v => v.m_enable && v.m_prefab).Select(v => v.m_prefab.name)
                .Where(n => n.ToLower().Contains("copper") || n.ToLower().Contains("tin") || n.ToLower().Contains("silver") ||
                            n.ToLower().Contains("mud") || n.ToLower().Contains("obsidian") || n.ToLower().Contains("meteor") ||
                            n.ToLower().Contains("brain") || n.ToLower().Contains("flametal") || n.ToLower().Contains("ore"))
                .Distinct().OrderBy(n => n).ToList();
            Print($"Ore-like vegetation in this game: {string.Join(", ", names.Select(n => ores.ContainsKey(n) ? n : n + " (not in list)"))}");

            // Real deposits, per label, from the world data
            var real = new List<KeyValuePair<string, Vector3>>();
            var zdos = new List<ZDO>();
            foreach (var kv in ores)
            {
                zdos.Clear();
                int index = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(kv.Key, zdos, ref index)) { }
                foreach (var zdo in zdos)
                    if (Utils.DistanceXZ(zdo.GetPosition(), player.transform.position) <= radius)
                        real.Add(new KeyValuePair<string, Vector3>(kv.Value, zdo.GetPosition()));
            }

            var centre = player.transform.position;
            var min = ZoneSystem.GetZone(centre - new Vector3(radius, 0f, radius));
            var max = ZoneSystem.GetZone(centre + new Vector3(radius, 0f, radius));
            var predicted = new List<KeyValuePair<string, Vector3>>();
            int zonesChecked = 0;
            for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
            {
                var zone = new Vector2s(x, y);
                if (!zones.IsZoneGenerated(zone)) continue;
                if (Utils.DistanceXZ(ZoneSystem.GetZonePos(zone), centre) > radius) continue;
                zonesChecked++;
                OrePredictor.Predict(zone, ores, predicted);
            }
            // Only real deposits inside the zones we predicted count
            real = real.Where(kv => zones.IsZoneGenerated(ZoneSystem.GetZone(kv.Value)) &&
                                    Utils.DistanceXZ(ZoneSystem.GetZonePos(ZoneSystem.GetZone(kv.Value)), centre) <= radius).ToList();

            int hit = predicted.Count(p => real.Any(q => q.Key == p.Key && Utils.DistanceXZ(q.Value, p.Value) <= Match));
            int found = real.Count(q => predicted.Any(p => p.Key == q.Key && Utils.DistanceXZ(q.Value, p.Value) <= Match));
            float nearest = predicted.Count == 0 || real.Count == 0 ? 0f :
                predicted.Select(p => real.Where(q => q.Key == p.Key).Select(q => Utils.DistanceXZ(q.Value, p.Value))
                    .DefaultIfEmpty(999f).Min()).Average();
            Print($"{zonesChecked} generated zones within {radius:0} m: {real.Count} real deposits, {predicted.Count} predicted.");
            Print($"Predicted spots with a real deposit within {Match} m: {hit}/{predicted.Count}. " +
                  $"Real deposits predicted: {found}/{real.Count}. Average distance to the nearest real one: {nearest:0.0} m.");
            foreach (var label in real.Select(q => q.Key).Concat(predicted.Select(p => p.Key)).Distinct())
                Print($"  {label}: real {real.Count(q => q.Key == label)}, predicted {predicted.Count(p => p.Key == label)}, " +
                      $"matched {predicted.Count(p => p.Key == label && real.Any(q => q.Key == label && Utils.DistanceXZ(q.Value, p.Value) <= Match))}");
            Print("(Mined-out deposits are gone from the world data, so they count as 'predicted but not real'.)");
        }

        private static void Print(string text)
        {
            Console.instance?.Print(text);
            Jotunn.Logger.LogInfo("[sb_orecheck] " + text);
        }
    }
}
