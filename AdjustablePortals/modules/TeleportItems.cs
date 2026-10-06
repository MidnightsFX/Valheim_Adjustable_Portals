using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace AdjustablePortals.modules {
    public static class TeleportItems {

        // Prefab names, matched ignoring case and surrounding spaces. Admins type these lists by hand,
        // and "Copper, copperscrap" has to mean the same as "Copper,CopperScrap".
        internal static readonly HashSet<string> EikthyrAllowedTeleports = NewItemSet();
        internal static readonly HashSet<string> ElderAllowedTeleports = NewItemSet();
        internal static readonly HashSet<string> BonemassAllowedTeleports = NewItemSet();
        internal static readonly HashSet<string> ModerAllowedTeleports = NewItemSet();
        internal static readonly HashSet<string> YagluthAllowedTeleports = NewItemSet();
        internal static readonly HashSet<string> QueenAllowedTeleports = NewItemSet();
        internal static readonly HashSet<string> FaderAllowedTeleports = NewItemSet();
        internal static readonly HashSet<string> NonTeleportableItems = NewItemSet();

        // Whether boss progression has unlocked a prefab. Deliberately not the item's whole answer,
        // which also depends on the item's own m_teleportable - and that is not a property of the
        // prefab. Backpacks gives each backpack a private copy of its shared data and flips the
        // flag to follow what is inside, so caching the combined answer by prefab name settled
        // every backpack by whichever one happened to be asked about first.
        static readonly Dictionary<string, bool> ProgressionUnlocked = new Dictionary<string, bool>();

        private static HashSet<string> NewItemSet() {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        // Initial loading of the config lists
        internal static void SetupTeleportLists() {
            ConfigListChanged(EikthyrAllowedTeleports, ValConfig.DefeatedEikthyrAllowedItems.Value);
            ConfigListChanged(ElderAllowedTeleports, ValConfig.DefeatedElderAllowedItems.Value);
            ConfigListChanged(BonemassAllowedTeleports, ValConfig.DefeatedBonemassAllowedItems.Value);
            ConfigListChanged(ModerAllowedTeleports, ValConfig.DefeatedModerAllowItems.Value);
            ConfigListChanged(YagluthAllowedTeleports, ValConfig.DefeatedYagluthAllowItems.Value);
            ConfigListChanged(QueenAllowedTeleports, ValConfig.DefeatedQueenAllowItems.Value);
            ConfigListChanged(FaderAllowedTeleports, ValConfig.DefeatedFaderAllowItems.Value);
            ConfigListChanged(NonTeleportableItems, ValConfig.NonTeleportableItems.Value);
        }

        internal static void EikthyrAllowedTeleportsChanged(object s, EventArgs e) { ConfigListChanged(EikthyrAllowedTeleports, ValConfig.DefeatedEikthyrAllowedItems.Value); }
        internal static void ElderAllowedTeleportsChanged(object s, EventArgs e) { ConfigListChanged(ElderAllowedTeleports, ValConfig.DefeatedElderAllowedItems.Value); }
        internal static void BonemassAllowedTeleportsChanged(object s, EventArgs e) { ConfigListChanged(BonemassAllowedTeleports, ValConfig.DefeatedBonemassAllowedItems.Value); }
        internal static void ModerAllowedTeleportsChanged(object s, EventArgs e) { ConfigListChanged(ModerAllowedTeleports, ValConfig.DefeatedModerAllowItems.Value); }
        internal static void YagluthAllowedTeleportsChanged(object s, EventArgs e) { ConfigListChanged(YagluthAllowedTeleports, ValConfig.DefeatedYagluthAllowItems.Value); }
        internal static void QueenAllowedTeleportsChanged(object s, EventArgs e) { ConfigListChanged(QueenAllowedTeleports, ValConfig.DefeatedQueenAllowItems.Value); }
        internal static void FaderAllowedTeleportsChanged(object s, EventArgs e) { ConfigListChanged(FaderAllowedTeleports, ValConfig.DefeatedFaderAllowItems.Value); }
        internal static void NonTeleportableItemsChanged(object s, EventArgs e) { ConfigListChanged(NonTeleportableItems, ValConfig.NonTeleportableItems.Value); }
        internal static void ProgressionKeySourceChanged(object s, EventArgs e) { InvalidateCache(); }

        /// <summary>
        /// Forgets every cached progression answer. Anything that changes which bosses count as
        /// defeated, or what they unlock, has to come through here.
        /// </summary>
        internal static void InvalidateCache() {
            ProgressionUnlocked.Clear();
            // Backpacks worked out each backpack's teleport flag from the answers just thrown away.
            Compatibility.MarkBackpacksStale();
        }

        private static void ConfigListChanged(HashSet<string> targetSet, string configValue) {
            InvalidateCache();
            targetSet.Clear();
            if (string.IsNullOrEmpty(configValue)) {
                return;
            }
            foreach (string entry in configValue.Split(',')) {
                string prefab = entry.Trim();
                if (prefab.Length > 0) {
                    targetSet.Add(prefab);
                }
            }
        }

        /// <summary>
        /// Whether <paramref name="item"/> is on the configured list of items that may never be
        /// teleported.
        /// </summary>
        internal static bool IsItemBlocked(ItemDrop.ItemData item) {
            return NonTeleportableItems.Count > 0 && item != null && item.m_dropPrefab != null && NonTeleportableItems.Contains(item.m_dropPrefab.name);
        }

        // Inventory rather than Humanoid, which only forwards here. Backpacks asks a backpack's own
        // inventory this directly to decide whether the backpack may travel, and it has to get the
        // same answer the player's inventory would.
        [HarmonyPatch(typeof(Inventory))]
        private static class AllowConfiguredTeleportableItems {
            [HarmonyPatch(nameof(Inventory.IsTeleportable))]
            private static void Postfix(Inventory __instance, ref bool __result) {
                // Nothing to do if the inventory is already allowed to teleport
                if (__result == true) { return; }

                List<ItemDrop.ItemData> items = __instance.m_inventory;
                // Vanilla refuses these outright, ahead of both allowAllItems and the TeleportAll
                // global key, so it is not a restriction this mod is meant to lift. Nothing below
                // would catch it either: an item blocked purely on tool tier is usually still
                // flagged m_teleportable, so the scan reads it as "all clear".
                for (int i = 0; i < items.Count; i++) {
                    if (items[i].m_shared.m_toolTier >= 1000) {
                        return;
                    }
                }

                // Runs twice a second for every portal the player stands near, so no LINQ here.
                for (int i = 0; i < items.Count; i++) {
                    if (PerPlayerTeleportableItems.IsItemTeleportable(items[i]) == false) {
                        return;
                    }
                }
                __result = true;
            }
        }

        [HarmonyPatch(typeof(Inventory))]
        private static class BlockNonTeleportableItems {
            // Last, so that nothing promoting the result after this - another mod's "let everything
            // through" option included - can carry a blocked item past it. This mod's own
            // progression allowance above never promotes a blocked item to begin with.
            [HarmonyPatch(nameof(Inventory.IsTeleportable))]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Inventory __instance, ref bool __result) {
                if (__result == false || NonTeleportableItems.Count == 0) {
                    return;
                }
                List<ItemDrop.ItemData> items = __instance.m_inventory;
                for (int i = 0; i < items.Count; i++) {
                    if (IsItemBlocked(items[i])) {
                        __result = false;
                        return;
                    }
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
                InvalidateCache();
            }

            // Covers removals too: RPC_GlobalKeys clears and re-adds, so a key that went away shows
            // up here rather than in the add patch above.
            [HarmonyPostfix]
            [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.ClearGlobalKeys))]
            private static void GlobalKeysCleared() {
                InvalidateCache();
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
                InvalidateCache();
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(Player.RemoveUniqueKey))]
            private static void PlayerKeyRemoved() {
                InvalidateCache();
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(Player.ResetUniqueKeys))]
            private static void PlayerKeysReset() {
                InvalidateCache();
            }

            // Also what puts right the backpacks: Load reads the inventory before the private keys,
            // so Backpacks has already judged every backpack without them by the time this runs.
            [HarmonyPostfix]
            [HarmonyPatch(nameof(Player.Load))]
            private static void PlayerLoaded() {
                InvalidateCache();
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
                if (item == null || item.m_shared == null) {
                    return true;
                }
                // Nothing for a config entry to name, so this is vanilla's call.
                if (item.m_dropPrefab == null) {
                    return item.m_shared.m_teleportable;
                }
                string itemPrefab = item.m_dropPrefab.name;

                if (NonTeleportableItems.Contains(itemPrefab)) {
                    return false;
                }
                // Read fresh every call rather than cached with the answer below; see ProgressionUnlocked.
                if (item.m_shared.m_teleportable) {
                    return true;
                }
                return IsUnlockedByProgression(itemPrefab);
            }

            private static bool IsUnlockedByProgression(string itemPrefab) {
                if (ProgressionUnlocked.TryGetValue(itemPrefab, out bool unlocked)) {
                    return unlocked;
                }

                unlocked = (EikthyrAllowedTeleports.Contains(itemPrefab) && ProgressionKeys.HasKey("defeated_eikthyr"))
                    || (ElderAllowedTeleports.Contains(itemPrefab) && ProgressionKeys.HasKey("defeated_gdking"))
                    || (BonemassAllowedTeleports.Contains(itemPrefab) && ProgressionKeys.HasKey("defeated_bonemass"))
                    || (ModerAllowedTeleports.Contains(itemPrefab) && ProgressionKeys.HasKey("defeated_dragon"))
                    || (YagluthAllowedTeleports.Contains(itemPrefab) && ProgressionKeys.HasKey("defeated_goblinking"))
                    || (QueenAllowedTeleports.Contains(itemPrefab) && ProgressionKeys.HasKey("defeated_queen"))
                    || (FaderAllowedTeleports.Contains(itemPrefab) && ProgressionKeys.HasKey("defeated_fader"));

                //Logger.LogDebug($"Item is unlocked by progression? {itemPrefab} - {unlocked}");
                ProgressionUnlocked[itemPrefab] = unlocked;
                return unlocked;
            }
        }
    }
}
