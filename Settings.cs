using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using Jotunn.Configs;
using UnityEngine;

namespace ScoutingBeacons
{
    /// User settings in BepInEx/config/SirCookie.ScoutingBeacons.cfg. All are server settings (Jötunn admin-only).
    public static class Settings
    {
        public static ConfigEntry<bool> ConvertOnClose;
        public static ConfigEntry<bool> DropResourcesOnDestroy;
        public static ConfigEntry<bool> MobsTargetBeacon;
        public static ConfigEntry<float> RingWidth;
        public static ConfigEntry<float> RingCostAtLimit;
        public static ConfigEntry<float> PostLimitIncrement;
        public static ConfigEntry<int> CoastBuffer;
        public static ConfigEntry<bool> PredictOres;
        public static ConfigEntry<bool> BlockAutoFeeding;
        private static ConfigEntry<string> s_fuelValues;

        public const string DefaultFuel =
            "Wood=1, RoundLog=1.2, ElderBark=1.4, FineWood=1.6, YggdrasilWood=1.8, Blackwood=1.8, Resin=1.5, " +
            "GreydwarfEye=2, Coal=2.5, CharcoalResin=3, Guck=5, Tar=8, Softtissue=10, SulfurStone=12, Wisp=15, Sap=25, " +
            "Eitr=40, SurtlingCore=50, BlackCore=100";

        public static void Bind(ConfigFile config)
        {
            const string general = "01 - General";
            ConvertOnClose = config.Bind(general, "Convert on close", false,
                Server("Closing the beacon's window turns its fuel items into fuel and drops everything that isn't fuel. " +
                       "Off: only the \"Feed beacon\" button converts; other items stay in the beacon like a chest."));
            DropResourcesOnDestroy = config.Bind(general, "Drop resources on destroy", false,
                Server("A beacon destroyed by damage drops its build materials. Taking it down with the hammer always returns them."));
            MobsTargetBeacon = config.Bind(general, "Mobs target beacons", false,
                Server("Enemies attack beacons like workbenches and other main targets. Needs a game restart."));
            RingWidth = config.Bind(general, "Ring width", 12f,
                Server("Metres per ring (one step outwards). Also the size of the land/ocean check grid.",
                    new AcceptableValueRange<float>(4f, 64f)));
            RingCostAtLimit = config.Bind(general, "Ring cost at limit", 50f,
                Server("Fuel the ring at a tier's area limit costs. Rings before it ramp up evenly from almost nothing."));
            PostLimitIncrement = config.Bind(general, "Post limit increment", 1f,
                Server("Each ring past the limit costs this many times 'Ring cost at limit' more than the one before " +
                       "(1: 2x, 3x, 4x ...)."));
            BlockAutoFeeding = config.Bind(general, "Block automatic feeding", true,
                Server("Mods that move items between chests leave beacons alone: ServersideQoL AutoStore doesn't store items " +
                       "into beacons, and OttoFuel / ServersideQoL AutoProcess and TameAssist don't take fuel out of them. " +
                       "ServersideQoL picks up a change after a server restart."));
            CoastBuffer = config.Bind(general, "Coast buffer", 1,
                Server("Rings of ocean revealed along the coast so the shoreline doesn't look cut off.",
                    new AcceptableValueRange<int>(0, 5)));

            s_fuelValues = config.Bind("02 - Fuel", "Fuel values", DefaultFuel,
                Server("Item prefab name = fuel per item. Items not listed aren't fuel."));
            s_fuelValues.SettingChanged += (_, __) => s_fuel = null;

            foreach (var tier in BeaconTiers.All)
            {
                tier.LimitKm2 = config.Bind(tier.Section, "Area limit (km2)", tier.DefaultLimit,
                    Server("Area after which ring costs rise steeply.", new AcceptableValueRange<float>(0.05f, 50f)));
                tier.HoursPerKm2 = config.Bind(tier.Section, "In-game hours per km2", tier.DefaultHours,
                    Server("In-game hours (world clock) the beacon needs to scout one square kilometre of land.",
                        new AcceptableValueRange<float>(0.1f, 1000f)));
                tier.Efficiency = config.Bind(tier.Section, "Fuel efficiency", tier.DefaultEfficiency,
                    Server("Multiplier on every ring cost (0.75 = pays 75%).", new AcceptableValueRange<float>(0.1f, 2f)));
                tier.MarkLocations = config.Bind(tier.Section, "Mark locations", tier.DefaultLocations,
                    Server("Pin dungeons, villages, caves, forts ... (see 06 - Markers) on the scouted land."));
                tier.MarkOres = config.Bind(tier.Section, "Mark ores", tier.DefaultOres,
                    Server("Pin ore deposits on the scouted land. Only deposits that already exist in the world: the game " +
                           "creates them the first time a player comes near that area."));
            }

            const string markers = "06 - Markers";
            s_locationMarkers = config.Bind(markers, "Locations", DefaultLocations,
                Server("Location name start = pin label. A location is pinned when its name starts with one of these. " +
                       "Locations that match nothing are listed once in the server log, to add them here."));
            s_oreMarkers = config.Bind(markers, "Ores", DefaultOres,
                Server("Ore deposit prefab = pin label."));
            s_locationMarkers.SettingChanged += (_, __) => s_locations = null;
            s_oreMarkers.SettingChanged += (_, __) => s_ores = null;
            if (System.Array.IndexOf(OldDefaultOres, s_oreMarkers.Value) >= 0) s_oreMarkers.Value = DefaultOres; // untouched old default
            if (System.Array.IndexOf(OldDefaultLocations, s_locationMarkers.Value) >= 0) s_locationMarkers.Value = DefaultLocations;
            s_markFortressRuins = config.Bind(markers, "Mark fortress ruins", false,
                Server("Also pin the Ashlands fortress ruins (there are many)."));
            s_markFortressRuins.SettingChanged += (_, __) => s_locations = null;
            s_markBosses = config.Bind(markers, "Mark boss altars", false,
                Server("Also pin boss altars (Eikthyr, The Elder, Bonemass, Moder, Yagluth)."));
            foreach (var trader in Traders)
                trader.Enabled = config.Bind(markers, "Mark " + trader.Label, false,
                    Server($"Also pin {trader.Label}. Until the game has placed them, every possible spot is pinned as \"{trader.Label} (possible)\"; " +
                           "once one is placed (someone came near), only that one."));
            s_oreLocations = config.Bind(markers, "Ore locations", DefaultOreLocations,
                Server("Locations that hold ore (name start = pin label), pinned with the ores: flametal in lava leviathans, " +
                       "brains in giant skeletons. Known for the whole world, so no prediction needed."));
            s_oreLocations.SettingChanged += (_, __) => s_oreLocationMap = null;
            s_markOthers = config.Bind(markers, "Mark other locations", false,
                Server("Also pin forts, infested (guck) trees and greydwarf camps."));
            foreach (var e in new[] { s_markBosses, s_markOthers }) e.SettingChanged += (_, __) => s_locations = null;
            s_markObsidian = config.Bind(markers, "Mark obsidian", false,
                Server("Also pin obsidian deposits (with ores, on tiers that mark ores). Off by default: obsidian is common."));
            s_markObsidian.SettingChanged += (_, __) => s_ores = null;
            s_markTin = config.Bind(markers, "Mark tin", false,
                Server("Also pin tin deposits (with ores, on tiers that mark ores). Off by default: tin is everywhere."));
            s_markTin.SettingChanged += (_, __) => s_ores = null;
            PredictOres = config.Bind(markers, "Predict ores", true,
                Server("In areas nobody has been near yet, ore deposits don't exist yet: work out where the game will put " +
                       "them from the world seed and pin those too. A few may be off or missing."));
        }

        // Ore lists shipped by earlier versions: replaced by the current default when still unchanged
        private static readonly string[] OldDefaultOres =
        {
            "rock4_copper=Copper, rock4_copper_frac=Copper, MineRock_Tin=Tin, mudpile=Scrap iron, mudpile2=Scrap iron, " +
            "mudpile_frac=Scrap iron, mudpile2_frac=Scrap iron, silvervein=Silver, silvervein_frac=Silver, " +
            "MineRock_Obsidian=Obsidian, MineRock_Meteorite=Flametal, giant_brain=Soft tissue, " +
            "giant_brain_frac=Soft tissue, FlametalRockstand=Flametal",
            "rock4_copper=Copper, rock4_copper_frac=Copper, MineRock_Tin=Tin, mudpile=Scrap iron, mudpile2=Scrap iron, " +
            "mudpile_frac=Scrap iron, mudpile2_frac=Scrap iron, silvervein=Silver, silvervein_frac=Silver, " +
            "MineRock_Meteorite=Flametal, giant_brain=Soft tissue, " +
            "giant_brain_frac=Soft tissue, FlametalRockstand=Flametal",
            "rock4_copper=Cu, rock4_copper_frac=Cu, mudpile_beacon=Fe, mudpile=Fe, mudpile2=Fe, mudpile_frac=Fe, " +
            "mudpile2_frac=Fe, silvervein=Ag, silvervein_frac=Ag, MineRock_Meteorite=Flametal, " +
            "FlametalRockstand=Flametal, giant_brain=Soft tissue, giant_brain_frac=Soft tissue",
        };

        public const string DefaultLocations =
            "Crypt=Burial chamber, SunkenCrypt=Sunken crypt, TrollCave=Troll cave, MountainCave=Frost cave, " +
            "GoblinCamp=Fuling village, WoodVillage=Draugr village, Hildir_crypt=Smouldering tomb, " +
            "Hildir_cave=Howling cavern, Hildir_plainsfortress=Sealed tower, Mistlands_DvergrTownEntrance=Infested mine, " +
            "Mistlands_DvergrBossEntrance=Sealed mine, Mistlands_Excavation=Dvergr excavation, " +
            "CharredFortress=Charred fortress, MorgenHole=Morgen hole, PlaceofMystery=Place of mystery, " +
            "FireHole=Fire hole, DrakeNest=Drake nest";

        // Location lists shipped by earlier versions: replaced by the current default when still unchanged
        private static readonly string[] OldDefaultLocations =
        {
            "Crypt=Burial chamber, SunkenCrypt=Sunken crypt, TrollCave=Troll cave, MountainCave=Frost cave, " +
            "GoblinCamp=Fuling village, WoodVillage=Draugr village, Hildir_crypt=Smouldering tomb, " +
            "Hildir_cave=Howling cavern, Hildir_plainsfortress=Sealed tower, Mistlands_DvergrTownEntrance=Infested mine, " +
            "Mistlands_DvergrBossEntrance=Sealed mine, Mistlands_Excavation=Dvergr excavation, " +
            "CharredFortress=Charred fortress, MorgenHole=Morgen hole, PlaceofMystery=Place of mystery, " +
            "FortressRuins=Fortress ruins, FireHole=Fire hole, DrakeNest=Drake nest",
            "Crypt=Burial chamber, SunkenCrypt=Sunken crypt, TrollCave=Troll cave, MountainCave=Frost cave, " +
            "GoblinCamp=Fuling village, WoodVillage=Draugr village, Hildir_crypt=Smouldering tomb, " +
            "Hildir_cave=Howling cavern, Hildir_plainsfortress=Sealed tower, Mistlands_DvergrTownEntrance=Infested mine, " +
            "Mistlands_DvergrBossEntrance=Sealed mine, Mistlands_Excavation=Dvergr excavation, " +
            "CharredFortress=Charred fortress, MorgenHole=Morgen hole, PlaceofMystery=Place of mystery, " +
            "FortressRuins=Fortress ruins",
        };

        // Optional location groups (switches in "06 - Markers"), location name start = label
        private const string BossLocations =
            "Eikthyrnir=Eikthyr, GDKing=The Elder, Bonemass=Bonemass, Dragonqueen=Moder, GoblinKing=Yagluth";
        private const string OtherLocations = "Fort1=Fort, InfestedTree=Infested tree, Greydwarf_camp=Greydwarf camp";
        private const string FortressRuinsLocations = "FortressRuins=Fortress ruins";
        private static ConfigEntry<bool> s_markBosses, s_markOthers, s_markFortressRuins;

        /// A trader (or similar one-of-a-kind place) the game puts at one of several possible spots.
        public class Trader
        {
            public string Label, NamePart; // NamePart: found anywhere in the location name, ignoring case
            public ConfigEntry<bool> Enabled;
        }

        public static readonly Trader[] Traders =
        {
            new Trader { Label = "Haldor", NamePart = "vendor_blackforest" },
            new Trader { Label = "Hildir", NamePart = "hildir_camp" },
            new Trader { Label = "Bog Witch", NamePart = "bogwitch" },
            new Trader { Label = "Altar of Potential", NamePart = "potential" },
        };

        public static Trader TraderFor(string locationName)
        {
            string lower = locationName.ToLowerInvariant();
            foreach (var t in Traders) if (lower.Contains(t.NamePart)) return t;
            return null;
        }

        public const string DefaultOreLocations = "LeviathanLava=Flametal, Mistlands_Giant=Brains";
        private static ConfigEntry<string> s_oreLocations;
        private static Dictionary<string, string> s_oreLocationMap;
        public static Dictionary<string, string> OreLocations => s_oreLocationMap ?? (s_oreLocationMap = ParseLabels(s_oreLocations.Value));

        /// Ore label for a location that holds ore, or null.
        public static string OreLocationLabel(string locationName)
        {
            foreach (var kv in OreLocations) if (locationName.StartsWith(kv.Key)) return kv.Value;
            return null;
        }

        public const string DefaultOres =
            "rock4_copper=Cu, rock4_copper_frac=Cu, mudpile_beacon=Fe, mudpile=Fe, mudpile2=Fe, mudpile_frac=Fe, " +
            "mudpile2_frac=Fe, silvervein=Ag, silvervein_frac=Ag, MineRock_Meteorite=Flametal, " +
            "FlametalRockstand=Flametal, giant_brain=Brains, giant_brain_frac=Brains";

        private static ConfigEntry<string> s_locationMarkers, s_oreMarkers;
        private static ConfigEntry<bool> s_markTin;
        private const string TinPrefab = "MineRock_Tin";
        private static Dictionary<string, string> s_locations, s_ores;

        public static Dictionary<string, string> LocationMarkers
        {
            get
            {
                if (s_locations != null) return s_locations;
                string text = s_locationMarkers.Value;
                if (s_markBosses.Value) text += ", " + BossLocations;
                if (s_markOthers.Value) text += ", " + OtherLocations;
                if (s_markFortressRuins.Value) text += ", " + FortressRuinsLocations;
                return s_locations = ParseLabels(text);
            }
        }
        private static ConfigEntry<bool> s_markObsidian;
        private const string ObsidianPrefab = "MineRock_Obsidian";

        public static Dictionary<string, string> OreMarkers
        {
            get
            {
                if (s_ores != null) return s_ores;
                s_ores = ParseLabels(s_oreMarkers.Value);
                if (s_markObsidian.Value && !s_ores.ContainsKey(ObsidianPrefab)) s_ores[ObsidianPrefab] = "Obs";
                if (s_markTin.Value && !s_ores.ContainsKey(TinPrefab)) s_ores[TinPrefab] = "Tn";
                return s_ores;
            }
        }

        private static Dictionary<string, string> ParseLabels(string text)
        {
            var map = new Dictionary<string, string>();
            foreach (var part in text.Split(','))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && kv[0].Trim().Length > 0) map[kv[0].Trim()] = kv[1].Trim();
            }
            return map;
        }

        /// Pin label for a location prefab name, or null when it isn't a marked location (longest matching start wins).
        public static string LocationLabel(string prefabName)
        {
            string best = null, label = null;
            foreach (var kv in LocationMarkers)
                if (prefabName.StartsWith(kv.Key) && (best == null || kv.Key.Length > best.Length)) { best = kv.Key; label = kv.Value; }
            return label;
        }

        private static ConfigDescription Server(string description, AcceptableValueBase range = null) =>
            new ConfigDescription("[Server] " + description, range, new ConfigurationManagerAttributes { IsAdminOnly = true });

        private static Dictionary<string, float> s_fuel;

        public static Dictionary<string, float> Fuel
        {
            get
            {
                if (s_fuel != null) return s_fuel;
                s_fuel = new Dictionary<string, float>();
                foreach (var part in s_fuelValues.Value.Split(','))
                {
                    var kv = part.Split('=');
                    if (kv.Length == 2 && float.TryParse(kv[1].Trim().Replace(',', '.'), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float value) && value > 0f)
                        s_fuel[Renamed(kv[0].Trim())] = value;
                }
                return s_fuel;
            }
        }

        public static float FuelValue(ItemDrop.ItemData item)
        {
            if (item?.m_dropPrefab == null) return 0f;
            return Fuel.TryGetValue(item.m_dropPrefab.name, out float v) ? v : 0f;
        }

        /// 0.1.0 shipped "Sulfur", but the item is SulfurStone: config files written by 0.1.0 still work.
        private static string Renamed(string name) => name == "Sulfur" ? "SulfurStone" : name;

        /// Logs fuel names that don't exist in the game, once the item database is up.
        public static void CheckFuelNames()
        {
            if (!ObjectDB.instance || ObjectDB.instance.m_items.Count == 0) return; // the first Awake runs before items are added
            foreach (var name in Fuel.Keys)
                if (!ObjectDB.instance.GetItemPrefab(name))
                    Jotunn.Logger.LogWarning($"Fuel item '{name}' doesn't exist in this game, ignored");
        }
    }

    public class BeaconTier
    {
        public string PieceName, DisplayName, Section, Description;
        public Color Tint;
        public int Wisps;
        public float DefaultLimit, DefaultHours, DefaultEfficiency;
        public Requirement[] Cost;
        public bool DefaultLocations, DefaultOres;
        public ConfigEntry<float> LimitKm2, HoursPerKm2, Efficiency;
        public ConfigEntry<bool> MarkLocations, MarkOres;

        /// Ring index at the area limit (a circle of that area).
        public int LimitRing => Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(LimitKm2.Value * 1e6f / Mathf.PI) / Settings.RingWidth.Value));

        /// Land scouted per second of world time, in m².
        public float SquareMetresPerSecond
        {
            get
            {
                float day = EnvMan.instance ? EnvMan.instance.m_dayLengthSec : 1800f;
                return 1e6f / (HoursPerKm2.Value / 24f * day);
            }
        }

        public float RingCost(int n)
        {
            if (n <= 0) return 0f;
            int limit = LimitRing;
            float stack = Settings.RingCostAtLimit.Value;
            float cost = n <= limit ? stack * n / limit : stack * (1f + (n - limit) * Settings.PostLimitIncrement.Value);
            return cost * Efficiency.Value;
        }
    }

    public static class BeaconTiers
    {
        public static readonly List<BeaconTier> All = new List<BeaconTier>
        {
            new BeaconTier
            {
                PieceName = "sb_beacon_bronze", DisplayName = "Huginn's Cairn", Section = "03 - Bronze",
                Description = "Huginn flies out over the land. Slow, but it sees everything on this island.",
                Tint = new Color(1.25f, 0.8f, 0.45f), Wisps = 2,
                DefaultLimit = 1f, DefaultHours = 48f, DefaultEfficiency = 1f,
                Cost = new[] { new Requirement("FineWood", 10), new Requirement("Bronze", 10) },
            },
            new BeaconTier
            {
                PieceName = "sb_beacon_silver", DisplayName = "Muninn's Spire", Section = "04 - Silver",
                Description = "Muninn remembers every hill and shore it passes.",
                Tint = new Color(0.95f, 1.05f, 1.25f), Wisps = 4,
                DefaultLimit = 2f, DefaultHours = 18f, DefaultEfficiency = 0.9f, DefaultLocations = true,
                Cost = new[] { new Requirement("Stone", 15), new Requirement("Silver", 15) },
            },
            new BeaconTier
            {
                PieceName = "sb_beacon_blackmetal", DisplayName = "Allfather's Eye", Section = "05 - Black metal",
                Description = "The Allfather's gaze sweeps the land.",
                Tint = new Color(0.45f, 0.55f, 0.5f), Wisps = 8,
                DefaultLimit = 4f, DefaultHours = 6f, DefaultEfficiency = 0.75f, DefaultLocations = true, DefaultOres = true,
                Cost = new[] { new Requirement("YggdrasilWood", 20), new Requirement("BlackMetal", 20) },
            },
        };

        public static BeaconTier ForPiece(string prefabName) => All.Find(t => t.PieceName == prefabName);
    }
}

namespace ScoutingBeacons
{
    public class Requirement
    {
        public readonly string Item;
        public readonly int Amount;
        public Requirement(string item, int amount) { Item = item; Amount = amount; }
    }
}
