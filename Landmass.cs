using System.Collections.Generic;
using UnityEngine;

namespace ScoutingBeacons
{
    /// The land a beacon can reach: a grid of RingWidth cells around the beacon, flood-filled over non-ocean cells.
    /// The world comes from its seed, so every client works this out the same way. Built ring by ring on demand.
    public class Landmass
    {
        private static readonly Dictionary<string, Landmass> s_cache = new Dictionary<string, Landmass>();

        public readonly Vector3 Origin;
        public readonly float Width;
        private readonly HashSet<long> m_seen = new HashSet<long>();
        private readonly SortedDictionary<int, List<long>> m_pending = new SortedDictionary<int, List<long>>();
        private readonly List<List<long>> m_rings = new List<List<long>>(); // land cells per ring
        private readonly Dictionary<long, int> m_ringOf = new Dictionary<long, int>();
        private int m_builtTo = -1;

        public static Landmass For(Vector3 origin)
        {
            float w = Settings.RingWidth.Value;
            string key = $"{Mathf.RoundToInt(origin.x)}_{Mathf.RoundToInt(origin.z)}_{w}";
            if (!s_cache.TryGetValue(key, out var land)) s_cache[key] = land = new Landmass(origin, w);
            return land;
        }

        private Landmass(Vector3 origin, float width)
        {
            Origin = origin;
            Width = width;
            long start = Key(0, 0);
            m_seen.Add(start);
            m_rings.Add(new List<long> { start }); // ring 0: where the beacon stands, land or not
            m_ringOf[start] = 0;
            m_builtTo = 0;
            Expand(0, 0);
        }

        private static long Key(int i, int j) => ((long)i << 32) | (uint)j;
        private static int I(long key) => (int)(key >> 32);
        private static int J(long key) => (int)(uint)key;

        public Vector3 CellCentre(long key) => Origin + new Vector3(I(key) * Width, 0f, J(key) * Width);

        private int RingOf(int i, int j) => Mathf.RoundToInt(Mathf.Sqrt(i * i + j * j));

        private bool IsLand(int i, int j)
        {
            var p = Origin + new Vector3(i * Width, 0f, j * Width);
            return WorldGenerator.instance == null || WorldGenerator.instance.GetBiome(p) != Heightmap.Biome.Ocean;
        }

        private void Expand(int i, int j)
        {
            for (int di = -1; di <= 1; di++)
            for (int dj = -1; dj <= 1; dj++)
            {
                if (di == 0 && dj == 0) continue;
                long key = Key(i + di, j + dj);
                if (!m_seen.Add(key)) continue;
                int ring = RingOf(i + di, j + dj);
                if (!m_pending.TryGetValue(ring, out var list)) m_pending[ring] = list = new List<long>();
                list.Add(key);
            }
        }

        /// Classifies every cell up to ring n.
        public void Build(int n)
        {
            while (m_builtTo < n)
            {
                int ring = m_builtTo + 1;
                while (m_rings.Count <= ring) m_rings.Add(new List<long>());
                // Cells found while expanding this ring may themselves be on this ring: loop until none are left
                while (m_pending.TryGetValue(ring, out var list))
                {
                    m_pending.Remove(ring);
                    foreach (long key in list)
                    {
                        if (!IsLand(I(key), J(key))) continue;
                        m_rings[ring].Add(key);
                        m_ringOf[key] = ring;
                        Expand(I(key), J(key));
                    }
                }
                // Neighbours that fall on an earlier ring (diagonals) join this one
                var early = new List<int>();
                foreach (var kv in m_pending) { if (kv.Key < ring) early.Add(kv.Key); else break; }
                foreach (int r in early)
                {
                    var list = m_pending[r];
                    m_pending.Remove(r);
                    foreach (long key in list)
                    {
                        if (!IsLand(I(key), J(key))) continue;
                        m_rings[ring].Add(key);
                        m_ringOf[key] = ring;
                        Expand(I(key), J(key));
                    }
                }
                m_builtTo = ring;
            }
        }

        public int CellsInRing(int n)
        {
            Build(n);
            return n < m_rings.Count ? m_rings[n].Count : 0;
        }

        /// Is there land left to scout after ring n?
        public bool HasMoreAfter(int n)
        {
            Build(n + 1);
            if (CellsInRing(n + 1) > 0) return true;
            foreach (var kv in m_pending) if (kv.Key > n + 1 && kv.Value.Count > 0) return true;
            return false;
        }

        /// Is this point on land the beacon has scouted by the end of ring n? (Its own cell or a neighbouring one, so
        /// things on the shoreline count too.)
        public bool Scouted(Vector3 p, int n)
        {
            Build(n);
            int i = Mathf.RoundToInt((p.x - Origin.x) / Width), j = Mathf.RoundToInt((p.z - Origin.z) / Width);
            for (int di = -1; di <= 1; di++)
            for (int dj = -1; dj <= 1; dj++)
                if (m_ringOf.TryGetValue(Key(i + di, j + dj), out int r) && r <= n) return true;
            return false;
        }

        public IEnumerable<long> CellsOfRing(int n)
        {
            Build(n);
            return n < m_rings.Count ? m_rings[n] : new List<long>();
        }

        public IEnumerable<long> CellsUpTo(int n)
        {
            Build(n);
            for (int r = 0; r <= n && r < m_rings.Count; r++)
                foreach (long key in m_rings[r]) yield return key;
        }
    }
}
