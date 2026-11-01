using System.Linq;
using BepInEx;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace ScoutingBeacons
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "SirCookie.ScoutingBeacons";
        public const string PluginName = "Valdar - Beacons that map";
        public const string PluginVersion = "1.0.1";

        public const string BasePrefab = "incinerator"; // the Obliterator

        /// Inactive copy of the Wisplight's floating wisp, visuals only; beacons clone it while active.
        public static GameObject WispTemplate;

        private void Awake()
        {
            Settings.Bind(Config);
            PrefabManager.OnVanillaPrefabsAvailable += AddPieces;
            new Harmony(PluginGUID).PatchAll();
            CommandManager.Instance.AddConsoleCommand(new OreCheckCommand()); // "sb_orecheck" accuracy test (cheat)
            Jotunn.Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        // After every plugin's Awake: optional patches into other mods
        private void Start()
        {
            var harmony = new Harmony(PluginGUID + ".compat");
            Compat.PatchServersideQoL(harmony);
            Compat.PatchOttoFuel(harmony);
            Compat.PatchAzumatt(harmony);
        }

        private void AddPieces()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= AddPieces;
            WispTemplate = MakeWispTemplate();
            foreach (var tier in BeaconTiers.All) AddBeacon(tier);
        }

        private static void AddBeacon(BeaconTier tier)
        {
            var config = new PieceConfig
            {
                Name = tier.DisplayName,
                Description = tier.Description,
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Misc,
            };
            foreach (var req in tier.Cost)
                config.AddRequirement(new RequirementConfig(req.Item, req.Amount, 0, true));

            var piece = new CustomPiece(tier.PieceName, BasePrefab, config);
            var prefab = piece.PiecePrefab;
            if (!prefab)
            {
                Jotunn.Logger.LogError($"Couldn't clone {BasePrefab} for {tier.DisplayName}");
                return;
            }

            // Keep the Obliterator's look and storage, drop its lever and obliterating
            foreach (var c in prefab.GetComponentsInChildren<Incinerator>(true)) Object.DestroyImmediate(c);
            foreach (var c in prefab.GetComponentsInChildren<Switch>(true)) Object.DestroyImmediate(c);

            // The Obliterator keeps its storage on a child: only one container, on the root (the Beacon reads it there)
            foreach (var c in prefab.GetComponentsInChildren<Container>(true))
                if (c.gameObject != prefab) Object.DestroyImmediate(c);
            var container = prefab.GetComponent<Container>();
            if (!container) container = prefab.AddComponent<Container>();
            container.m_name = tier.DisplayName;
            container.m_width = 6;
            container.m_height = 3;
            container.m_privacy = Container.PrivacySetting.Public;
            container.m_checkGuardStone = true;

            var p = prefab.GetComponent<Piece>();
            p.m_primaryTarget = Settings.MobsTargetBeacon.Value;
            p.m_randomTarget = Settings.MobsTargetBeacon.Value;

            Tint(prefab, tier.Tint);
            prefab.AddComponent<Beacon>();
            PieceManager.Instance.AddPiece(piece);
        }

        /// Recolours every material of the clone (copies, so the vanilla Obliterator keeps its look).
        private static void Tint(GameObject prefab, Color tint)
        {
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!mats[i]) continue;
                    var m = new Material(mats[i]);
                    if (m.HasProperty("_Color")) m.color = m.color * tint;
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
        }

        private static GameObject MakeWispTemplate()
        {
            var source = PrefabManager.Instance.GetPrefab("demister_ball");
            GameObject copy;
            if (source)
            {
                bool was = source.activeSelf;
                source.SetActive(false);
                copy = Object.Instantiate(source);
                source.SetActive(was);
                // Visuals only: no network object, no Demister force field, no physics
                for (int pass = 0; pass < 4; pass++)
                    foreach (var c in copy.GetComponentsInChildren<Component>(true).Reverse())
                        if (!(c is Transform || c is Renderer || c is MeshFilter || c is ParticleSystem || c is Light))
                            Object.DestroyImmediate(c);
            }
            else
            {
                Jotunn.Logger.LogWarning("demister_ball not found, beacons get a plain light instead of wisps");
                copy = new GameObject();
                copy.SetActive(false);
                var light = copy.AddComponent<Light>();
                light.color = new Color(0.6f, 0.9f, 1f);
                light.range = 6f;
                light.intensity = 1.5f;
            }
            copy.name = "sb_wisp";
            Object.DontDestroyOnLoad(copy);
            copy.transform.localScale = Vector3.one * 0.6f;
            return copy;
        }
    }
}
