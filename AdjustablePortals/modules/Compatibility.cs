using AdjustablePortals.common;
using HarmonyLib;
using Jotunn.Utils;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AdjustablePortals.modules {
    internal static class Compatibility {

        public static bool IsTargetPortalInstalled = false;
        private static TeleportWorld activeSourcePortal = null;
        private static float activeSourcePortalExpiry = 0f;
        // A player who opens the portal map and then closes it never teleports. Without an expiry
        // the stale reference would charge that portal for whatever teleport happened next.
        private const float ActiveSourcePortalLifetime = 30f;

        /// <summary>
        /// The portal a TargetPortal map was opened from, while that choice is still live.
        /// </summary>
        /// <remarks>
        /// Read rather than consumed: unlike the fuel charge below, more than one thing wants to
        /// know which portal a teleport started from, and a reader that cleared the answer would
        /// decide by patch order which of them got it.
        /// </remarks>
        internal static TeleportWorld GetActiveSourcePortal() {
            if (activeSourcePortal == null || Time.time > activeSourcePortalExpiry) {
                return null;
            }
            return activeSourcePortal;
        }

        internal static void CheckModCompat() {
            try {
                Dictionary<string, BepInEx.BaseUnityPlugin> plugins = BepInExUtils.GetPlugins();
                if (plugins == null) { return; }
                if (plugins.Keys.Contains("org.bepinex.plugins.targetportal")) {
                    IsTargetPortalInstalled = true;
                }
            } catch {
                Logger.LogWarning("Unable to check mod compatibility. Ensure that Bepinex can load.");
            }
        }

        internal static void TargetPortalCompat() {
            if (!IsTargetPortalInstalled) return;

            Logger.LogInfo("TargetPortal detected, applying compatibility patches...");

            // Patch TargetPortal's nested OpenMapOnPortalEnter.Prefix directly. This is more
            // reliable than patching TeleportWorldTrigger.OnTriggerEnter with [HarmonyBefore],
            // because hooking their Prefix method lets us short-circuit it before its body runs.
            var mapType = AccessTools.TypeByName("TargetPortal.Map");
            var openMapType = mapType?.GetNestedType("OpenMapOnPortalEnter", AccessTools.all);
            var targetPortalPrefix = openMapType != null ? AccessTools.Method(openMapType, "Prefix") : null;

            if (targetPortalPrefix == null) {
                Logger.LogWarning("Could not find TargetPortal.Map.OpenMapOnPortalEnter.Prefix - TargetPortal compatibility patches will not be applied.");
                return;
            }

            var blocker = new HarmonyMethod(AccessTools.Method(typeof(Compatibility), nameof(BlockOrTrackPortalEntry)));
            AdjustablePortals.Harmony.Patch(targetPortalPrefix, prefix: blocker);

            var teleportToMethod = AccessTools.Method(typeof(Player), nameof(Player.TeleportTo));
            var teleportToPostfix = new HarmonyMethod(AccessTools.Method(typeof(Compatibility), nameof(TeleportToPostfix)));
            AdjustablePortals.Harmony.Patch(teleportToMethod, postfix: teleportToPostfix);

            Logger.LogInfo("TargetPortal compatibility patches applied.");
        }

        // This must be __0, as that is the first parameter of the injected harmony method, we do not get harmony's usual __instance, since this is a patch of a patch
        private static bool BlockOrTrackPortalEntry(TeleportWorldTrigger __0) {
            if (__0 == null || __0.m_teleportWorld == null) {
                // This fails open
                return true;
            }

            if (ActivationRequirements.PortalInstanceActivatable.AreActivationRequirementsMet(__0.m_teleportWorld, out string reason) == false) {
                if (Player.m_localPlayer != null) {
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center, reason);
                }
                return false; // Skip TargetPortal's Prefix body - map will not open
            }

            activeSourcePortal = __0.m_teleportWorld;
            activeSourcePortalExpiry = Time.time + ActiveSourcePortalLifetime;
            return true;
        }

        private static void TeleportToPostfix(Player __instance) {
            if (__instance != Player.m_localPlayer || activeSourcePortal == null) {
                return;
            }
            // Expired means the player walked away from the map instead of picking a destination,
            // so whatever moved them just now was not this portal.
            if (Time.time > activeSourcePortalExpiry) {
                activeSourcePortal = null;
                return;
            }
            ActivationRequirements.PortalInstanceActivatable.ConsumeFuel(activeSourcePortal);
            activeSourcePortal = null;
        }
    }
}
