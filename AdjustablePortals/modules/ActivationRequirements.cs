using AdjustablePortals.common;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace AdjustablePortals.modules {
    internal static class ActivationRequirements {

        [HarmonyPatch(typeof(TeleportWorld))]
        internal static class PortalInstanceActivatable {

            private static readonly string fuelKey = "AJP_FUEL";
            private static readonly string nearbyPiecesKey = "AJP_PORT_BUILD_NEARBY";

            // How long a portal's piece count is trusted before it is counted again.
            private const float ScanIntervalSeconds = 10f;
            // A portal that has just come into view uses its persisted count for this long, giving
            // the surrounding zone a chance to finish streaming in before the first real count.
            private const float InitialGraceSeconds = 5f;

            private struct PortalPieceState {
                public int Pieces;
                public float NextScanTime;
            }

            // Keyed on the whole ZDOID. ZDOID.ID is only unique per user, so keying on it alone
            // lets two different players' portals collide and share a piece count.
            private static readonly Dictionary<ZDOID, PortalPieceState> nearbyPiecesByPortal = new Dictionary<ZDOID, PortalPieceState>();
            // Reused between counts so the per-scan query does not allocate.
            private static readonly List<Piece> scanBuffer = new List<Piece>();

            /// <summary>
            /// Nearby-piece count for one portal, recounted at most once every
            /// <see cref="ScanIntervalSeconds"/>. The timer is per portal: a single shared timer
            /// means only whichever portal happens to ask first ever gets recounted.
            /// </summary>
            private static int GetPieceCount(TeleportWorld instance) {
                ZDO pzdo = instance.m_nview.GetZDO();
                ZDOID id = pzdo.m_uid;

                PortalPieceState state;
                if (nearbyPiecesByPortal.TryGetValue(id, out state)) {
                    if (Time.time < state.NextScanTime) {
                        return state.Pieces;
                    }
                } else {
                    // First sight this session. Seed from the persisted count and hold it briefly
                    // rather than counting a half-loaded zone.
                    state.Pieces = pzdo.GetInt(nearbyPiecesKey, 0);
                    state.NextScanTime = Time.time + InitialGraceSeconds;
                    nearbyPiecesByPortal[id] = state;
                    return state.Pieces;
                }

                scanBuffer.Clear();
                Piece.GetAllPiecesInRadius(instance.transform.position, ValConfig.PortalPieceActivationDistance.Value, scanBuffer);
                state.Pieces = scanBuffer.Count;
                state.NextScanTime = Time.time + ScanIntervalSeconds;
                scanBuffer.Clear();
                nearbyPiecesByPortal[id] = state;

                // Only the owner's ZDO writes replicate, so for everyone else this stays an
                // in-memory cache instead of a write that would be silently discarded.
                if (instance.m_nview.IsOwner()) {
                    pzdo.Set(nearbyPiecesKey, state.Pieces);
                }

                return state.Pieces;
            }

            [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.HaveTarget))]
            [HarmonyBefore("org.bepinex.plugins.targetportal")]
            [HarmonyPrefix]
            internal static bool Activator(TeleportWorld __instance, ref bool __result) {
                return CheckActivationRequirements(__instance, ref __result);
            }

            public static void ConsumeFuel(TeleportWorld instance) {
                if (instance == null || instance.m_nview == null || !instance.m_nview.IsValid() || !ValConfig.EnablePortalRequireFuel.Value) {
                    return;
                }
                int fuel = instance.m_nview.GetZDO().GetInt(fuelKey, 0);
                if (fuel <= 0) {
                    return;
                }
                // Only the owner's writes replicate; without this the charge is spent locally and
                // reappears the next time the owner's copy of the ZDO comes back around.
                instance.m_nview.ClaimOwnership();
                instance.m_nview.GetZDO().Set(fuelKey, fuel - 1);
                Logger.LogDebug("Consumed 1 usage of portal fuel.");
            }

            public static bool AreActivationRequirementsMet(TeleportWorld instance, out string reason) {
                reason = "";
                bool result = true;
                if (instance.m_nview == null || !instance.m_nview.IsValid()) {
                    return result;
                }

                // Piece requirements
                if (ValConfig.EnablePortalPieceRequirements.Value) {
                    int current_pieces = GetPieceCount(instance);
                    if (current_pieces < ValConfig.PortalNearbyPiecesForActivation.Value) {
                        reason = $"More nearby structures required ({current_pieces}/{ValConfig.PortalNearbyPiecesForActivation.Value})";
                        result = false;
                    }
                }

                // Fuel requirements
                if (ValConfig.EnablePortalRequireFuel.Value) {
                    int fuel = instance.m_nview.GetZDO().GetInt(fuelKey, 0);
                    if (fuel <= 0) {
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
                    int current_pieces = GetPieceCount(__instance);
                    if (current_pieces < ValConfig.PortalNearbyPiecesForActivation.Value) {
                        __result += Localization.instance.Localize($"\nMore nearby structures required ({current_pieces}/{ValConfig.PortalNearbyPiecesForActivation.Value})");
                    }
                }
                if (ValConfig.EnablePortalRequireFuel.Value) {
                    int fuel = __instance.m_nview.GetZDO().GetInt(fuelKey, 0);
                    if (fuel <= 0) {
                        __result += Localization.instance.Localize($"\nFuel is required, add <color=red>{ValConfig.PortalFuelPrefab.Value}</color> x {ValConfig.PortalFuelBatchSize.Value}");
                    } else {
                        __result += Localization.instance.Localize($"\nCurrent Fuel {fuel}.");
                    }
                }

            }

            // Mutate the result of the portal connection, so that failing the configured
            // requirements kills the portal connection. TeleportWorld.Teleport gates on
            // TargetFound, so this - not HaveTarget - is what actually stops a teleport.
            //
            // This may only ever clear the result. Vanilla TargetFound also returns false while
            // the target ZDO is still being fetched, and promoting that to true would send
            // Teleport off after a target that has not arrived yet.
            [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.TargetFound))]
            [HarmonyPostfix]
            internal static void TargetFoundPrevention(TeleportWorld __instance, ref bool __result) {
                if (__result == false) {
                    return;
                }
                if (__instance.m_nview == null || __instance.m_nview.IsValid() == false) {
                    return;
                }
                if (ValConfig.EnablePortalPieceRequirements.Value == false && ValConfig.EnablePortalRequireFuel.Value == false) {
                    return;
                }

                if (AreActivationRequirementsMet(__instance, out string reason) == false) {
                    Logger.LogDebug($"Portal blocked: {reason}");
                    __result = false;
                }
            }

            [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.UseItem))]
            [HarmonyPrefix]
            internal static bool AddFuelOnUseItem(TeleportWorld __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result) {
                if (__instance.m_nview == null || __instance.m_nview.IsValid() == false || ValConfig.EnablePortalRequireFuel.Value == false) {
                    return true;
                }
                if (item == null || item.m_dropPrefab == null || item.m_dropPrefab.name != ValConfig.PortalFuelPrefab.Value) {
                    return true;
                }

                int batchSize = ValConfig.PortalFuelBatchSize.Value;
                // The clicked stack is not the whole story - the removal draws from the entire
                // inventory, so a player holding two partial stacks can still pay.
                if (user.m_inventory.CountItemsByPrefab(ValConfig.PortalFuelPrefab.Value) < batchSize ||
                    user.m_inventory.RemoveItemByPrefab(ValConfig.PortalFuelPrefab.Value, batchSize) == false) {
                    user.Message(MessageHud.MessageType.Center, $"Requires at least {ValConfig.PortalFuelPrefab.Value}x{batchSize}.");
                    __result = true;
                    return false;
                }

                __instance.m_nview.ClaimOwnership();
                int fuel = __instance.m_nview.GetZDO().GetInt(fuelKey, 0) + ValConfig.PortalFuelUsagesPerBatch.Value;
                __instance.m_nview.GetZDO().Set(fuelKey, fuel);
                user.Message(MessageHud.MessageType.Center, $"Added {ValConfig.PortalFuelPrefab.Value}x{batchSize} for {ValConfig.PortalFuelUsagesPerBatch.Value} fuel.");
                __result = true;
                return false;
            }

            // Vanilla Teleport bails out on a missing target, the NoPortals key, an active boss and
            // a non-teleportable inventory, so "the postfix ran" is not the same as "the player
            // teleported". TeleportTo is what flips m_teleporting, so compare it across the call.
            [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
            [HarmonyPrefix]
            internal static void ConsumeFuelAfterTeleportPrefix(Player player, out bool __state) {
                __state = player != null && player.IsTeleporting();
            }

            [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
            [HarmonyPostfix]
            internal static void ConsumeFuelAfterTeleport(TeleportWorld __instance, Player player, bool __state) {
                if (__state || player == null || player.IsTeleporting() == false) {
                    return;
                }
                ConsumeFuel(__instance);
            }

            // ZDOIDs are not stable across sessions, so the piece cache must not survive one.
            [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Shutdown))]
            [HarmonyPostfix]
            internal static void ClearPortalPieceCache() {
                nearbyPiecesByPortal.Clear();
                scanBuffer.Clear();
            }

        }
    }
}
