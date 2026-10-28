using System.Collections.Generic;
using UnityEngine;

namespace ScoutingBeacons
{
    /// A placed beacon. The ZDO owner burns fuel and scouts ring by ring on the world clock; anyone who opens it
    /// gets the scouted land on their map. State in ZDO keys `sb_*`.
    public class Beacon : MonoBehaviour
    {
        private const string FuelKey = "sb_fuel", RingKey = "sb_ring", PaidKey = "sb_paid", ProgressKey = "sb_prog",
            LastKey = "sb_last", ActiveKey = "sb_active", CoastKey = "sb_coast";
        private const float TickSeconds = 1f;
        private const string ChestButlerManualOnly = "psort_manual";

        public static readonly List<Beacon> Instances = new List<Beacon>();

        private ZNetView m_nview;
        private Container m_container;
        private BeaconTier m_tier;
        private float m_timer;
        private readonly List<Transform> m_wisps = new List<Transform>();

        public BeaconTier Tier => m_tier ?? (m_tier = BeaconTiers.ForPiece(Utils.GetPrefabName(gameObject)));
        public Container Container => m_container;
        public Landmass Land => Landmass.For(transform.position);
        private ZDO Zdo => m_nview && m_nview.IsValid() ? m_nview.GetZDO() : null;

        public float Fuel => Zdo?.GetFloat(FuelKey) ?? 0f;
        public int Ring => Zdo?.GetInt(RingKey) ?? 0;
        public bool Paid => (Zdo?.GetInt(PaidKey) ?? 0) == 1;
        public float Progress => Zdo?.GetFloat(ProgressKey) ?? 0f;
        public bool Active => (Zdo?.GetInt(ActiveKey) ?? 0) == 1;
        public bool CoastReached => (Zdo?.GetInt(CoastKey) ?? 0) == 1;

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
            m_container = GetComponent<Container>();
            Instances.Add(this);
        }

        private void OnDestroy() => Instances.Remove(this);

        private void Update()
        {
            UpdateWisps();
            if (Zdo == null || !m_nview.IsOwner() || Tier == null) return;
            m_timer += Time.deltaTime;
            if (m_timer < TickSeconds) return;
            m_timer = 0f;
            Tick();
        }

        /// Spends the world time since the last tick: pay a ring, scout its land, next ring.
        private void Tick()
        {
            var zdo = Zdo;
            // ChestButler reads this key: with it set, its sorter never sorts items into this container
            if (Compat.ChestButlerInstalled && zdo.GetBool(ChestButlerManualOnly) != Settings.BlockAutoFeeding.Value)
                zdo.Set(ChestButlerManualOnly, Settings.BlockAutoFeeding.Value);
            double now = ZNet.instance.GetTimeSeconds();
            long last = zdo.GetLong(LastKey);
            zdo.Set(LastKey, (long)now);
            if (last == 0) return;
            double dt = now - last;
            if (dt <= 0) return;

            float fuel = Fuel, progress = Progress;
            int ring = Ring;
            bool paid = Paid, coast = false;
            float speed = Tier.SquareMetresPerSecond, cellArea = Land.Width * Land.Width;
            for (int guard = 0; guard < 500; guard++)
            {
                if (!Land.HasMoreAfter(ring)) { coast = true; break; }
                int next = ring + 1;
                if (!paid)
                {
                    float cost = RingCost(next);
                    if (fuel < cost) break;
                    fuel -= cost;
                    paid = true;
                }
                float need = RingCells(next) * cellArea - progress;
                double time = need / speed;
                if (dt < time) { progress += (float)(dt * speed); break; }
                dt -= time;
                ring = next;
                progress = 0f;
                paid = false;
            }
            zdo.Set(FuelKey, fuel);
            zdo.Set(RingKey, ring);
            zdo.Set(PaidKey, paid ? 1 : 0);
            zdo.Set(ProgressKey, progress);
            zdo.Set(CoastKey, coast ? 1 : 0);
            zdo.Set(ActiveKey, paid && !coast ? 1 : 0);
        }

        // ---- rings the builder had already explored ----

        private const string KnownKey = "sb_known";
        private byte[] m_known;

        /// Rings whose land the builder's map already showed when the beacon was placed: free and instant.
        public bool IsKnown(int n)
        {
            if (m_known == null) m_known = Zdo?.GetByteArray(KnownKey) ?? new byte[0];
            int i = n >> 3;
            return n > 0 && i < m_known.Length && (m_known[i] & (1 << (n & 7))) != 0;
        }

        public float RingCost(int n) => IsKnown(n) ? 0f : Tier.RingCost(n);
        public int RingCells(int n) => IsKnown(n) ? 0 : Land.CellsInRing(n);

        /// Called on the builder's client right after placing: marks every ring whose land cells are all explored on
        /// the builder's own map (other players' maps don't count), up to well past the area limit.
        public void SnapshotKnownMap()
        {
            var map = Minimap.instance;
            if (!map || Zdo == null || Tier == null) return;
            int max = Tier.LimitRing * 2;
            var bits = new byte[max / 8 + 1];
            int known = 0;
            for (int n = 1; n <= max; n++)
            {
                if (!Land.HasMoreAfter(n - 1)) break; // past the coast
                bool all = true;
                foreach (long key in Land.CellsOfRing(n))
                    if (!map.IsExplored(Land.CellCentre(key))) { all = false; break; }
                if (!all) continue;
                bits[n >> 3] |= (byte)(1 << (n & 7));
                known++;
            }
            Zdo.Set(KnownKey, bits);
            m_known = bits;
            if (known > 0)
                Jotunn.Logger.LogInfo($"{Tier.DisplayName}: {known} rings already on the builder's map, skipped");
        }

        /// Fuel for rings 1 to the area limit, without the rings the builder already knew.
        public float FuelToLimit()
        {
            float total = 0f;
            for (int n = 1; n <= Tier.LimitRing; n++) total += RingCost(n);
            return total;
        }

        // ---- fuel ----

        /// Turns every fuel item in the beacon's inventory into fuel. Returns the fuel added.
        public float Feed(bool dropOthers)
        {
            var inventory = m_container ? m_container.GetInventory() : null;
            if (inventory == null || Zdo == null) return 0f;
            if (!m_nview.IsOwner()) m_nview.ClaimOwnership();
            float added = 0f;
            foreach (var item in new List<ItemDrop.ItemData>(inventory.GetAllItems()))
            {
                float value = Settings.FuelValue(item);
                if (value > 0f)
                {
                    added += value * item.m_stack;
                    inventory.RemoveItem(item);
                }
                else if (dropOthers)
                {
                    ItemDrop.DropItem(item, item.m_stack, transform.position + transform.forward * 1.5f + Vector3.up, Quaternion.identity);
                    inventory.RemoveItem(item);
                }
            }
            if (added > 0f)
            {
                Zdo.Set(FuelKey, Fuel + added);
                if (Zdo.GetLong(LastKey) == 0) Zdo.Set(LastKey, (long)ZNet.instance.GetTimeSeconds());
                m_timer = TickSeconds; // pay the next ring straight away
            }
            return added;
        }

        public float PendingFuel(out List<string> nonFuel)
        {
            nonFuel = new List<string>();
            float total = 0f;
            var inventory = m_container ? m_container.GetInventory() : null;
            if (inventory == null) return 0f;
            foreach (var item in inventory.GetAllItems())
            {
                float v = Settings.FuelValue(item);
                if (v > 0f) total += v * item.m_stack;
                else nonFuel.Add($"{item.m_stack} {Localization.instance.Localize(item.m_shared.m_name)}");
            }
            return total;
        }

        public struct Forecast
        {
            public int Ring;          // last ring that will be finished
            public int Cells;         // new land cells scouted
            public double Seconds;    // world time needed
            public float Leftover;    // fuel left once it stops
            public bool Coast;        // stops at the coast rather than for lack of fuel
            public float NextCost;    // cost of the first ring it can't pay
        }

        /// What the beacon will do with `fuel` from where it is now.
        public Forecast Predict(float fuel)
        {
            var f = new Forecast { Ring = Ring };
            float progress = Progress, speed = Tier.SquareMetresPerSecond, cellArea = Land.Width * Land.Width;
            bool paid = Paid;
            for (int guard = 0; guard < 2000; guard++)
            {
                if (!Land.HasMoreAfter(f.Ring)) { f.Coast = true; break; }
                int next = f.Ring + 1;
                float cost = RingCost(next);
                if (!paid)
                {
                    if (fuel < cost) { f.NextCost = cost; break; }
                    fuel -= cost;
                }
                int cells = RingCells(next);
                f.Seconds += (cells * cellArea - progress) / speed;
                f.Cells += cells;
                f.Ring = next;
                progress = 0f;
                paid = false;
            }
            f.Leftover = fuel;
            return f;
        }

        // ---- map ----

        /// Copies the finished rings onto the local player's map. Returns the number of newly revealed map pixels.
        public int TransferMap()
        {
            var map = Minimap.instance;
            if (!map || Tier == null) return 0;
            float w = Land.Width, buffer = Settings.CoastBuffer.Value * w;
            float reach = w * 0.5f + buffer;
            float step = Mathf.Max(1f, Mathf.Min(w, map.m_pixelSize) * 0.5f);
            var pixels = new HashSet<long>();
            foreach (long key in Land.CellsUpTo(Ring))
            {
                var c = Land.CellCentre(key);
                for (float dx = -reach; dx <= reach; dx += step)
                for (float dz = -reach; dz <= reach; dz += step)
                {
                    map.WorldToPixel(c + new Vector3(dx, 0f, dz), out int px, out int py);
                    pixels.Add(((long)px << 32) | (uint)py);
                }
            }
            int revealed = 0;
            foreach (long p in pixels)
                if (map.Explore((int)(p >> 32), (int)(uint)p)) revealed++;
            if (revealed > 0) map.m_fogTexture.Apply();
            return revealed;
        }

        // ---- looks ----

        private static readonly Color PastLimitColor = new Color(1f, 0.15f, 0.1f);
        private bool m_wispsRed;

        private void UpdateWisps()
        {
            bool show = Active && Plugin.WispTemplate;
            int want = show && Tier != null ? Tier.Wisps : 0;
            // Scouting a ring past the area limit: red, pulsing wisps (rebuilt when that changes)
            bool red = show && Ring + 1 > Tier.LimitRing;
            if (red != m_wispsRed)
            {
                m_wispsRed = red;
                foreach (var w in m_wisps) Destroy(w.gameObject);
                m_wisps.Clear();
            }
            while (m_wisps.Count > want)
            {
                Destroy(m_wisps[m_wisps.Count - 1].gameObject);
                m_wisps.RemoveAt(m_wisps.Count - 1);
            }
            while (m_wisps.Count < want)
            {
                var wisp = Instantiate(Plugin.WispTemplate, transform);
                if (red) Recolour(wisp, PastLimitColor);
                wisp.SetActive(true);
                m_wisps.Add(wisp.transform);
            }
            float pulse = red ? 0.75f + 0.35f * Mathf.Sin(Time.time * 6f) : 1f;
            for (int i = 0; i < m_wisps.Count; i++)
            {
                m_wisps[i].localScale = Plugin.WispTemplate.transform.localScale * pulse;
                foreach (var light in m_wisps[i].GetComponentsInChildren<Light>()) light.intensity = 1.5f * pulse;
                float a = Time.time * 0.8f + i * Mathf.PI * 2f / m_wisps.Count;
                m_wisps[i].position = transform.position +
                    new Vector3(Mathf.Cos(a) * 1.4f, 2.2f + Mathf.Sin(Time.time * 1.7f + i) * 0.25f, Mathf.Sin(a) * 1.4f);
            }
        }

        private static void Recolour(GameObject wisp, Color color)
        {
            foreach (var light in wisp.GetComponentsInChildren<Light>(true)) light.color = color;
            foreach (var ps in wisp.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startColor = color;
            }
            foreach (var r in wisp.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.materials)
                {
                    if (m.HasProperty("_Color")) m.color = color;
                    if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", color);
                    if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", color * 2f);
                }
        }

        public string GetHoverText(string vanilla)
        {
            string state = CoastReached ? "Coast reached" : Active ? "Scouting" : "Needs fuel";
            return vanilla + $"\n<size=80%>{state}  |  ring {Ring}  |  fuel {Fuel:0.#}</size>";
        }
    }
}
