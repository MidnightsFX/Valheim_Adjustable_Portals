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
        internal static void ProgressionKeySourceChanged(object s, EventArgs e) { PlayerItemsAllowTeleport.Clear(); }

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

                // Vanilla refuses these outright, ahead of both allowAllItems and the TeleportAll
                // global key, so it is not a restriction this mod is meant to lift. Nothing below
                // would catch it either: the scan only considers items flagged m_teleportable
                // false, and an item blocked purely on tool tier is usually not one of them, so
                // the allow list comes back empty and reads as "all clear".
                if (__instance.m_inventory.GetAllItems().Any(x => x.m_shared.m_toolTier >= 1000)) {
                    return;
                }

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

        // SetGlobalKey only fires a routed RPC on the caller; every other client learns about a new
        // key through RPC_GlobalKeys -> GlobalKeyAdd. Patching the methods that actually mutate the
        // key collections is what makes a boss kill invalidate the cache everywhere rather than
        // only for whoever landed the last hit.
        [HarmonyPatch(typeof(ZoneSystem))]
        public static class ClearTeleportableCache {

            [HarmonyPostfix]
            [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.GlobalKeyAdd), argumentTypes: new Type[] { typeof(string), typeof(bool) })]
            private static void GlobalKeyAdded(string keyStr) {
                PlayerItemsAllowTeleport.Clear();
            }

            // Covers removals too: RPC_GlobalKeys clears and re-adds, so a key that went away shows
            // up here rather than in the add patch above.
            [HarmonyPostfix]
            [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.ClearGlobalKeys))]
            private static void GlobalKeysCleared() {
                PlayerItemsAllowTeleport.Clear();
            }
        }

        // Private keys belong to the player rather than the world, so none of the patches above see
        // them change. Load is here because it fills a freshly spawned player's keys directly rather
        // than through AddUniqueKey, and every respawn and character switch goes through it.
        [HarmonyPatch(typeof(Player))]
        public static class ClearTeleportableCacheOnPlayerKeys {

            [HarmonyPostfix]
            [HarmonyPatch(nameof(Player.AddUniqueKey))]
            private static void PlayerKeyAdded() {
                PlayerItemsAllowTeleport.Clear();
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(Player.RemoveUniqueKey))]
            private static void PlayerKeyRemoved() {
                PlayerItemsAllowTeleport.Clear();
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(Player.ResetUniqueKeys))]
            private static void PlayerKeysReset() {
                PlayerItemsAllowTeleport.Clear();
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(Player.Load))]
            private static void PlayerLoaded() {
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

                // Vanilla computes `!item.m_shared.m_teleportable`, which is these three
                // instructions: load the loop's item local, then the two field loads. Swap the
                // whole expression for IsItemTeleportable(item) - note the inverted sense, which
                // the surrounding vanilla `!` restores.
                codeMatcher.MatchStartForward(
                    new CodeMatch(instruction => instruction.IsLdloc()),
                    new CodeMatch(OpCodes.Ldfld),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(ItemDrop.ItemData.SharedData), nameof(ItemDrop.ItemData.SharedData.m_teleportable)))
                ).ThrowIfNotMatch("Unable to patch item teleport visual display.");

                // Reuse the item load the match just landed on instead of naming a local slot.
                // The slot number is not stable across game updates - it was 18 when this was
                // written and is 16 now, and the old hardcoded index had drifted onto an
                // unrelated bool local, which pushes the wrong type for the delegate below.
                // Cloning also carries over any labels on the instruction being replaced.
                CodeInstruction loadItem = codeMatcher.Instruction.Clone();

                codeMatcher.RemoveInstructions(3).InsertAndAdvance(
                    loadItem,
                    Transpilers.EmitDelegate(IsItemTeleportable)
                );

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
                if (teleportable == false && ProgressionKeys.HasKey("defeated_eikthyr")) {
                    if (EikthyrAllowedTeleports.Contains(itemPrefab)) {
                        teleportable = true;
                    }
                }

                // Elder
                if (teleportable == false && ProgressionKeys.HasKey("defeated_gdking")) {
                    if (ElderAllowedTeleports.Contains(itemPrefab)) {
                        teleportable = true;
                    }
                }

                // Bonemass
                if (teleportable == false && ProgressionKeys.HasKey("defeated_bonemass")) {
                    if (BonemassAllowedTeleports.Contains(itemPrefab)) {
                        teleportable = true;
                    }
                }

                // Moder
                if (teleportable == false && ProgressionKeys.HasKey("defeated_dragon")) {
                    if (ModerAllowedTeleports.Contains(itemPrefab)) {
                        teleportable = true;
                    }
                }

                // Yagluth
                if (teleportable == false && ProgressionKeys.HasKey("defeated_goblinking")) {
                    if (YagluthAllowedTeleports.Contains(itemPrefab)) {
                        teleportable = true;
                    }
                }

                // Queen
                if (teleportable == false && ProgressionKeys.HasKey("defeated_queen")) {
                    if (QueenAllowedTeleports.Contains(itemPrefab)) {
                        teleportable = true;
                    }
                }

                // Fader
                if (teleportable == false && ProgressionKeys.HasKey("defeated_fader")) {
                    if (FaderAllowedTeleports.Contains(itemPrefab)) {
                        teleportable = true;
                    }
                }


                //Logger.LogDebug($"Item is teleportable? {itemPrefab} - {teleportable}");
                if (PlayerItemsAllowTeleport.ContainsKey(itemPrefab) == false) {
                    PlayerItemsAllowTeleport.Add(itemPrefab, teleportable);
                }
                return teleportable;
            }
        }
    }
}
