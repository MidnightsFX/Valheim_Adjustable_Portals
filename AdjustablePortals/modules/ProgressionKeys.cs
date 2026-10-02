using HarmonyLib;

namespace AdjustablePortals.modules {
    /// <summary>
    /// Where boss progression is read from: the world's global keys, or with UsePrivateKeys, the
    /// private keys each player holds for the bosses they have helped defeat.
    /// </summary>
    /// <remarks>
    /// The private keys are vanilla's own - the same defeated_* names, kept on the character and
    /// saved with it - so they are also what the PlayerEvents world modifier reads.
    /// </remarks>
    internal static class ProgressionKeys {

        private const string GrantBossKeyRPC = "AJP_GrantBossKey";

        internal static bool HasKey(string key) {
            if (ValConfig.UsePrivateKeys.Value == false) {
                return ZoneSystem.instance.GetGlobalKey(key);
            }
            return Player.m_localPlayer != null && Player.m_localPlayer.HaveUniqueKey(key);
        }

        [HarmonyPatch(typeof(Game), nameof(Game.Start))]
        private static class RegisterRPCs {
            // ZRoutedRpc is recreated with ZNet every session, and Game.Start is where vanilla registers its own.
            private static void Postfix() {
                ZRoutedRpc.instance.Register<string>(GrantBossKeyRPC, RPC_GrantBossKey);
            }
        }

        /// <summary>
        /// Vanilla's private boss key is not enough to progress a party on. It is only queued on
        /// the clients that run the boss's death, which for every boss this mod unlocks items on
        /// is the boss's owner alone - whoever's game happened to be simulating it, who need not
        /// have fought it. The queue is then emptied into whichever Player wakes next, and that
        /// can be another player's avatar loading in rather than the local player.
        ///
        /// So the owner credits the key to every player that landed a hit, the same list vanilla
        /// credits the kill to, and each of them adds it to their own character directly. Only
        /// bosses: the keys on lesser creatures (killed_surtling, jotun_killed) are nothing this
        /// mod reads, so they keep vanilla's rules.
        /// </summary>
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class CreditBossKeyToAttackers {
            // A prefix, as OnDeath ends by destroying the boss and the attacker list lives on its ZDO.
            private static void Prefix(Character __instance) {
                if (ValConfig.UsePrivateKeys.Value == false || __instance.IsBoss() == false || string.IsNullOrEmpty(__instance.m_defeatSetGlobalKey)) {
                    return;
                }
                // Bosses with a death animation run OnDeath on every client that sees it, and only
                // one of them may hand out credit.
                ZNetView nview = __instance.m_nview;
                if (nview == null || nview.IsValid() == false || nview.IsOwner() == false) {
                    return;
                }

                ZDO zdo = nview.GetZDO();
                foreach (ZNet.PlayerInfo player in ZNet.instance.GetPlayerList()) {
                    // Peer 0 is a broadcast, so a player without a character yet must not be sent
                    // to by their character's user ID.
                    if (player.m_characterID.IsNone() || zdo.GetBool(ZDOVars.s_attackers + player.m_name) == false) {
                        continue;
                    }
                    ZRoutedRpc.instance.InvokeRoutedRPC(player.m_characterID.UserID, GrantBossKeyRPC, __instance.m_defeatSetGlobalKey);
                }
            }
        }

        private static void RPC_GrantBossKey(long sender, string key) {
            Player player = Player.m_localPlayer;
            if (player == null) {
                // Nobody to give it to until the player respawns. Vanilla's own queue is the only
                // thing that carries a key across that.
                Player.m_addUniqueKeyQueue.Add(key);
                return;
            }
            if (player.HaveUniqueKey(key) == false) {
                Logger.LogDebug($"Credited with {key} for taking part in the kill.");
                player.AddUniqueKey(key);
            }
        }
    }
}
