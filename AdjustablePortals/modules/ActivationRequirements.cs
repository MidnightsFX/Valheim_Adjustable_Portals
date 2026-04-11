using AdjustablePortals.common;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace AdjustablePortals.modules {
    internal static class ActivationRequirements {

        [HarmonyPatch(typeof(TeleportWorld))]
        internal static class PortalInstanceActivatable {

            private static readonly int pieceMask = LayerMask.GetMask("piece"); // "piece_nonsolid"
            private static readonly string fuelKey = "AJP_FUEL";
            private static readonly string nearbyPiecesKey = "AJP_PORT_BUILD_NEARBY";
            private static float update_timer = 0f;
            private static float current_update_time = 0f;

            private static Dictionary<uint, int> nearbyPiecesByPortal = new Dictionary<uint, int>();

            private static int GetCurrentPiecesForSpecificPortal(ZDO portalZDO) {
                int current_pieces;
                uint id = portalZDO.m_uid.ID;
                if (nearbyPiecesByPortal.ContainsKey(id)) {
                    current_pieces = nearbyPiecesByPortal[id];
                } else {
                    current_pieces = portalZDO.GetInt(nearbyPiecesKey, 1);
                    nearbyPiecesByPortal.Add(id, current_pieces);
                }
                return current_pieces;
            }

            [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.HaveTarget))]
            [HarmonyBefore("org.bepinex.plugins.targetportal")]
            [HarmonyPrefix]
            internal static bool Activator(TeleportWorld __instance, ref bool __result) {
                return CheckActivationRequirements(__instance, ref __result);
            }

            public static void ConsumeFuel(TeleportWorld instance) {
                if (instance.m_nview == null || !instance.m_nview.IsValid() || !ValConfig.EnablePortalRequireFuel.Value) {
                    return;
                }
                int fuel = instance.m_nview.GetZDO().GetInt(fuelKey, 0);
                if (fuel > 0) {
                    fuel--;
                    instance.m_nview.GetZDO().Set(fuelKey, fuel);
                    Logger.LogDebug("Consumed 1 usage of portal fuel.");
                }
            }

            public static bool AreActivationRequirementsMet(TeleportWorld instance, out string reason) {
                reason = "";
                bool result = true;
                if (instance.m_nview == null || !instance.m_nview.IsValid()) {
                    return result;
                }

                // Piece requirements
                if (ValConfig.EnablePortalPieceRequirements.Value) {
                    ZDO pzdo = instance.m_nview.GetZDO();
                    int current_pieces = GetCurrentPiecesForSpecificPortal(pzdo);

                    current_update_time += UnityEngine.Time.fixedDeltaTime;
                    if (update_timer <= current_update_time) {
                        current_pieces = instance.m_nview.GetZDO().GetInt(nearbyPiecesKey, 0);
                        // TODO: configurable duration for delta offset
                        //if (nearby_pieces >= ValConfig.PortalNearbyPiecesForActivation.Value) {
                        //    update_timer = current_update_time + 500;
                        //}
                        update_timer = current_update_time + 10;
                        Collider[] nearbyPieces = Physics.OverlapSphere(instance.transform.position, ValConfig.PortalPieceActivationDistance.Value, pieceMask);
                        current_pieces = nearbyPieces.Length;
                        nearbyPiecesByPortal[pzdo.m_uid.ID] = current_pieces;
                        pzdo.Set(nearbyPiecesKey, current_pieces);
                    }
                    //Logger.LogDebug($"Checking for portal piece requirement {current_pieces < ValConfig.PortalNearbyPiecesForActivation.Value}");
                    if (current_pieces < ValConfig.PortalNearbyPiecesForActivation.Value) {
                        reason = $"More nearby structures required ({current_pieces}/{ValConfig.PortalNearbyPiecesForActivation.Value})";
                        result = false;
                    }
                }

                // Fuel requirements
                if (ValConfig.EnablePortalRequireFuel.Value) {
                    int fuel = instance.m_nview.GetZDO().GetInt(fuelKey, 0);
                    //Logger.LogDebug($"Checking for portal fuel requirement {fuel == 0}");
                    if (fuel == 0) {
                        reason = $"Fuel is required, add {ValConfig.PortalFuelPrefab.Value} x {ValConfig.PortalFuelBatchSize.Value}";
                        result = false;
                    }
                }

                return result;
            }

            public static bool CheckActivationRequirements(TeleportWorld __instance, ref bool __result) {
                if (__instance.m_nview == null || __instance.m_nview.IsValid() == false) {
                    return true; // Let the original run
                }

                if (!Compatibility.IsTargetPortalInstalled) {
                    if (__instance.m_nview.GetZDO().GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) == ZDOID.None) {
                        __result = false;
                        return false;
                    }
                }

                // Valid unless failing requirements
                __result = AreActivationRequirementsMet(__instance, out string reason);
                if (__result == false) {
                    Logger.LogDebug($"Is portal active? {__result} {reason}");
                }
                
                // Skip the original, 
                return false;
            }


            [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.GetHoverText))]
            [HarmonyPostfix]
            internal static void OnHoverHelp(TeleportWorld __instance, ref string __result) {
                if (__instance.m_nview == null || __instance.m_nview.IsValid() == false) {
                    return;
                }
                if (ValConfig.EnablePortalPieceRequirements.Value) {
                    int current_pieces = GetCurrentPiecesForSpecificPortal(__instance.m_nview.GetZDO());
                    if (current_pieces < ValConfig.PortalNearbyPiecesForActivation.Value) {
                        __result += Localization.instance.Localize($"\nMore nearby structures required ({current_pieces}/{ValConfig.PortalNearbyPiecesForActivation.Value})");
                    }
                }
                if (ValConfig.EnablePortalRequireFuel.Value) {
                    int fuel = __instance.m_nview.GetZDO().GetInt(fuelKey, 0);
                    if (fuel == 0) {
                        __result += Localization.instance.Localize($"\nFuel is required, add <color=red>{ValConfig.PortalFuelPrefab.Value}</color> x {ValConfig.PortalFuelBatchSize.Value}");
                    } else {
                        __result += Localization.instance.Localize($"\nCurrent Fuel {fuel}.");
                    }
                }

            }

            // Mutate the result of the portal connection, so that falling below the number of required pieces kills the portal connection
            [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.TargetFound))]
            [HarmonyPostfix]
            internal static void TargetFoundPrevention(TeleportWorld __instance, ref bool __result) {
                if (__instance.m_nview == null || __instance.m_nview.IsValid() == false || ValConfig.EnablePortalPieceRequirements.Value == false) {
                    return;
                }

                CheckActivationRequirements(__instance, ref __result);
            }

            [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.UseItem))]
            [HarmonyPrefix]
            internal static bool TeleportCost(TeleportWorld __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result) {
                if (__instance.m_nview == null || __instance.m_nview.IsValid() == false || ValConfig.EnablePortalRequireFuel.Value == false) {
                    return true;
                }
                int fuel = __instance.m_nview.GetZDO().GetInt(fuelKey, 0);
                if (item.m_dropPrefab != null && item.m_dropPrefab.name == ValConfig.PortalFuelPrefab.Value) {
                    if (item.m_stack >= ValConfig.PortalFuelBatchSize.Value) {
                        user.m_inventory.RemoveItemByPrefab(ValConfig.PortalFuelPrefab.Value, ValConfig.PortalFuelBatchSize.Value);
                        fuel += ValConfig.PortalFuelUsagesPerBatch.Value;
                        __instance.m_nview.GetZDO().Set(fuelKey, fuel);
                        user.Message(MessageHud.MessageType.Center, $"Added {ValConfig.PortalFuelPrefab.Value}x{ValConfig.PortalFuelBatchSize.Value} for {ValConfig.PortalFuelUsagesPerBatch.Value} fuel.");
                    } else {
                        user.Message(MessageHud.MessageType.Center, $"Requires at least {ValConfig.PortalFuelPrefab.Value}x{ValConfig.PortalFuelBatchSize.Value}.");
                    }
                    __result = true;
                    return false;
                }
                return true;
            }

            [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
            [HarmonyPostfix]
            internal static void TeleportCost(TeleportWorld __instance) {
                if (__instance.m_nview == null || __instance.m_nview.IsValid() == false || ValConfig.EnablePortalRequireFuel.Value == false) {
                    return;
                }
                int fuel = __instance.m_nview.GetZDO().GetInt(fuelKey, 0);
                if (fuel > 0) {
                    fuel--;
                    __instance.m_nview.GetZDO().Set(fuelKey, fuel);
                    Logger.LogDebug("Consumed 1 usage of portal fuel.");
                }
            }

        }
    }
}
