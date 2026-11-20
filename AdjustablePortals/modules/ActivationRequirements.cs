using AdjustablePortals.common;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdjustablePortals.modules {
    internal static class ActivationRequirements {

        private const string NearbyPiecesKey = "AJP_PORT_BUILD_NEARBY";
        // What a portal reads as until the server has counted it.
        private const int NotCounted = -1;

        /// <summary>
        /// The server's count of the building pieces around each portal, kept on the portal's ZDO
        /// so that every player reads the same number.
        /// </summary>
        /// <remarks>
        /// Counted on the server because it is the only peer that holds every piece in the world.
        /// A client only has the zones its own simulation distance reaches, and the portal's owner
        /// is whichever player in range the server picked, often one at the far edge of the base
        /// with half of it unloaded.
        /// </remarks>
        internal static class NearbyPieceCounts {

            private const string SetNearbyPiecesRPC = "AJP_SetNearbyPieces";

            private const float NearbyRecountSeconds = 10f;
            // Nobody close to the portal. Its pieces can still change at the far end of a large
            // radius, or by raids and decay around a player just out of reach.
            private const float IdleRecountSeconds = 300f;
            // How far past the counting radius a player still counts as close enough to be
            // building inside it.
            private const float NearbyPlayerMargin = 64f;
            private const float PlanIntervalSeconds = 1f;
            // A single count in a large base can take longer than this on its own; at least one
            // always runs per frame.
            private const double FrameBudgetMilliseconds = 1.0;

            private static readonly Dictionary<ZDOID, float> lastCountedAt = new Dictionary<ZDOID, float>();
            // ZDOIDs rather than ZDOs: ZDOs are pooled, and one destroyed while it waits here
            // would come back as some other object.
            private static readonly List<ZDOID> queue = new List<ZDOID>();
            private static readonly List<ZDOID> idleQueue = new List<ZDOID>();
            private static readonly List<ZDOID> pruneBuffer = new List<ZDOID>();
            private static int queueIndex = 0;
            private static float nextPlanTime = 0f;
            private static readonly System.Diagnostics.Stopwatch frameTimer = new System.Diagnostics.Stopwatch();

            internal static int Read(ZDO portal) {
                return portal.GetInt(NearbyPiecesKey, NotCounted);
            }

            /// <summary>
            /// Recounts the portals that are due, within a per frame budget. Server only.
            /// </summary>
            internal static void Update() {
                if (ZNet.instance == null || ZNet.instance.IsServer() == false || ZDOMan.instance == null || ZNetScene.instance == null || ZoneSystem.instance == null) {
                    return;
                }
                if (ValConfig.EnablePortalPieceRequirements.Value == false) {
                    return;
                }

                float now = Time.time;
                if (queueIndex >= queue.Count) {
                    if (now < nextPlanTime) {
                        return;
                    }
                    nextPlanTime = now + PlanIntervalSeconds;
                    Plan(now);
                }

                frameTimer.Restart();
                while (queueIndex < queue.Count) {
                    ZDO portal = ZDOMan.instance.GetZDO(queue[queueIndex++]);
                    if (portal != null) {
                        Recount(portal, now);
                    }
                    if (frameTimer.Elapsed.TotalMilliseconds >= FrameBudgetMilliseconds) {
                        break;
                    }
                }
            }

            private static void Plan(float now) {
                queue.Clear();
                idleQueue.Clear();
                queueIndex = 0;

                float reach = ValConfig.PortalPieceActivationDistance.Value + NearbyPlayerMargin;
                float reachSq = reach * reach;
                int portalCount = 0;
                foreach (List<ZDO> sectorPortals in ZDOMan.instance.GetPortals().Values) {
                    for (int i = 0; i < sectorPortals.Count; i++) {
                        ZDO portal = sectorPortals[i];
                        portalCount++;
                        bool nearby = PlayerNear(portal.GetPosition(), reachSq);
                        if (lastCountedAt.TryGetValue(portal.m_uid, out float last) && now - last < (nearby ? NearbyRecountSeconds : IdleRecountSeconds)) {
                            continue;
                        }
                        (nearby ? queue : idleQueue).Add(portal.m_uid);
                    }
                }
                // Portals with someone at them first, since theirs are the counts being looked at.
                queue.AddRange(idleQueue);
                idleQueue.Clear();

                if (lastCountedAt.Count > portalCount) {
                    ForgetDestroyedPortals();
                }
                PieceProximity.PruneUnused();
            }

            private static bool PlayerNear(Vector3 position, float reachSq) {
                // A host's own player is not in its peer list.
                if (Player.m_localPlayer != null && HorizontalDistanceSq(ZNet.instance.GetReferencePosition(), position) < reachSq) {
                    return true;
                }
                List<ZNetPeer> peers = ZNet.instance.GetPeers();
                for (int i = 0; i < peers.Count; i++) {
                    if (HorizontalDistanceSq(peers[i].m_refPos, position) < reachSq) {
                        return true;
                    }
                }
                return false;
            }

            private static float HorizontalDistanceSq(Vector3 a, Vector3 b) {
                float dx = a.x - b.x;
                float dz = a.z - b.z;
                return dx * dx + dz * dz;
            }

            private static void Recount(ZDO portal, float now) {
                lastCountedAt[portal.m_uid] = now;
                int pieces = PieceProximity.CountWithin(portal.GetPosition(), ValConfig.PortalPieceActivationDistance.Value);
                int stored = Read(portal);
                if (pieces == stored) {
                    return;
                }
                long owner = portal.GetOwner();
                Logger.LogDebug($"Portal {portal.m_uid} now has {pieces} pieces nearby (was {stored}), owner {owner}.");

                // The same split vanilla's Game.SetConnection makes for the pairing it writes from
                // the server. A write from anyone but the owner races the owner's own writes, such
                // as fuel, and one of the two is lost, so a connected owner is asked to make it.
                if (owner != 0L && ZNet.instance.GetPeer(owner) != null) {
                    ZRoutedRpc.instance.InvokeRoutedRPC(owner, SetNearbyPiecesRPC, portal.m_uid, pieces);
                    return;
                }
                portal.SetOwner(ZDOMan.GetSessionID());
                portal.Set(NearbyPiecesKey, pieces);
            }

            private static void RPC_SetNearbyPieces(long sender, ZDOID portalId, int pieces) {
                if (sender != ZRoutedRpc.instance.GetServerPeerID()) {
                    return;
                }
                ZDO portal = ZDOMan.instance.GetZDO(portalId);
                // Ownership can move on while this is in flight. The server still sees the old
                // count on its next pass and sends it to the new owner.
                if (portal == null || portal.IsOwner() == false) {
                    return;
                }
                portal.Set(NearbyPiecesKey, pieces);
            }

            private static void ForgetDestroyedPortals() {
                pruneBuffer.Clear();
                foreach (ZDOID id in lastCountedAt.Keys) {
                    if (ZDOMan.instance.GetZDO(id) == null) {
                        pruneBuffer.Add(id);
                    }
                }
                for (int i = 0; i < pruneBuffer.Count; i++) {
                    lastCountedAt.Remove(pruneBuffer[i]);
                }
                pruneBuffer.Clear();
            }

            internal static void RadiusChanged(object sender, EventArgs e) {
                lastCountedAt.Clear();
                queue.Clear();
                queueIndex = 0;
            }

            internal static void Clear() {
                lastCountedAt.Clear();
                queue.Clear();
                idleQueue.Clear();
                pruneBuffer.Clear();
                queueIndex = 0;
                nextPlanTime = 0f;
                PieceProximity.Clear();
            }

            [HarmonyPatch(typeof(Game), nameof(Game.Start))]
            private static class RegisterRPCs {
                private static void Postfix() {
                    ZRoutedRpc.instance.Register<ZDOID, int>(SetNearbyPiecesRPC, RPC_SetNearbyPieces);
                }
            }
        }

        [HarmonyPatch(typeof(TeleportWorld))]
        internal static class PortalInstanceActivatable {

            private static bool PiecesMissing(ZDO portal, out string reason) {
                int required = ValConfig.PortalNearbyPiecesForActivation.Value;
                int pieces = NearbyPieceCounts.Read(portal);
                if (required <= 0 || pieces >= required) {
                    reason = "";
                    return false;
                }
                reason = pieces == NotCounted ? "Counting nearby structures..." : $"More nearby structures required ({pieces}/{required})";
                return true;
            }

            [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.HaveTarget))]
            [HarmonyPrefix]
            internal static bool Activator(TeleportWorld __instance, ref bool __result) {
                return CheckActivationRequirements(__instance, ref __result);
            }

            /// <summary>
            /// Whether a portal had enough building pieces around it when the server last counted
            /// it. Portals nobody is near are recounted every few minutes, so this can lag behind.
            /// </summary>
            internal static bool StoredPiecesMeetRequirement(ZDO portal) {
                if (ValConfig.EnablePortalPieceRequirements.Value == false) {
                    return true;
                }
                return PiecesMissing(portal, out _) == false;
            }

            public static bool AreActivationRequirementsMet(TeleportWorld instance, out string reason) {
                reason = "";
                bool result = true;
                if (instance.m_nview == null || !instance.m_nview.IsValid()) {
                    return result;
                }

                if (ValConfig.EnablePortalPieceRequirements.Value && PiecesMissing(instance.m_nview.GetZDO(), out reason)) {
                    result = false;
                }

                // This player's copy of the fuel count can be behind, so it only turns away a portal
                // that looks empty. The owner's count decides when PortalFuel spends the charge,
                // and a trip it has already paid for needs nothing more.
                if (PortalFuel.Required && PortalFuel.IsPaidFor(instance) == false && PortalFuel.Read(instance.m_nview.GetZDO()) <= 0) {
                    reason = PortalFuel.RequiredMessage();
                    result = false;
                }

                return result;
            }

            public static bool CheckActivationRequirements(TeleportWorld __instance, ref bool __result) {
                if (__instance.m_nview == null || __instance.m_nview.IsValid() == false) {
                    return true; // Let the original run
                }

                // Nothing configured to gate on, so this prefix has nothing to add - hand the call
                // back rather than reimplementing HaveTarget's connection test below. Mirrors the
                // early return in TargetFoundPrevention.
                //
                // Picking destinations is the exception: there the connection test is the wrong
                // question, since the player chooses the far end and every portal has one.
                if (ValConfig.EnablePortalPieceRequirements.Value == false && ValConfig.EnablePortalRequireFuel.Value == false) {
                    if (PortalTargeting.IsActive) {
                        __result = true;
                        return false;
                    }
                    return true;
                }

                if (!PortalTargeting.IsActive) {
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
                if (ValConfig.EnablePortalPieceRequirements.Value && PiecesMissing(__instance.m_nview.GetZDO(), out string piecesReason)) {
                    __result += Localization.instance.Localize($"\n{piecesReason}");
                }
                if (PortalFuel.Required) {
                    int fuel = PortalFuel.Read(__instance.m_nview.GetZDO());
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

        }

        /// <summary>
        /// Adds and spends portal fuel. Only the portal's owner ever changes the count.
        /// </summary>
        /// <remarks>
        /// Any other player's copy of the count is as old as their connection is slow. Checking
        /// that copy let a lagging player travel on a charge somebody else had already used, and
        /// writing to it could not be trusted either: the server keeps whichever copy has been
        /// changed more times, not the newest, so the write was either dropped or overwrote a newer
        /// count. A player who does not own the portal asks the owner to spend a charge, the way
        /// vanilla's Container asks before opening, and travels once the owner has spent it.
        /// </remarks>
        [HarmonyPatch(typeof(TeleportWorld))]
        internal static class PortalFuel {

            private const string FuelKey = "AJP_FUEL";
            private const string SpendRPC = "AJP_SpendFuel";
            private const string SpendAnswerRPC = "AJP_SpendFuelAnswer";
            private const string AddRPC = "AJP_AddFuel";
            // An owner that left, or handed the portal on while the question was in flight, never
            // answers.
            private const float AnswerTimeoutSeconds = 5f;

            private static bool waiting = false;
            private static TeleportWorld waitingPortal = null;
            private static Func<Player, bool> waitingTrip = null;
            private static int requestId = 0;
            private static float requestTime = 0f;
            // Set while a trip whose charge is already spent runs, so the checks it passes through
            // on the way neither turn it away on a stale count nor charge it again.
            private static TeleportWorld paidPortal = null;

            internal static bool Required {
                get { return ValConfig.EnablePortalRequireFuel.Value; }
            }

            internal static int Read(ZDO portal) {
                return portal.GetInt(FuelKey, 0);
            }

            internal static string RequiredMessage() {
                return $"Fuel is required, add {ValConfig.PortalFuelPrefab.Value} x {ValConfig.PortalFuelBatchSize.Value}";
            }

            internal static bool IsPaidFor(TeleportWorld portal) {
                return paidPortal != null && paidPortal == portal;
            }

            /// <summary>
            /// Has the owner of <paramref name="portal"/> spend one charge, then runs
            /// <paramref name="trip"/>, which returns whether the player went. A trip that did not
            /// go gets its charge back. Runs the trip at once when fuel is not required or the local
            /// player owns the portal.
            /// </summary>
            internal static void SpendThen(TeleportWorld portal, Func<Player, bool> trip) {
                if (Required == false) {
                    trip(Player.m_localPlayer);
                    return;
                }
                if (waiting || portal == null || portal.m_nview == null || portal.m_nview.IsValid() == false) {
                    return;
                }
                // Set before asking. When the local player owns the portal, the answer comes back
                // inside this very call.
                waiting = true;
                waitingPortal = portal;
                waitingTrip = trip;
                requestTime = Time.time;
                requestId++;
                InvokeOnOwner(portal.m_nview, SpendRPC, requestId);
            }

            private static void InvokeOnOwner(ZNetView nview, string method, int value) {
                // An unowned ZDO's RPCs go to nobody. Vanilla claims one for the nearest player
                // shortly after it loads, so this only covers the moment before that.
                if (nview.GetZDO().HasOwner() == false) {
                    nview.ClaimOwnership();
                }
                nview.InvokeRPC(method, value);
            }

            internal static void Update() {
                if (waiting == false || Time.time - requestTime < AnswerTimeoutSeconds) {
                    return;
                }
                ClearSessionState();
                if (Player.m_localPlayer != null) {
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center, "The portal did not respond, try again.");
                }
            }

            internal static void ClearSessionState() {
                waiting = false;
                waitingPortal = null;
                waitingTrip = null;
                paidPortal = null;
            }

            [HarmonyPatch(nameof(TeleportWorld.Awake))]
            [HarmonyPostfix]
            private static void RegisterRPCs(TeleportWorld __instance) {
                ZNetView nview = __instance.m_nview;
                if (nview == null || nview.GetZDO() == null) {
                    return;
                }
                nview.Register<int>(SpendRPC, (sender, id) => RPC_Spend(__instance, sender, id));
                nview.Register<int, bool>(SpendAnswerRPC, (sender, id, granted) => RPC_SpendAnswer(__instance, id, granted));
                nview.Register<int>(AddRPC, (sender, amount) => RPC_Add(__instance, amount));
            }

            private static void RPC_Spend(TeleportWorld portal, long sender, int id) {
                if (portal.m_nview.IsOwner() == false) {
                    return;
                }
                ZDO zdo = portal.m_nview.GetZDO();
                int fuel = Read(zdo);
                bool granted = fuel > 0;
                if (granted) {
                    zdo.Set(FuelKey, fuel - 1);
                }
                portal.m_nview.InvokeRPC(sender, SpendAnswerRPC, id, granted);
            }

            private static void RPC_SpendAnswer(TeleportWorld portal, int id, bool granted) {
                if (waiting == false || portal != waitingPortal || id != requestId) {
                    // Answered after this player gave up waiting.
                    if (granted) {
                        InvokeOnOwner(portal.m_nview, AddRPC, 1);
                    }
                    return;
                }
                Func<Player, bool> trip = waitingTrip;
                waiting = false;
                waitingPortal = null;
                waitingTrip = null;

                Player player = Player.m_localPlayer;
                if (granted == false) {
                    if (player != null) {
                        player.Message(MessageHud.MessageType.Center, RequiredMessage());
                    }
                    return;
                }
                bool went = false;
                if (player != null && player.IsDead() == false) {
                    paidPortal = portal;
                    try {
                        went = trip(player);
                    } finally {
                        paidPortal = null;
                    }
                }
                if (went) {
                    Logger.LogDebug("Spent 1 usage of portal fuel.");
                } else {
                    InvokeOnOwner(portal.m_nview, AddRPC, 1);
                }
            }

            private static void RPC_Add(TeleportWorld portal, int amount) {
                if (portal.m_nview.IsOwner() == false) {
                    return;
                }
                ZDO zdo = portal.m_nview.GetZDO();
                zdo.Set(FuelKey, Read(zdo) + amount);
            }

            [HarmonyPatch(nameof(TeleportWorld.UseItem))]
            [HarmonyPrefix]
            private static bool AddFuelOnUseItem(TeleportWorld __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result) {
                if (__instance.m_nview == null || __instance.m_nview.IsValid() == false || Required == false) {
                    return true;
                }
                if (user == null || user.m_inventory == null || item == null || item.m_dropPrefab == null || item.m_dropPrefab.name != ValConfig.PortalFuelPrefab.Value) {
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

                InvokeOnOwner(__instance.m_nview, AddRPC, ValConfig.PortalFuelUsagesPerBatch.Value);
                user.Message(MessageHud.MessageType.Center, $"Added {ValConfig.PortalFuelPrefab.Value}x{batchSize} for {ValConfig.PortalFuelUsagesPerBatch.Value} fuel.");
                __result = true;
                return false;
            }

            // Holds back a trip vanilla would let through until the owner has spent its charge,
            // then sends it through again. First, so that nothing else acts on a trip that is not
            // happening yet.
            [HarmonyPatch(nameof(TeleportWorld.Teleport))]
            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static bool PayBeforeTeleport(TeleportWorld __instance, Player player) {
                if (Required == false || player == null || player != Player.m_localPlayer || IsPaidFor(__instance)) {
                    return true;
                }
                // A trip vanilla turns down is left to vanilla, which says why, and costs nothing.
                if (player.IsTeleporting() || player.m_teleportCooldown < PortalTargeting.TeleportCooldownSeconds) {
                    return true;
                }
                if (__instance.TargetFound() == false || PortalTargeting.CanTravelFrom(__instance, player, out _) == false) {
                    return true;
                }
                SpendThen(__instance, traveller => {
                    __instance.Teleport(traveller);
                    return traveller.IsTeleporting();
                });
                return false;
            }
        }

        // ZDOIDs are not stable across sessions, so none of the per-portal state may survive one.
        // Separate class rather than a method on the one above, whose class-level attribute scopes
        // it to TeleportWorld.
        [HarmonyPatch(typeof(ZNetScene))]
        internal static class PortalCacheLifetime {

            [HarmonyPatch(nameof(ZNetScene.Shutdown))]
            [HarmonyPostfix]
            private static void OnShutdown() {
                NearbyPieceCounts.Clear();
                PortalFuel.ClearSessionState();
                TeleportCompanions.ClearSessionState();
                PortalTargeting.ClearSessionState();
            }
        }
    }
}
