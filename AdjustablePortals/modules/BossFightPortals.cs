using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace AdjustablePortals.modules {

    /// <summary>
    /// The world modifier that turns portals off during boss fights, with its world-wide half made optional.
    /// </summary>
    /// <remarks>
    /// With NoBossPortals set, vanilla refuses a portal for either of two reasons: the player's own boss
    /// health bar is up, which means an alerted boss within 100 m, or the world's activeBosses count is
    /// above zero. Any alerted boss anywhere raises that count, so one group's fight stops every portal
    /// on the server. BossFightsElsewhereBlockPortals decides whether the count is read at all; the
    /// player's own fight always blocks.
    /// </remarks>
    internal static class BossFightPortals {

        /// <summary>
        /// Whether a boss fight keeps the local player from travelling by portal right now.
        /// </summary>
        internal static bool BlocksTravel() {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null || zones.GetGlobalKey(GlobalKeys.NoBossPortals) == false) {
                return false;
            }
            if (RandEventSystem.instance != null && RandEventSystem.instance.GetBossEvent() != null) {
                return true;
            }
            return ReadGlobalKey(zones, GlobalKeys.activeBosses, out float activeBosses) && activeBosses > 0f;
        }

        // ZoneSystem.GetGlobalKey(GlobalKeys, out float), except that activeBosses reads as unset while
        // boss fights elsewhere are not to block portals.
        private static bool ReadGlobalKey(ZoneSystem zones, GlobalKeys key, out float value) {
            if (key == GlobalKeys.activeBosses && ValConfig.BossFightsElsewhereBlockPortals.Value == false) {
                value = 0f;
                return false;
            }
            return zones.GetGlobalKey(key, out value);
        }

        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
        private static class VanillaPortal {

            // Vanilla reads activeBosses with the only GetGlobalKey(GlobalKeys, out float) call in
            // Teleport. ReadGlobalKey takes the same arguments in the same order, so the call is all
            // that changes.
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
                MethodInfo getKeyValue = AccessTools.Method(typeof(ZoneSystem), nameof(ZoneSystem.GetGlobalKey), new[] { typeof(GlobalKeys), typeof(float).MakeByRefType() });
                CodeMatcher matcher = new CodeMatcher(instructions).MatchStartForward(new CodeMatch(OpCodes.Callvirt, getKeyValue));
                if (matcher.IsInvalid) {
                    // Not thrown: that would stop every patch after this one, and vanilla's own check
                    // still works, just without the setting.
                    Logger.LogWarning("Could not find the boss fight check in TeleportWorld.Teleport. Boss fights anywhere will block portals that pair by name.");
                    return matcher.InstructionEnumeration();
                }
                matcher.Set(OpCodes.Call, AccessTools.Method(typeof(BossFightPortals), nameof(ReadGlobalKey)));
                return matcher.InstructionEnumeration();
            }
        }
    }
}
