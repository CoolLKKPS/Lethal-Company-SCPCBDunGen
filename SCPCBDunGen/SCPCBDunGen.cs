using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using LobbyCompatibility.Features;
using LobbyCompatibility.Enums;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using SCPCBDunGen.Patches;
using Unity.Netcode;
using UnityEngine;
using HarmonyLib;
using LethalLevelLoader;
using Newtonsoft.Json;
using System;
using System.Linq;
using BepInEx.Bootstrap;
using System.Net.NetworkInformation;
using DunGen;
using UnityEngine.ProBuilder;
using System.Runtime.CompilerServices;
using DunGenPlus;

namespace SCPCBDunGen
{
    // Represents conversions from a given item and what it can become depending on the setting
    // Used by the dictionary
    public struct SCP914Conversion
    {
        public string ItemName;

        public List<string> RoughResults { get; set; }
        public List<string> CoarseResults { get; set; }
        public List<string> OneToOneResults { get; set; }
        public List<string> FineResults { get; set; }
        public List<string> VeryFineResults { get; set; }
    }

    // Keyed collection of SCP 914 conversions
    public class SCP914ConversionSet : KeyedCollection<string, SCP914Conversion>
    {
        protected override string GetKeyForItem(SCP914Conversion conversion) => conversion.ItemName;
    }

    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency("BMX.LobbyCompatibility", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(LethalLevelLoader.Plugin.ModGUID, BepInDependency.DependencyFlags.HardDependency)]
    //[BepInDependency("ImoutoSama.DungeonGenerationPlus", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("Piggy.PiggyVarietyMod", BepInDependency.DependencyFlags.SoftDependency)]
    public class SCPCBDunGen : BaseUnityPlugin
    {
        public static SCPCBDunGen Instance { get; private set; } = null!;
        internal new static ManualLogSource Logger { get; private set; } = null!;

        public static AssetBundle? SCPCBAssets = null;

        // SCP 914 conversion dictionary (using KeyedCollection for easier json conversion)
        public SCP914ConversionSet SCP914Conversions = new SCP914ConversionSet();

        private void Awake()
        {
            Logger = base.Logger;
            Instance = this;

            NetcodePatcher();
            Hook();

            string AssemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            SCPCBAssets = AssetBundle.LoadFromFile(Path.Combine(AssemblyLocation, "scpcb_dungeon"));
            if (SCPCBAssets == null)
            {
                Logger.LogError("Failed to load SCPCB Dungeon assets.");
                return;
            }

            ExtendedDungeonFlow SCPExtendedFlow = SCPCBAssets.LoadAsset<ExtendedDungeonFlow>("assets/Mods/SCP/data/SCPCBDunGenExtFlow.asset");
            if (SCPExtendedFlow == null)
            {
                Logger.LogError("Failed to load SCP:CB Extended Dungeon Flow.");
                return;
            }
            //DunGenExtender DunGenPlusExtender = SCPCBAssets.LoadAsset<DunGenExtender>("assets/Mods/SCP/data/SCPDunGenExtender.asset");
            //if (DunGenPlusExtender == null)
            //{
            //    Logger.LogError("failed to load SCP:CB Dungeon Extender.");
            //    return;
            //}
            //DunGenPlus.API.AddDunGenExtender(SCPExtendedFlow.DungeonFlow, DunGenPlusExtender);

            // Config setup
            ConfigEntry<bool> configDefault914 = Config.Bind("General", "Default914Recipes", true, new ConfigDescription("If false, any custom 914 Json files named \"default.json\" will be ignored (i.e. the default 914 config will not be loaded).\nSome custom 914 implementations may want to fully override the default settings, in which case this can be set to false."));
            ConfigEntry<bool> configPVMCompat = Config.Bind("Mod Compatibility", "PiggysVarietyMod", true, new ConfigDescription("If Piggys Variety Mod is present and this setting is enabled, tesla gates can spawn in the SCP Foundation.\nWARNING: Ensure this value matches across all clients or desync will occur."));

            // PVM compat check
            if (configPVMCompat.Value && Chainloader.PluginInfos.ContainsKey("Piggy.PiggyVarietyMod"))
            {
                CompatPVM(SCPExtendedFlow); // Must be in a separate function with no-inlining flags, see https://risk-of-thunder.github.io/R2Wiki/Mod-Creation/C%23-Programming/Mod-Compatibility%3A-Soft-Dependency/
            }
            if (Chainloader.PluginInfos.ContainsKey("BMX.LobbyCompatibility"))
            {
                SCPCBDunGen.CompatBMXLobby();
            }

            PatchedContent.RegisterExtendedDungeonFlow(SCPExtendedFlow);

            // SCP 914 item conversion registry
            foreach (string sJsonFile in DiscoverConfiguredRecipeFiles())
            {
                if (!configDefault914.Value && (Path.GetFileName(sJsonFile) == "default.json")) continue; // Skip any files named "default.json" if the config is set to false
                StreamReader streamReader = new StreamReader(sJsonFile);
                string sJsonValue = streamReader.ReadToEnd();
                try
                {
                    List<SCP914Conversion> Conversions = JsonConvert.DeserializeObject<List<SCP914Conversion>>(sJsonValue);
                    foreach (SCP914Conversion Conversion in Conversions)
                    {
                        // If the given item already has conversions, add the possible conversions to it
                        // Using ToLowerInvariant which should help other langauges deal with item names (if they need to)
                        if (SCP914Conversions.Contains(Conversion.ItemName.ToLowerInvariant()))
                        {
                            SCP914Conversions[Conversion.ItemName].RoughResults.AddRange(Conversion.RoughResults);
                            SCP914Conversions[Conversion.ItemName].CoarseResults.AddRange(Conversion.CoarseResults);
                            SCP914Conversions[Conversion.ItemName].OneToOneResults.AddRange(Conversion.OneToOneResults);
                            SCP914Conversions[Conversion.ItemName].FineResults.AddRange(Conversion.FineResults);
                            SCP914Conversions[Conversion.ItemName].VeryFineResults.AddRange(Conversion.VeryFineResults);
                        }
                        else SCP914Conversions.Add(Conversion);
                    }
                    Logger.LogInfo($"Registed SCP 914 json file successfully: {sJsonFile}");
                }
                catch (JsonException exception)
                {
                    Logger.LogError($"Failed to deserialize file: {sJsonFile}. Exception: {exception.Message}");
                }
            }

            Hook();

            Logger.LogInfo($"SCP:CB DunGen for Lethal Company [Version {MyPluginInfo.PLUGIN_VERSION}] successfully loaded.");

            Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} has loaded!");
        }

        // Compatibility methods
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static void CompatPVM(ExtendedDungeonFlow SCPExtendedFlow)
        {
            // Add the tesla gate room to the list of potential LC rooms
            Logger.LogInfo("PiggysVarietyMod detected and compatibility layer enabled! Adding tesla gate room.");

            GameObject tile = SCPCBAssets.LoadAsset<GameObject>("assets/Mods/SCP/prefabs/Rooms/LC/SCPHallTesla.prefab");
            Transform TeslaSpawnTransform = tile.transform.Find("TeslaSpawn");
            SpawnSyncedObject SyncedSpawn = TeslaSpawnTransform.gameObject.AddComponent<SpawnSyncedObject>();
            SyncedSpawn.spawnPrefab = PiggyVarietyMod.Plugin.teslaGatePrefab;

            SCPExtendedFlow.DungeonFlow.Lines.First().DungeonArchetypes.First().TileSets.First().AddTile(tile, 1, 1);
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static void CompatBMXLobby()
        {
            Version pluginVersion = Version.Parse("4.3.4");
            PluginHelper.RegisterPlugin("SCPCBDunGen", pluginVersion, 2, 3);
        }


        // Thanks to Lordfirespeed for this json file path getter function
        IEnumerable<string> DiscoverConfiguredRecipeFiles()
        {
            return Directory.GetDirectories(Paths.PluginPath) // get plugin directories
                .Select(pluginDirectory => Path.Join(pluginDirectory, "badhamknibbs-scp914-recipes")) // map to path of special dir
                .Where(Directory.Exists) // filter out files and paths that don't exist
                .SelectMany(Directory.GetFiles) // select the files inside those directories and flatten
                .Where(filePath => Path.GetExtension(filePath) == ".json"); // filter out files without the '.json' extension
        }

        internal static void Hook()
        {
            Logger.LogDebug("Hooking...");

            /*
             *  Subscribe with 'On.Class.Method += CustomClass.CustomMethod;' for each method you're patching.
             */

            On.RoundManager.SpawnScrapInLevel += RoundManagerPatch.SetItemSpawnPoints;
            On.RoundManager.SpawnScrapInLevel += RoundManagerPatch.SCP914Configuration;

            Logger.LogDebug("Finished Hooking!");
        }

        private void NetcodePatcher()
        {
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
        }
    }
}
