using LethalLevelLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Networking.Transport.Error;
using UnityEngine;

namespace SCPCBDunGen.Patches
{
    public class RoundManagerPatch
    {
        internal static void SetItemSpawnPoints(On.RoundManager.orig_SpawnScrapInLevel original, RoundManager self)
        {
            do
            {
                if (self.dungeonGenerator.Generator.DungeonFlow.name != "SCPFlow") break; // Do nothing to non-SCP dungeons, exit to orig

                // Grab the general and tabletop item groups from the bottle bin item
                StartOfRound startOfRound = StartOfRound.Instance;
                if (startOfRound == null)
                {
                    SCPCBDunGen.Logger.LogError("Failed to get start of round instance. Scrap spawns may not work correctly.");
                    break; // Exit to orig
                }

                Item bottleItem = startOfRound.allItemsList.itemsList.Find(x => x.name == "BottleBin");
                if (bottleItem == null)
                {
                    SCPCBDunGen.Logger.LogError("Failed to find bottle bin item for reference snatching; scrap spawn may be significantly lower than expected.");
                    break; // Exit to orig
                }
                // Grab the small item group from the fancy glass (only appears on paid moons, so this one is optional and replaced with tabletop items if invalid)
                Item fancyGlassItem = startOfRound.allItemsList.itemsList.Find(x => x.name == "FancyCup");

                int iGeneralScrapCount = 0;
                int iTabletopScrapCount = 0;
                int iSmallScrapCount = 0;

                // Grab the item groups
                ItemGroup itemGroupGeneral = bottleItem.spawnPositionTypes.Find(x => x.name == "GeneralItemClass");
                ItemGroup itemGroupTabletop = bottleItem.spawnPositionTypes.Find(x => x.name == "TabletopItems");
                ItemGroup itemGroupSmall = (fancyGlassItem == null) ? itemGroupTabletop : fancyGlassItem.spawnPositionTypes.Find(x => x.name == "SmallItems"); // Use tabletop items in place of small items if not on a paid moon
                RandomScrapSpawn[] scrapSpawns = UnityEngine.Object.FindObjectsOfType<RandomScrapSpawn>();
                foreach (RandomScrapSpawn scrapSpawn in scrapSpawns)
                {
                    switch (scrapSpawn.spawnableItems.name)
                    {
                        case "GeneralItemClass":
                            scrapSpawn.spawnableItems = itemGroupGeneral;
                            iGeneralScrapCount++;
                            break;
                        case "TabletopItems":
                            scrapSpawn.spawnableItems = itemGroupTabletop;
                            iTabletopScrapCount++;
                            break;
                        case "SmallItems":
                            scrapSpawn.spawnableItems = itemGroupSmall;
                            iSmallScrapCount++;
                            break;
                    }
                }

                SCPCBDunGen.Logger.LogInfo($"Totals for scrap replacement: General: {iGeneralScrapCount}, Tabletop: {iTabletopScrapCount}, Small: {iSmallScrapCount}");
                if ((iGeneralScrapCount + iTabletopScrapCount + iSmallScrapCount) < 10) SCPCBDunGen.Logger.LogWarning("Unusually low scrap spawn count; scrap may be sparse.");
            } while (false);

            original(self);
        }

        // Add conversions for a specified item to possible results
        // Use "*" to destroy an item, or "@" to do no conversion (same item as input)
        public static void AddConversions(SCP914Converter SCP914, List<Item> lItems, string sItem, IEnumerable<string> arROUGH, IEnumerable<string> arCOARSE, IEnumerable<string> arONETOONE, IEnumerable<string> arFINE, IEnumerable<string> arVERYFINE)
        {
            string sItemLower = sItem.ToLowerInvariant();
            // Array to reference arrays via type index
            IEnumerable<string>[] arSettingToItems = [arROUGH, arCOARSE, arONETOONE, arFINE, arVERYFINE];

            // Enemy conversion
            if (sItemLower[0] == '!') {
                string sEnemyNameLower = sItemLower.TrimStart('!');
                ExtendedEnemyType enemyType = PatchedContent.ExtendedEnemyTypes.Find(x => x.EnemyType.enemyName.ToLowerInvariant() == sEnemyNameLower); // Enemy we want to add conversions for
                if (enemyType == null) {
                    SCPCBDunGen.Logger.LogError($"Failed to find enemy for conversion \"{sEnemyNameLower}\", skipping.");
                    return;
                }
                foreach (SCP914Converter.SCP914Setting scp914Setting in Enum.GetValues(typeof(SCP914Converter.SCP914Setting))) { // Iterate all setting values
                    List<Item> lConvertItems = []; // Create a list of all items we want to add conversions for
                    List<EnemyType> lConvertEnemies = []; // Same as above for enemies
                    foreach (string sObjectName in arSettingToItems[(int)scp914Setting]) {
                        string sObjectNameLower = sObjectName.ToLowerInvariant();
                        if (sObjectNameLower == "*") lConvertEnemies.Add(null);
                        else if (sObjectNameLower == "@") lConvertEnemies.Add(enemyType.EnemyType);
                        else if (sObjectNameLower[0] == '!') { // Enemy-Enemy conversion
                            string sEnemyNameTarget = sObjectNameLower.TrimStart('!');
                            ExtendedEnemyType enemyTypeConvert = PatchedContent.ExtendedEnemyTypes.Find(x => x.EnemyType.enemyName.ToLowerInvariant() == sEnemyNameTarget); // Enemy we want to add conversions for
                            if (enemyTypeConvert == null) {
                                SCPCBDunGen.Logger.LogWarning($"Conversion target for enemy {sItem} not found: {sObjectNameLower}, possibly spelt wrong or from a mod not enabled. Treating as self-conversion.");
                                enemyTypeConvert = enemyType;
                            }
                        } else { // Enemy-Item conversion
                            // If no name, then an item in the conversion list doesn't exist (either a modded item that isn't in and/or is spelt incorrectly)
                            Item TargetItem = lItems.Find(x => x.itemName.ToLowerInvariant() == sObjectNameLower);
                            if (TargetItem.itemName.Length > 0) lConvertItems.Add(TargetItem);
                            else SCPCBDunGen.Logger.LogWarning($"Conversion target for enemy {sItem} not found: {sObjectNameLower}, possibly spelt wrong or from a mod not enabled. Skipping.");
                        }
                    }
                    if (lConvertItems.Count > 0) SCP914.AddConversion(scp914Setting, enemyType.EnemyType, lConvertItems); // Add to enemy->item conversion dictionary
                    if (lConvertEnemies.Count > 0) SCP914.AddConversion(scp914Setting, enemyType.EnemyType, lConvertEnemies); // Add to enemy->enemy conversion dictionary
                }
            } else {
                Item itemConvert = lItems.Find(x => x.itemName.ToLowerInvariant() == sItemLower); // Item we want to add conversions for
                if (itemConvert == null) {
                    SCPCBDunGen.Logger.LogError($"Failed to find item for conversion \"{sItem}\", skipping.");
                    return;
                }
                foreach (SCP914Converter.SCP914Setting scp914Setting in Enum.GetValues(typeof(SCP914Converter.SCP914Setting))) { // Iterate all setting values
                    List<Item> lConvertItems = []; // Create a list of all items we want to add conversions for
                    List<EnemyType> lConvertEnemies = []; // Same as above for enemies
                    foreach (string sObjectName in arSettingToItems[(int)scp914Setting]) {
                        string sObjectNameLower = sObjectName.ToLowerInvariant();
                        if (sObjectNameLower == "*") lConvertItems.Add(null);
                        else if (sObjectNameLower == "@") lConvertItems.Add(itemConvert);
                        else if (sObjectNameLower[0] == '!') { // Item-Enemy conversion
                            string sEnemyNameTarget = sObjectNameLower.TrimStart('!');
                            ExtendedEnemyType enemyTypeConvert = PatchedContent.ExtendedEnemyTypes.Find(x => x.EnemyType.enemyName.ToLowerInvariant() == sEnemyNameTarget); // Enemy we want to add conversions for
                            // If no name we failed to find the enemy conversion
                            if (enemyTypeConvert != null) lConvertEnemies.Add(enemyTypeConvert.EnemyType);
                            else SCPCBDunGen.Logger.LogWarning($"Conversion target for item {sItem} not found: {sObjectNameLower}, possibly spelt wrong or from a mod not enabled. Skipping.");
                        } else { // Item-Item conversion
                            // If no name, then an item in the conversion list doesn't exist (either a modded item that isn't in and/or is spelt incorrectly)
                            Item TargetItem = lItems.Find(x => x.itemName.ToLowerInvariant() == sObjectNameLower);
                            if (TargetItem != null) lConvertItems.Add(TargetItem);
                            else SCPCBDunGen.Logger.LogWarning($"Conversion target for enemy {sItem} not found: {sObjectNameLower}, possibly spelt wrong or from a mod not enabled. Skipping.");
                        }
                    }
                    if (lConvertItems.Count > 0) SCP914.AddConversion(scp914Setting, itemConvert, lConvertItems); // Add to item->item conversion dictionary
                    if (lConvertEnemies.Count > 0) SCP914.AddConversion(scp914Setting, itemConvert, lConvertEnemies); // Add to item->enemy conversion dictionary
                }
            }
        }

        internal static void SCP914Configuration(On.RoundManager.orig_SpawnScrapInLevel original, RoundManager self)
        {
            original(self);

            if (self.dungeonGenerator.Generator.DungeonFlow.name != "SCPFlow") return; // There won't be an SCP 914

            SCP914Converter SCP914 = UnityEngine.Object.FindObjectOfType<SCP914Converter>();
            if (SCP914 == null)
            { // No 914, don't do anything
                SCPCBDunGen.Logger.LogInfo("No 914 room found.");
                return;
            }

            List<Item> lItems = StartOfRound.Instance.allItemsList.itemsList;
            if (lItems.Count == 0)
            {
                SCPCBDunGen.Logger.LogError("Item list was empty from StartOfRound.");
                return;
            }

            // Add conversions
            foreach (var conversion in SCPCBDunGen.Instance.SCP914Conversions)
            {
                AddConversions(SCP914, lItems, conversion.ItemName, conversion.RoughResults, conversion.CoarseResults, conversion.OneToOneResults, conversion.FineResults, conversion.VeryFineResults);
            }
        }
    }
}
