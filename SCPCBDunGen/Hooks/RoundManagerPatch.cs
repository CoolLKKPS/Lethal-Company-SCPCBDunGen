using System;
using System.Collections.Generic;
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

            // Array to reference arrays via type index
            IEnumerable<string>[] arSettingToItems = [arROUGH, arCOARSE, arONETOONE, arFINE, arVERYFINE];

            Item itemConvert = lItems.Find(x => x.itemName.ToLowerInvariant() == sItem); // Item we want to add conversions for
            if (itemConvert == null)
            {
                SCPCBDunGen.Logger.LogError($"Failed to find item for conversion \"{sItem}\", skipping.");
                return;
            }
            foreach (SCP914Converter.SCP914Setting scp914Setting in Enum.GetValues(typeof(SCP914Converter.SCP914Setting)))
            { // Iterate all setting values
                List<Item> lConvertItems = new List<Item>(); // Create a list of all items we want to add conversions for
                foreach (string sItemName in arSettingToItems[(int)scp914Setting])
                {
                    if (sItemName == "*") lConvertItems.Add(null);
                    else if (sItemName == "@") lConvertItems.Add(itemConvert);
                    else lConvertItems.Add(lItems.Find(x => x.itemName.ToLowerInvariant() == sItemName)); // OK to be null
                }
                SCP914.AddConversion(scp914Setting, itemConvert, lConvertItems); // Add to conversion dictionary
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
