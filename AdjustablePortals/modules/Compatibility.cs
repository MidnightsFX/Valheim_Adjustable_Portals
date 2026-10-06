using AdjustablePortals.common;
using HarmonyLib;
using Jotunn.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AdjustablePortals.modules {
    internal static class Compatibility {

        internal const string BackpacksGUID = "org.bepinex.plugins.backpacks";

        public static bool IsTargetPortalInstalled = false;
        public static bool IsBackpacksInstalled = false;
        private static BepInEx.BaseUnityPlugin backpacksPlugin = null;
        // Backpacks.API.GetAllBackpackInventories, bound once Backpacks is known to be loaded.
        private static Func<Inventory, List<Inventory>> getBackpackInventories = null;
        private static bool backpacksStale = false;
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
                if (plugins.TryGetValue(BackpacksGUID, out backpacksPlugin) && backpacksPlugin != null) {
                    IsBackpacksInstalled = true;
                }
            } catch {
                Logger.LogWarning("Unable to check mod compatibility. Ensure that Bepinex can load.");
            }
        }

        /// <summary>
        /// Backpacks decides whether a backpack may go through a portal by asking the backpack's own
        /// inventory - which this mod's patches answer - and writing the result onto the backpack
        /// item's teleport flag. It only does that when the backpack's contents change, though, so
        /// a boss kill or a config change leaves every backpack on its old answer until the player
        /// happens to move something in or out of it. This hands it the cue it is missing.
        /// </summary>
        internal static void BackpacksCompat() {
            if (!IsBackpacksInstalled) return;

            // Looked up in Backpacks' own assembly rather than by name across everything loaded.
            // Backpacks also publishes an API stub for other mods to bundle, with the same type name
            // and every method returning nothing, and a lookup by name can land on that instead.
            Type api = backpacksPlugin.GetType().Assembly.GetType("Backpacks.API");
            MethodInfo method = api != null ? AccessTools.Method(api, "GetAllBackpackInventories", new Type[] { typeof(Inventory) }) : null;
            if (method == null || method.ReturnType != typeof(List<Inventory>)) {
                Logger.LogWarning("Could not find Backpacks.API.GetAllBackpackInventories - backpacks may keep a stale portal restriction until their contents change.");
                return;
            }
            getBackpackInventories = (Func<Inventory, List<Inventory>>)Delegate.CreateDelegate(typeof(Func<Inventory, List<Inventory>>), method);
            Logger.LogInfo("Backpacks detected, backpack portal restrictions will follow boss progression.");
        }

        /// <summary>
        /// Notes that every backpack's teleport flag may be out of date. The refresh itself waits for
        /// <see cref="RefreshStaleBackpacks"/>: this is called from inside key and config updates that
        /// fire many times in one frame, and while the player is still loading.
        /// </summary>
        internal static void MarkBackpacksStale() {
            backpacksStale = true;
        }

        internal static void RefreshStaleBackpacks() {
            if (backpacksStale == false || getBackpackInventories == null) {
                return;
            }
            Player player = Player.m_localPlayer;
            // Left marked: the player loading in is one of the things that marks it.
            if (player == null) {
                return;
            }
            backpacksStale = false;
            try {
                // Changed is what Backpacks listens to, and it reruns exactly the check it would
                // have run had the player moved an item - the backpack's own "ignore portals"
                // settings included.
                foreach (Inventory backpack in getBackpackInventories(player.GetInventory())) {
                    backpack?.Changed();
                }
            } catch (Exception ex) {
                Logger.LogWarning($"Unable to refresh backpack portal restrictions: {ex.Message}");
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
