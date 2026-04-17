using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace AdjustablePortals.modules {
    public static class TeleportItems {

        internal static List<string> EikthyrAllowedTeleports = new List<string>();
        internal static List<string> ElderAllowedTeleports = new List<string>();
        internal static List<string> BonemassAllowedTeleports = new List<string>();
        internal static List<string> ModerAllowedTeleports = new List<string>();
        internal static List<string> YagluthAllowedTeleports = new List<string>();
        internal static List<string> QueenAllowedTeleports = new List<string>();
        internal static List<string> FaderAllowedTeleports = new List<string>();

        static Dictionary<string, bool> PlayerItemsAllowTeleport = new Dictionary<string, bool>();

        // Initial loading of the config lists
        internal static void SetupTeleportLists() {
            ConfigListChanged(EikthyrAllowedTeleports, ValConfig.DefeatedEikthyrAllowedItems.Value);
            ConfigListChanged(ElderAllowedTeleports, ValConfig.DefeatedElderAllowedItems.Value);
            ConfigListChanged(BonemassAllowedTeleports, ValConfig.DefeatedBonemassAllowedItems.Value);
            ConfigListChanged(ModerAllowedTeleports, ValConfig.DefeatedModerAllowItems.Value);
            ConfigListChanged(YagluthAllowedTeleports, ValConfig.DefeatedYagluthAllowItems.Value);
            ConfigListChanged(QueenAllowedTeleports, ValConfig.DefeatedQueenAllowItems.Value);
            ConfigListChanged(FaderAllowedTeleports, ValConfig.DefeatedFaderAllowItems.Value);
        }

        internal static void EikthyrAllowedTeleportsChanged(object s, EventArgs e) { ConfigListChanged(EikthyrAllowedTeleports, ValConfig.DefeatedEikthyrAllowedItems.Value); }
        internal static void ElderAllowedTeleportsChanged(object s, EventArgs e) { ConfigListChanged(ElderAllowedTeleports, ValConfig.DefeatedElderAllowedItems.Value); }
        internal static void BonemassAllowedTeleportsChanged(object s, EventArgs e) { ConfigListChanged(BonemassAllowedTeleports, ValConfig.DefeatedBonemassAllowedItems.Value); }
        internal static void ModerAllowedTeleportsChanged(object s, EventArgs e) { ConfigListChanged(ModerAllowedTeleports, ValConfig.DefeatedModerAllowItems.Value); }
        internal static void YagluthAllowedTeleportsChanged(object s, EventArgs e) { ConfigListChanged(YagluthAllowedTeleports, ValConfig.DefeatedYagluthAllowItems.Value); }
        internal static void QueenAllowedTeleportsChanged(object s, EventArgs e) { ConfigListChanged(QueenAllowedTeleports, ValConfig.DefeatedQueenAllowItems.Value); }
        internal static void FaderAllowedTeleportsChanged(object s, EventArgs e) { ConfigListChanged(FaderAllowedTeleports, ValConfig.DefeatedFaderAllowItems.Value); }

        private static void ConfigListChanged(List<string> targetList, string configValue) {
            PlayerItemsAllowTeleport.Clear();
            try {
                List<string> listEntry = new List<string>() { };
                foreach (var item in configValue.Split(',')) {
                    listEntry.Add(item);
                }
                if (listEntry.Count > 0) {
                    targetList.Clear();
                    targetList.AddRange(listEntry);
                }
            } catch (Exception ex) {
                Logger.LogWarning($"Error parsing ConfigList: {ex}");
            }
        }


        [HarmonyPatch(typeof(Humanoid))]
        private static class AllowConfiguredTeleportableItems {
            [HarmonyPatch(nameof(Humanoid.IsTeleportable))]
            private static void Postfix(Humanoid __instance, ref bool __result) {
                // Nothing to do if the player is already allowed to teleport
                if (__result == true) { return; }

                List<ItemDrop.ItemData> playerNonTeleportableItems = __instance.m_inventory.GetAllItems().Where(x => x.m_shared.m_teleportable == false).Distinct().ToList();
                //Logger.LogDebug($"Checking if the player can teleport the following items: {string.Join(", ", playerNonTeleportableItems)}");
                List<string> playerItemsNotAllowed = new List<string>();
                foreach (ItemDrop.ItemData item in playerNonTeleportableItems) {
                    if (PerPlayerTeleportableItems.IsItemTeleportable(item) == false) {
                        playerItemsNotAllowed.Add(item.m_dropPrefab.name);
                    }
                }

                if (playerItemsNotAllowed.Count == 0) {
                    __result = true;
                } else {
                    Logger.LogDebug($"The following items are not teleportable {string.Join(", ", playerItemsNotAllowed)}");
                    __result = false;
                }
            }
        }

        [HarmonyPatch(typeof(ZoneSystem))]
        public static class ClearTeleportableCache {

            [HarmonyPostfix]
            [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.SetGlobalKey), argumentTypes: new Type[] { typeof(string) })]
            private static void ClearGlobalKeyReset(string name) {
                PlayerItemsAllowTeleport.Clear();
            }
        }

        [HarmonyPatch(typeof(Player))]
        public static class ClearTeleportableCachePrivateKeys {
            [HarmonyPostfix]
            [HarmonyPatch(typeof(Player), nameof(Player.AddUniqueKey), argumentTypes: new Type[] { typeof(string) })]
            private static void ClearPrivateKeyReset(string name) {
                PlayerItemsAllowTeleport.Clear();
            }
        }

        [HarmonyPatch(typeof(InventoryGrid))]
        public static class PerPlayerTeleportableItems {

            //[HarmonyEmitIL(".dump")]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(InventoryGrid.UpdateGui))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/) {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldfld),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(ItemDrop.ItemData.SharedData), nameof(ItemDrop.ItemData.SharedData.m_teleportable)))
                ).RemoveInstructions(3).InsertAndAdvance(
                    new CodeInstruction(OpCodes.Ldloc_S, 18),
                    Transpilers.EmitDelegate(IsItemTeleportable)
                ).ThrowIfNotMatch("Unable to patch item teleport visual display.");

                return codeMatcher.Instructions();
            }

            public static bool IsItemTeleportable(ItemDrop.ItemData item) {
                if (item == null || item.m_shared == null || item.m_dropPrefab == null) {
                    return true;
                }
                string itemPrefab = item.m_dropPrefab.name;

                if (PlayerItemsAllowTeleport.ContainsKey(itemPrefab)) {
                    return PlayerItemsAllowTeleport[itemPrefab];
                }

                bool teleportable = item.m_shared.m_teleportable;
                // Eikthyr
                if (teleportable == false && ZoneSystem.instance.GetGlobalKey(GlobalKeys.defeated_eikthyr)) {
                    if (EikthyrAllowedTeleports.Contains(itemPrefab)) {
                        teleportable = true;
                    }
                }

                // Elder
                if (teleportable == false && ZoneSystem.instance.GetGlobalKey(GlobalKeys.defeated_gdking)) {
                    if (ElderAllowedTeleports.Contains(itemPrefab)) {
                        teleportable = true;
                    }
                }

                // Bonemass
                if (teleportable == false && ZoneSystem.instance.GetGlobalKey(GlobalKeys.defeated_bonemass)) {
                    if (BonemassAllowedTeleports.Contains(itemPrefab)) {
                        teleportable = true;
                    }
                }

                // Moder
                if (teleportable == false && ZoneSystem.instance.GetGlobalKey(GlobalKeys.defeated_dragon)) {
                    if (ModerAllowedTeleports.Contains(itemPrefab)) {
                        teleportable = true;
                    }
                }

                // Yagluth
                if (teleportable == false && ZoneSystem.instance.GetGlobalKey(GlobalKeys.defeated_goblinking)) {
                    if (YagluthAllowedTeleports.Contains(itemPrefab)) {
                        teleportable = true;
                    }
                }

                // Queen
                if (teleportable == false && ZoneSystem.instance.GetGlobalKey("defeated_queen")) {
                    if (QueenAllowedTeleports.Contains(itemPrefab)) {
                        teleportable = true;
                    }
                }

                // Fader
                if (teleportable == false && ZoneSystem.instance.GetGlobalKey("defeated_fader")) {
                    if (FaderAllowedTeleports.Contains(itemPrefab)) {
                        teleportable = true;
                    }
                }


                Logger.LogDebug($"Item is teleportable? {itemPrefab} - {teleportable}");
                if (PlayerItemsAllowTeleport.ContainsKey(itemPrefab) == false) {
                    PlayerItemsAllowTeleport.Add(itemPrefab, teleportable);
                }
                return false;
            }
        }
    }
}
