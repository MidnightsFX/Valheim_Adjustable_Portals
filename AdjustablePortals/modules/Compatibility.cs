using HarmonyLib;
using Jotunn.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace AdjustablePortals.modules {
    internal static class Compatibility {

        internal const string BackpacksGUID = "org.bepinex.plugins.backpacks";

        public static bool IsBackpacksInstalled = false;
        private static BepInEx.BaseUnityPlugin backpacksPlugin = null;
        // Backpacks.API.GetAllBackpackInventories, bound once Backpacks is known to be loaded.
        private static Func<Inventory, List<Inventory>> getBackpackInventories = null;
        private static bool backpacksStale = false;

        internal static void CheckModCompat() {
            try {
                Dictionary<string, BepInEx.BaseUnityPlugin> plugins = BepInExUtils.GetPlugins();
                if (plugins == null) { return; }
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
    }
}
