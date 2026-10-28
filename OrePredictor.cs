using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ScoutingBeacons
{
    /// Predicts where the game will place ore deposits in a zone that hasn't been generated yet, by replaying
    /// ZoneSystem.PlaceVegetation for the ore entries only (Valheim 1.0.21). Each vegetation entry seeds the random
    /// generator from the world seed, the zone and its prefab name, so one entry can be replayed on its own.
    /// Known gaps: objects that would block a spot (trees, rocks placed earlier) can't be seen without loading the
    /// area, and terrain height/normal come from the world generator instead of the built terrain mesh.
    public static class OrePredictor
    {
        private const float ZoneSize = 64f;
        private const float WaterLevel = 30f;

        /// Adds predicted deposits of the given ore prefabs (prefab -> label) in this zone.
        public static void Predict(Vector2s zone, Dictionary<string, string> ores, List<KeyValuePair<string, Vector3>> found)
        {
            var zones = ZoneSystem.instance;
            var world = WorldGenerator.instance;
            if (!zones || world == null) return;
            Vector3 centre = ZoneSystem.GetZonePos(zone);
            var terrain = new ZoneTerrain(world, centre);
            var clear = ClearAreas(zones, zone);
            var state = Random.state;
            int seed = world.GetSeed();
            var surround = new List<float>();
            try
            {
                foreach (var veg in zones.m_vegetation)
                {
                    if (!veg.m_enable || !veg.m_prefab || !ores.TryGetValue(veg.m_prefab.name, out string label)) continue;
                    if (!terrain.HaveBiome(veg.m_biome)) continue;
                    Random.InitState(seed + zone.x * 4271 + zone.y * 9187 + veg.m_prefab.name.GetStableHashCode());
                    int wanted = 1;
                    if (veg.m_max < 1f)
                    {
                        if (Random.value > veg.m_max) continue;
                    }
                    else wanted = Random.Range((int)veg.m_min, (int)veg.m_max + 1);
                    float maxTilt = Mathf.Cos(Mathf.PI / 180f * veg.m_maxTilt);
                    float minTilt = Mathf.Cos(Mathf.PI / 180f * veg.m_minTilt);
                    float spread = 32f - veg.m_groupRadius;
                    surround.Clear();
                    int tries = veg.m_forcePlacement ? wanted * 50 : wanted;
                    int placedGroups = 0;
                    for (int i = 0; i < tries; i++)
                    {
                        var first = new Vector3(Random.Range(centre.x - spread, centre.x + spread), 0f,
                            Random.Range(centre.z - spread, centre.z + spread));
                        var sector = world.GetBiomeSector(first);
                        if ((veg.AltBiomeParent != null && !sector.AltBiomes.Any(x => x.m_name == veg.AltBiomeParent)) ||
                            sector.AltBiomes.Any(x => x.m_blockVegetationNames.Contains(veg.m_name)))
                            continue;
                        int groupSize = Random.Range(veg.m_groupSizeMin, veg.m_groupSizeMax + 1);
                        bool placedAny = false;
                        for (int j = 0; j < groupSize; j++)
                        {
                            Vector3 p = j == 0 ? first : RandomPointInRadius(first, veg.m_groupRadius);
                            Random.Range(0, 360);                               // rotation
                            Random.Range(veg.m_scaleMin, veg.m_scaleMax);       // scale
                            Random.Range(-veg.m_randTilt, veg.m_randTilt);      // tilt x
                            Random.Range(-veg.m_randTilt, veg.m_randTilt);      // tilt z
                            // (blocked by other objects: unknown here, assumed free)
                            p.y = terrain.Height(p.x, p.z);
                            Vector3 normal = terrain.Normal(p.x, p.z);
                            if ((veg.m_biome & terrain.Biome(p)) == 0 || (veg.m_biomeArea & terrain.BiomeArea) == 0) continue;
                            float altitude = p.y - WaterLevel;
                            if (altitude < veg.m_minAltitude || altitude > veg.m_maxAltitude) continue;
                            if (!veg.m_minVegetation.Equals(veg.m_maxVegetation))
                            {
                                float mask = terrain.VegetationMask(p);
                                if (mask > veg.m_maxVegetation || mask < veg.m_minVegetation) continue;
                            }
                            if (!veg.m_minOceanDepth.Equals(veg.m_maxOceanDepth))
                            {
                                float depth = terrain.OceanDepth(p);
                                if (depth < veg.m_minOceanDepth || depth > veg.m_maxOceanDepth) continue;
                            }
                            if (normal.y < maxTilt || normal.y > minTilt) continue;
                            if (veg.m_terrainDeltaRadius > 0f)
                            {
                                float delta = TerrainDelta(terrain, p, veg.m_terrainDeltaRadius);
                                if (delta > veg.m_maxTerrainDelta || delta < veg.m_minTerrainDelta) continue;
                            }
                            if (veg.m_minDistanceFromCenter > 0f || veg.m_maxDistanceFromCenter > 0f)
                            {
                                float d = Utils.LengthXZ(p);
                                if ((veg.m_minDistanceFromCenter > 0f && d < veg.m_minDistanceFromCenter) ||
                                    (veg.m_maxDistanceFromCenter > 0f && d > veg.m_maxDistanceFromCenter)) continue;
                            }
                            if (veg.m_inForest)
                            {
                                float forest = WorldGenerator.GetForestFactor(p);
                                if (forest < veg.m_forestTresholdMin || forest > veg.m_forestTresholdMax) continue;
                            }
                            if (veg.m_surroundCheckVegetation)
                            {
                                float sum = 0f;
                                for (int k = 0; k < veg.m_surroundCheckLayers; k++)
                                {
                                    float r = (float)(k + 1) / veg.m_surroundCheckLayers * veg.m_surroundCheckDistance;
                                    for (int l = 0; l < 6; l++)
                                    {
                                        float f = l / 6f * Mathf.PI * 2f;
                                        float m = terrain.VegetationMask(p + new Vector3(Mathf.Sin(f) * r, 0f, Mathf.Cos(f) * r));
                                        sum += m * ((1f - r) / (veg.m_surroundCheckDistance * 2f));
                                    }
                                }
                                surround.Add(sum);
                                if (surround.Count < 10) continue;
                                float max = surround.Max(), avg = surround.Average();
                                if (sum < avg + (max - avg) * veg.m_surroundBetterThanAverage) continue;
                            }
                            if (InsideClearArea(clear, p)) continue;
                            if (veg.m_chanceToUseGroundTilt > 0f) Random.value.GetHashCode(); // the game draws one more value here
                            found.Add(new KeyValuePair<string, Vector3>(label, p));
                            placedAny = true;
                        }
                        if (placedAny) placedGroups++;
                        if (placedGroups >= wanted) break;
                    }
                }
            }
            finally { Random.state = state; }
        }

        private static Vector3 RandomPointInRadius(Vector3 centre, float radius)
        {
            float f = Random.value * Mathf.PI * 2f;
            float d = Random.Range(0f, radius);
            return centre + new Vector3(Mathf.Sin(f) * d, 0f, Mathf.Cos(f) * d);
        }

        private static float TerrainDelta(ZoneTerrain terrain, Vector3 centre, float radius)
        {
            float high = -999999f, low = 999999f;
            for (int i = 0; i < 10; i++)
            {
                Vector2 o = Random.insideUnitCircle * radius;
                float h = terrain.Height(centre.x + o.x, centre.z + o.y);
                if (h < low) low = h;
                if (h > high) high = h;
            }
            return high - low;
        }

        private struct Area { public Vector3 Centre; public float Radius; }

        private static List<Area> ClearAreas(ZoneSystem zones, Vector2s zone)
        {
            var list = new List<Area>();
            if (zones.m_locationInstances.TryGetValue(zone, out var instance) && instance.m_location.m_clearArea)
                list.Add(new Area { Centre = instance.m_position, Radius = instance.m_location.m_exteriorRadius });
            return list;
        }

        private static bool InsideClearArea(List<Area> areas, Vector3 p)
        {
            foreach (var a in areas)
                if (p.x > a.Centre.x - a.Radius && p.x < a.Centre.x + a.Radius && p.z > a.Centre.z - a.Radius && p.z < a.Centre.z + a.Radius)
                    return true;
            return false;
        }

        /// The zone's terrain as HeightmapBuilder builds it: 65 x 65 vertices, 1 m apart, heights blended between the
        /// four corner biomes with a smoothstep; biome per point from the corner weights like Heightmap.GetBiome.
        private class ZoneTerrain
        {
            private readonly WorldGenerator m_world;
            private readonly float m_minX, m_minZ;
            private readonly Heightmap.Biome b0, b1, b2, b3;      // WorldGenerator.GetBiome at the corners (heights)
            private readonly Heightmap.Biome s0, s1, s2, s3;      // corner biome sectors (biome lookup, edge, HaveBiome)
            private readonly float[] m_depth = new float[4];
            public readonly Heightmap.BiomeArea BiomeArea;
            private readonly Dictionary<int, KeyValuePair<float, float>> m_vertices = new Dictionary<int, KeyValuePair<float, float>>();

            public ZoneTerrain(WorldGenerator world, Vector3 centre)
            {
                m_world = world;
                m_minX = centre.x - ZoneSize / 2f;
                m_minZ = centre.z - ZoneSize / 2f;
                float maxX = m_minX + ZoneSize, maxZ = m_minZ + ZoneSize;
                b0 = world.GetBiome(m_minX, m_minZ); b1 = world.GetBiome(maxX, m_minZ);
                b2 = world.GetBiome(m_minX, maxZ); b3 = world.GetBiome(maxX, maxZ);
                s0 = world.GetBiomeSector(m_minX, m_minZ).Biome; s1 = world.GetBiomeSector(maxX, m_minZ).Biome;
                s2 = world.GetBiomeSector(m_minX, maxZ).Biome; s3 = world.GetBiomeSector(maxX, maxZ).Biome;
                BiomeArea = s0 == s1 && s0 == s2 && s0 == s3 ? Heightmap.BiomeArea.Median : Heightmap.BiomeArea.Edge;
                // Ocean depth per corner (approximation: water level minus the corner's height, never below 0)
                m_depth[0] = Mathf.Max(0f, WaterLevel - Vertex(0, 0).Key);
                m_depth[1] = Mathf.Max(0f, WaterLevel - Vertex(64, 0).Key);
                m_depth[2] = Mathf.Max(0f, WaterLevel - Vertex(64, 64).Key);
                m_depth[3] = Mathf.Max(0f, WaterLevel - Vertex(0, 64).Key);
            }

            public bool HaveBiome(Heightmap.Biome biome) =>
                (s0 & biome) != 0 || (s1 & biome) != 0 || (s2 & biome) != 0 || (s3 & biome) != 0;

            /// Height and vegetation mask (alpha) of vertex (l, k), cached.
            private KeyValuePair<float, float> Vertex(int l, int k)
            {
                l = Mathf.Clamp(l, 0, 64);
                k = Mathf.Clamp(k, 0, 64);
                int key = k * 65 + l;
                if (m_vertices.TryGetValue(key, out var v)) return v;
                float wx = m_minX + l, wy = m_minZ + k;
                float h;
                Color mask;
                if (b1 == b0 && b2 == b0 && b3 == b0) h = m_world.GetBiomeHeight(b0, wx, wy, out mask);
                else
                {
                    float t = DUtils.SmoothStep(0f, 1f, k / 64f), t2 = DUtils.SmoothStep(0f, 1f, l / 64f);
                    float h0 = m_world.GetBiomeHeight(b0, wx, wy, out var m0);
                    float h1 = m_world.GetBiomeHeight(b1, wx, wy, out var m1);
                    float h2 = m_world.GetBiomeHeight(b2, wx, wy, out var m2);
                    float h3 = m_world.GetBiomeHeight(b3, wx, wy, out var m3);
                    h = DUtils.Lerp(DUtils.Lerp(h0, h1, t2), DUtils.Lerp(h2, h3, t2), t);
                    mask = Color.Lerp(Color.Lerp(m0, m1, t2), Color.Lerp(m2, m3, t2), t);
                }
                v = new KeyValuePair<float, float>(h, mask.a);
                m_vertices[key] = v;
                return v;
            }

            public float Height(float x, float z)
            {
                float fx = x - m_minX, fz = z - m_minZ;
                int l = Mathf.FloorToInt(fx), k = Mathf.FloorToInt(fz);
                float tx = fx - l, tz = fz - k;
                float a = Mathf.Lerp(Vertex(l, k).Key, Vertex(l + 1, k).Key, tx);
                float b = Mathf.Lerp(Vertex(l, k + 1).Key, Vertex(l + 1, k + 1).Key, tx);
                return Mathf.Lerp(a, b, tz);
            }

            public Vector3 Normal(float x, float z)
            {
                const float e = 0.5f;
                float dx = Height(x + e, z) - Height(x - e, z);
                float dz = Height(x, z + e) - Height(x, z - e);
                return new Vector3(-dx, 2f * e, -dz).normalized;
            }

            public float VegetationMask(Vector3 p) =>
                Vertex(Mathf.FloorToInt(p.x - 0.5f - m_minX + 0.5f), Mathf.FloorToInt(p.z - 0.5f - m_minZ + 0.5f)).Value;

            public float OceanDepth(Vector3 p)
            {
                float t = Mathf.Clamp01((p.x - m_minX) / ZoneSize), t2 = Mathf.Clamp01((p.z - m_minZ) / ZoneSize);
                return Mathf.Lerp(Mathf.Lerp(m_depth[3], m_depth[2], t), Mathf.Lerp(m_depth[0], m_depth[1], t), t2);
            }

            /// Like Heightmap.GetBiome: one biome when all corners agree, else the corner with the most weight.
            public Heightmap.Biome Biome(Vector3 p)
            {
                if (s0 == s1 && s0 == s2 && s0 == s3) return s0;
                float x = (p.x - m_minX) / ZoneSize, y = (p.z - m_minZ) / ZoneSize;
                var weights = new Dictionary<Heightmap.Biome, float>();
                void Add(Heightmap.Biome b, float w) => weights[b] = (weights.TryGetValue(b, out var o) ? o : 0f) + w;
                Add(s0, Weight(x, y, 0f, 0f));
                Add(s1, Weight(x, y, 1f, 0f));
                Add(s2, Weight(x, y, 0f, 1f));
                Add(s3, Weight(x, y, 1f, 1f));
                return weights.OrderByDescending(kv => kv.Value).First().Key;
            }

            private static float Weight(float x, float y, float rx, float ry)
            {
                float d = Mathf.Sqrt((x - rx) * (x - rx) + (y - ry) * (y - ry));
                float w = 1.414f - d;
                return w * w * w;
            }
        }
    }
}
