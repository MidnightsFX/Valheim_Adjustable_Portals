using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AdjustablePortals.modules {
    internal static class TeleportItems {

        internal static List<string> EikthyrAllowedTeleports = new List<string>();
        internal static List<string> ElderAllowedTeleports = new List<string>();
        internal static List<string> BonemassAllowedTeleports = new List<string>();
        internal static List<string> ModerAllowedTeleports = new List<string>();
        internal static List<string> YagluthAllowedTeleports = new List<string>();
        internal static List<string> QueenAllowedTeleports = new List<string>();
        internal static List<string> FaderAllowedTeleports = new List<string>();

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

                List<string> playerNonTeleportableItems = __instance.m_inventory.GetAllItems().Where(x => x.m_shared.m_teleportable == false).Select(x => x.m_dropPrefab.name).Distinct().ToList();
                //Logger.LogDebug($"Checking if the player can teleport the following items: {string.Join(", ", playerNonTeleportableItems)}");

                // Eikthyr
                if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.defeated_eikthyr)) {
                    foreach (string item in EikthyrAllowedTeleports) {
                        if (playerNonTeleportableItems.Contains(item)) {
                            playerNonTeleportableItems.Remove(item);
                        }
                    }
                }

                // Elder
                if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.defeated_gdking)) {
                    foreach (string item in ElderAllowedTeleports) {
                        if (playerNonTeleportableItems.Contains(item)) {
                            playerNonTeleportableItems.Remove(item);
                        }
                    }
                }

                // Bonemass
                if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.defeated_bonemass)) {
                    foreach (string item in BonemassAllowedTeleports) {
                        if (playerNonTeleportableItems.Contains(item)) {
                            playerNonTeleportableItems.Remove(item);
                        }
                    }
                }

                // Moder
                if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.defeated_dragon)) {
                    foreach (string item in ModerAllowedTeleports) {
                        if (playerNonTeleportableItems.Contains(item)) {
                            playerNonTeleportableItems.Remove(item);
                        }
                    }
                }

                // Yagluth
                if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.defeated_goblinking)) {
                    foreach (string item in YagluthAllowedTeleports) {
                        if (playerNonTeleportableItems.Contains(item)) {
                            playerNonTeleportableItems.Remove(item);
                        }
                    }
                }

                // Queen
                if (ZoneSystem.instance.GetGlobalKey("defeated_queen")) {
                    foreach (string item in QueenAllowedTeleports) {
                        if (playerNonTeleportableItems.Contains(item)) {
                            playerNonTeleportableItems.Remove(item);
                        }
                    }
                }

                // Fader
                if (ZoneSystem.instance.GetGlobalKey("defeated_fader")) {
                    foreach (string item in FaderAllowedTeleports) {
                        if (playerNonTeleportableItems.Contains(item)) {
                            playerNonTeleportableItems.Remove(item);
                        }
                    }
                }

                if (playerNonTeleportableItems.Count == 0) {
                    __result = true;
                } else {
                    Logger.LogDebug($"The following items are not teleportable {string.Join(", ", playerNonTeleportableItems)}");
                    __result = false;
                }
            }
        }
    }
}
