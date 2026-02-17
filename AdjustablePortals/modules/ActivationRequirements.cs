using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace AdjustablePortals.modules {
    internal static class ActivationRequirements {

        [HarmonyPatch(typeof(TeleportWorld))]
        internal static class PortalInstanceActivatable {

            private static readonly int pieceMask = LayerMask.GetMask("piece"); // "piece_nonsolid"
            private static float update_timer = 0f;
            private static float current_update_time = 0f;
            private static int nearby_pieces = 0;

            [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.HaveTarget))]
            [HarmonyPostfix]
            internal static void Activator(TeleportWorld __instance, ref bool __result) {
                if (__instance.m_nview == null || __instance.m_nview.IsValid() == false ) {
                    return;
                }
                current_update_time += UnityEngine.Time.fixedDeltaTime;
                if (update_timer <= current_update_time) {
                    // TODO: configurable duration for delta offset
                    update_timer = current_update_time + 10;
                    Collider[] nearbyPieces = Physics.OverlapSphere(__instance.transform.position, ValConfig.PortalPieceActivationDistance.Value, pieceMask);
                    nearby_pieces = nearbyPieces.Length;
                }
                if (nearby_pieces < ValConfig.PortalNearbyPiecesForActivation.Value) {
                    __result = false;
                }
            }


            [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.GetHoverText))]
            [HarmonyPostfix]
            internal static void OnHoverHelp(TeleportWorld __instance, ref string __result) {
                if (__instance.m_nview == null || __instance.m_nview.IsValid() == false || nearby_pieces > ValConfig.PortalNearbyPiecesForActivation.Value) {
                    return;
                }
                __result += Localization.instance.Localize($"\nMore nearby structures required ({nearby_pieces}/{ValConfig.PortalNearbyPiecesForActivation.Value})");
            }

            // Mutate the result of the portal connection, so that falling below the number of required pieces kills the portal connection
            [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.TargetFound))]
            [HarmonyPostfix]
            internal static void TargetFoundPrevention(TeleportWorld __instance, ref bool __result) {
                if (__instance.m_nview == null || __instance.m_nview.IsValid() == false || nearby_pieces > ValConfig.PortalNearbyPiecesForActivation.Value) {
                    return;
                }
                __result = false;
            }
        }
    }
}
