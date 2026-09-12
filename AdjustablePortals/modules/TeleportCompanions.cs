using AdjustablePortals.common;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AdjustablePortals.modules {

    /// <summary>
    /// Takes what the player was travelling with through the portal too: the mount they were riding,
    /// tamed creatures standing around the portal, and the cart they were pulling.
    /// </summary>
    /// <remarks>
    /// Everything here runs on the teleporting player's own client. TeleportWorldTrigger only
    /// fires for the local player, and Player.TeleportTo hands a non-owner's call off over RPC
    /// before doing anything, so there is no path where this moves someone else's companions.
    /// </remarks>
    internal static class TeleportCompanions {

        /// <summary>
        /// What a portal trip needs to know, captured before the teleport starts. By the time the
        /// teleport has actually begun the answers are already going away: Vagon.Update detaches
        /// the cart the moment the player counts as teleporting, and the TargetPortal
        /// compatibility layer clears its own note of the source portal in a postfix that Harmony
        /// is free to run before ours.
        /// </summary>
        private struct PortalTrip {
            public bool FromPortal;
            public Vector3 Center;
            public Vector3 Destination;
            public Character Mount;
            public Vagon Cart;
            public string CartBlockedBy;
        }

        // TeleportWorld.Teleport calls Player.TeleportTo from its own body, so a frame stamp is
        // all it takes to pair the two - and to tell a portal trip apart from every other
        // TeleportTo, none of which should drag a herd along.
        private static TeleportWorld vanillaSourcePortal = null;
        private static int vanillaSourcePortalFrame = -1;

        // Companions are fanned out around the exit rather than stacked on it, so a lox does not
        // land inside the player. Sunflower spacing: no ring sizes to pick, and it stays compact
        // as the count grows.
        private const float CompanionSpacing = 1.6f;
        private const float GoldenAngleDegrees = 137.5f;

        // Player.UpdateTeleport gives up on a destination that never finishes loading at 15s, so
        // anything still waiting past this was never going to arrive.
        private const float ArrivalTimeoutSeconds = 20f;

        // Enough to tell the player what to unload without papering the screen with a manifest.
        private const int MaxBlockingItemsListed = 3;

        // Reused across teleports rather than reallocated; only ever touched on the main thread.
        private static readonly List<Character> tameBuffer = new List<Character>();
        private static readonly List<string> blockedItemBuffer = new List<string>();

        /// <summary>
        /// Drops the reference to the last portal walked into. Nothing reads it across a session
        /// boundary - the frame stamp guarding it never matches again - but there is no reason to
        /// keep a destroyed portal alive until the next one comes along.
        /// </summary>
        internal static void ClearSessionState() {
            vanillaSourcePortal = null;
            vanillaSourcePortalFrame = -1;
            tameBuffer.Clear();
            blockedItemBuffer.Clear();
        }

        private static Vector3 CompanionOffset(int index) {
            float angle = index * GoldenAngleDegrees * Mathf.Deg2Rad;
            float radius = CompanionSpacing * Mathf.Sqrt(index + 1);
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        /// <summary>
        /// The cart <paramref name="player"/> is currently pulling, or null.
        /// </summary>
        /// <remarks>
        /// The joint only exists on the client that built it, and Vagon.Update detaches on every
        /// other client - so a cart that still has one here is one the local player owns.
        /// </remarks>
        internal static Vagon GetAttachedCart(Player player) {
            if (player == null || Vagon.m_instances == null) {
                return null;
            }
            GameObject playerObject = player.gameObject;
            foreach (Vagon cart in Vagon.m_instances) {
                if (cart == null || cart.m_attachJoin == null) {
                    continue;
                }
                if (cart.m_attachedObject == playerObject) {
                    return cart;
                }
                // m_attachedObject is only set by the client that built the joint. The joint
                // itself is the same question asked from the other direction.
                if (cart.m_attachJoin.connectedBody != null && cart.m_attachJoin.connectedBody.gameObject == playerObject) {
                    return cart;
                }
            }
            return null;
        }

        /// <summary>
        /// The creature <paramref name="player"/> is riding, or null.
        /// </summary>
        private static Character GetRiddenMount(Player player) {
            if (player == null || player.IsRiding() == false) {
                return null;
            }
            Sadle saddle = player.GetDoodadController() as Sadle;
            if (saddle == null) {
                return null;
            }
            Character mount = saddle.GetCharacter();
            if (mount == null || mount.IsDead() || mount.m_nview == null || mount.m_nview.IsValid() == false) {
                return null;
            }
            return mount;
        }

        /// <summary>
        /// Whether every item in the cart may go through a portal, judged by the same progression
        /// allowances that apply to the player's own inventory.
        /// </summary>
        internal static bool IsCartTeleportable(Vagon cart, bool allowAllItems, out string blockedBy) {
            blockedBy = "";
            if (cart == null || cart.m_container == null) {
                return true;
            }
            Inventory inventory = cart.m_container.GetInventory();
            if (inventory == null) {
                return true;
            }

            bool teleportAllKey = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.TeleportAll);
            blockedItemBuffer.Clear();
            foreach (ItemDrop.ItemData item in inventory.GetAllItems()) {
                if (item == null || item.m_shared == null) {
                    continue;
                }
                // Mirrors Inventory.IsTeleportable, which refuses these ahead of both the portal's
                // allowAllItems flag and the TeleportAll key - not a restriction this mod lifts.
                if (item.m_shared.m_toolTier < 1000) {
                    if (allowAllItems || teleportAllKey) {
                        continue;
                    }
                    if (TeleportItems.PerPlayerTeleportableItems.IsItemTeleportable(item)) {
                        continue;
                    }
                }
                string name = Localization.instance.Localize(item.m_shared.m_name);
                if (blockedItemBuffer.Contains(name) == false) {
                    blockedItemBuffer.Add(name);
                }
            }

            if (blockedItemBuffer.Count == 0) {
                return true;
            }
            int listed = Mathf.Min(blockedItemBuffer.Count, MaxBlockingItemsListed);
            blockedBy = string.Join(", ", blockedItemBuffer.GetRange(0, listed).ToArray());
            if (blockedItemBuffer.Count > listed) {
                blockedBy += $", +{blockedItemBuffer.Count - listed}";
            }
            blockedItemBuffer.Clear();
            return false;
        }

        private static bool IsFollowing(Character tame, Player player) {
            MonsterAI ai = tame.GetComponent<MonsterAI>();
            if (ai != null && ai.GetFollowTarget() == player.gameObject) {
                return true;
            }
            // The follow target is persisted as a player name, and is what Tameable re-reads to
            // resume following after a reload - so it is also the only answer available for a tame
            // whose AI is running on somebody else's client.
            ZDO zdo = tame.m_nview.GetZDO();
            return zdo != null && zdo.GetString(ZDOVars.s_follow, "") == player.GetPlayerName();
        }

        private static void GatherTames(Player player, Vector3 center, Character exclude, List<Character> into) {
            into.Clear();
            float radiusSqr = ValConfig.TeleportTamesRadius.Value * ValConfig.TeleportTamesRadius.Value;
            bool requireFollowing = ValConfig.TeleportTamesRequireFollowing.Value;

            List<Character> characters = Character.GetAllCharacters();
            for (int i = 0; i < characters.Count; i++) {
                Character candidate = characters[i];
                if (candidate == null || candidate == exclude || candidate.IsPlayer() || candidate.IsDead()) {
                    continue;
                }
                if (candidate.m_nview == null || candidate.m_nview.IsValid() == false) {
                    continue;
                }
                if (candidate.IsTamed() == false) {
                    continue;
                }
                if (Utils.DistanceSqr(candidate.transform.position, center) > radiusSqr) {
                    continue;
                }
                // Somebody is riding this one. Moving it out from under them would strand the
                // rider's controls on a creature that is no longer where they are.
                Sadle saddle = candidate.GetComponentInChildren<Sadle>();
                if (saddle != null && saddle.HaveValidUser()) {
                    continue;
                }
                if (requireFollowing && IsFollowing(candidate, player) == false) {
                    continue;
                }
                into.Add(candidate);
            }

            // Nearest first, so a capped list keeps the tames crowding the portal rather than an
            // arbitrary few of whatever order the character list happened to be in.
            into.Sort((a, b) => Utils.DistanceSqr(a.transform.position, center).CompareTo(Utils.DistanceSqr(b.transform.position, center)));
            int max = ValConfig.TeleportTamesMaxCount.Value;
            if (into.Count > max) {
                into.RemoveRange(max, into.Count - max);
            }
        }

        /// <summary>
        /// Moves a loaded object and its ZDO to <paramref name="destination"/>.
        /// </summary>
        /// <remarks>
        /// The player's own transform does not move until two seconds in, so for that window the
        /// companion sits in a zone nobody is standing in and ZNetScene unloads it. That is fine,
        /// and is why the ZDO is written explicitly instead of being left to ZSyncTransform: the
        /// ZDO is what survives the unload and what the destination respawns from.
        /// </remarks>
        private static void MoveToDestination(ZNetView nview, Transform transform, Vector3 destination) {
            nview.ClaimOwnership();
            transform.position = destination;
            nview.GetZDO().SetPosition(destination);
        }

        private static void MoveTame(Character tame, Vector3 destination) {
            MoveToDestination(tame.m_nview, tame.transform, destination);
            if (tame.m_body != null) {
                tame.m_body.position = destination;
                tame.m_body.linearVelocity = Vector3.zero;
                tame.m_body.angularVelocity = Vector3.zero;
            }
            // The portal exit sits a metre above its floor. Without this the drop out of it is
            // counted as a fall and the tame lands hurt.
            tame.m_maxAirAltitude = destination.y;
        }

        private static void MoveCart(Vagon cart, Vector3 destination) {
            MoveToDestination(cart.m_nview, cart.transform, destination);
            if (cart.m_bodies == null) {
                return;
            }
            // The wheels are rigidbodies of their own under the cart. Their transforms came along
            // with the root, but each body keeps its own pose until told otherwise, and a wheel
            // left behind drags the whole cart back across the world to meet it.
            foreach (Rigidbody body in cart.m_bodies) {
                if (body == null) {
                    continue;
                }
                body.position = body.transform.position;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        private static void CarryCompanions(Player player, PortalTrip trip) {
            int placed = 0;

            if (trip.Mount != null && trip.Mount.m_nview != null && trip.Mount.m_nview.IsValid()) {
                ZDOID mountID = trip.Mount.m_nview.GetZDO().m_uid;
                // Out of the saddle before the mount moves. Player.UpdateAttach pins the rider to
                // the saddle every tick, so a mount sent ahead would pull the player straight into
                // a destination that has not loaded yet - skipping the wait UpdateTeleport exists
                // to provide, with nothing underneath either of them. Dismounted, the player takes
                // the normal teleport and the mount takes the same route as any other tame.
                player.StopDoodadControl();
                MoveTame(trip.Mount, trip.Destination + CompanionOffset(placed));
                placed++;
                Logger.LogDebug("Took a ridden mount through the portal.");
                StartArrivalCoroutine(player, mountID, "Mount", Remount);
            }

            if (ValConfig.EnableTeleportTames.Value) {
                // The mount is excluded outright rather than left to the ridden check inside,
                // which reads a flag Sadle only refreshes on its next FixedUpdate.
                GatherTames(player, trip.Center, trip.Mount, tameBuffer);
                foreach (Character tame in tameBuffer) {
                    MoveTame(tame, trip.Destination + CompanionOffset(placed));
                    placed++;
                }
                if (tameBuffer.Count > 0) {
                    Logger.LogDebug($"Took {tameBuffer.Count} tamed creature(s) through the portal.");
                }
                tameBuffer.Clear();
            }

            if (trip.Cart != null && trip.Cart.m_nview != null && trip.Cart.m_nview.IsValid()) {
                ZDOID cartID = trip.Cart.m_nview.GetZDO().m_uid;
                // Drop the hitch before moving. Vagon gets around to this itself on its next
                // tick, which is a tick too late: the joint is anchored to a player who is now a
                // world away, and its spring is what would be holding the two together until it
                // gave up and broke.
                trip.Cart.Detach();
                MoveCart(trip.Cart, trip.Destination + CompanionOffset(placed));
                Logger.LogDebug("Took a cart through the portal.");
                StartArrivalCoroutine(player, cartID, "Cart", SettleCart);
            }
        }

        private static void StartArrivalCoroutine(Player player, ZDOID id, string what, Func<Player, GameObject, IEnumerator> arrived) {
            if (AdjustablePortals.Instance != null) {
                AdjustablePortals.Instance.StartCoroutine(AfterArrival(player, id, what, arrived));
            }
        }

        /// <summary>
        /// Waits for the player to land and for the companion behind <paramref name="id"/> to be
        /// rebuilt at the destination, then hands the new instance to <paramref name="arrived"/>.
        /// </summary>
        /// <remarks>
        /// The companion was sent to the portal's exit position, but the player lands wherever
        /// ZoneSystem.FindFloor puts them, which is not the same spot - and the companion's own
        /// instance is destroyed and rebuilt in between, so nothing about it can be held as a
        /// reference across the wait.
        /// </remarks>
        private static IEnumerator AfterArrival(Player player, ZDOID id, string what, Func<Player, GameObject, IEnumerator> arrived) {
            float deadline = Time.time + ArrivalTimeoutSeconds;

            while (Time.time < deadline && player != null && player.IsTeleporting()) {
                yield return null;
            }

            GameObject instance = null;
            while (Time.time < deadline && player != null) {
                instance = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(id) : null;
                if (instance != null) {
                    break;
                }
                yield return null;
            }

            if (player == null || instance == null) {
                Logger.LogDebug($"{what} did not turn up at the destination in time, leaving it where it landed.");
                yield break;
            }
            yield return AdjustablePortals.Instance.StartCoroutine(arrived(player, instance));
        }

        /// <summary>
        /// Puts the cart back within reach, and hitches it up again if configured to.
        /// </summary>
        private static IEnumerator SettleCart(Player player, GameObject instance) {
            Vagon cart = instance.GetComponent<Vagon>();
            if (cart == null || cart.m_nview == null || cart.m_nview.IsValid() == false) {
                yield break;
            }

            // Land the cart's handle exactly where the joint wants it. The offset from the cart's
            // root to its handle is fixed, so this works whichever way round the cart ended up.
            Vector3 handleOffset = cart.m_attachPoint.position - cart.transform.position;
            MoveCart(cart, player.transform.position + cart.m_attachOffset - handleOffset);

            if (ValConfig.ReattachCartAfterTeleport.Value == false) {
                yield break;
            }
            // One frame for the move above to land before the joint reads the positions it is
            // built from. Vagon.Update drops the joint again on its next tick if this was wrong,
            // so a bad attach costs the player one interact and nothing else.
            yield return null;
            if (player != null && cart != null && player.IsTeleporting() == false) {
                cart.AttachTo(player.gameObject);
            }
        }

        /// <summary>
        /// Puts the player back in the saddle.
        /// </summary>
        /// <remarks>
        /// Goes through Sadle.Interact - the same control request the player's own interact
        /// makes - rather than attaching directly, because only the mount's owner may grant
        /// control, and after an unload and respawn there is no telling which peer that is.
        /// </remarks>
        private static IEnumerator Remount(Player player, GameObject instance) {
            Sadle saddle = instance.GetComponentInChildren<Sadle>();
            if (saddle == null) {
                yield break;
            }
            // The player found something else to do while the destination loaded - sat down, took
            // a ship's helm, died. Climbing back on would undo that.
            if (player.IsDead() || player.IsAttached() || player.GetDoodadController() != null) {
                yield break;
            }
            if (saddle.InUseDistance(player) == false) {
                Logger.LogDebug("Mount landed out of reach of the player, leaving it unridden.");
                yield break;
            }
            saddle.Interact(player, false, false);
        }

        [HarmonyPatch(typeof(TeleportWorld))]
        internal static class PortalTripSource {

            [HarmonyPatch(nameof(TeleportWorld.Teleport))]
            [HarmonyPrefix]
            private static bool OnPortalTeleport(TeleportWorld __instance, Player player) {
                // Note which portal this is for the Player.TeleportTo patch below. Teleport calls
                // TeleportTo from its own body, so the frame stamp pairs them without leaving a
                // reference behind that could outlive a teleport that never happened.
                vanillaSourcePortal = __instance;
                vanillaSourcePortalFrame = Time.frameCount;

                if (ValConfig.EnableTeleportCarts.Value == false || player == null || player != Player.m_localPlayer) {
                    return true;
                }
                Vagon cart = GetAttachedCart(player);
                if (cart == null) {
                    return true;
                }
                // Vanilla says nothing at an unconnected or blocked portal, and neither should a
                // cart complaint - walking past one should not start explaining itself.
                if (__instance.TargetFound() == false) {
                    return true;
                }
                if (IsCartTeleportable(cart, __instance.m_allowAllItems, out string blockedBy)) {
                    return true;
                }

                // IsTeleportable is patched below to account for the cart, so vanilla would refuse
                // this anyway - but its "$msg_noteleport" sends the player hunting through their
                // own inventory for something that is not in it.
                player.Message(MessageHud.MessageType.Center, Localization.instance.Localize($"$msg_noteleport ({blockedBy})"));
                Logger.LogDebug($"Teleport blocked, cart contains: {blockedBy}");
                return false;
            }
        }

        [HarmonyPatch(typeof(Humanoid))]
        internal static class LoadedCartBlocksTeleport {

            // Runs after TeleportItems' postfix, which promotes a false result to true once the
            // player's own items clear progression. The ordering only matters in one direction:
            // this may never promote a result, and its refusal has to be the last word.
            [HarmonyPatch(nameof(Humanoid.IsTeleportable))]
            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Humanoid __instance, bool allowAllItems, ref bool __result) {
                if (__result == false || ValConfig.EnableTeleportCarts.Value == false) {
                    return;
                }
                // Only the local player's cart is knowable here, and it is the only one that
                // matters: every teleport is driven by the teleporting player's own client.
                if (__instance == null || __instance != Player.m_localPlayer) {
                    return;
                }
                Vagon cart = GetAttachedCart(Player.m_localPlayer);
                if (cart == null) {
                    return;
                }
                if (IsCartTeleportable(cart, allowAllItems, out string _) == false) {
                    __result = false;
                }
            }
        }

        [HarmonyPatch(typeof(Player))]
        internal static class CarryCompanionsThroughPortal {

            // Captured in the prefix rather than the postfix because the postfix is already too
            // late: TeleportTo sets m_teleporting, and the cart is on its way to detaching.
            [HarmonyPatch(nameof(Player.TeleportTo))]
            [HarmonyPrefix]
            private static void Prefix(Player __instance, Vector3 pos, out PortalTrip __state) {
                __state = default(PortalTrip);
                if (__instance == null || __instance != Player.m_localPlayer) {
                    return;
                }
                if (ValConfig.EnableTeleportTames.Value == false && ValConfig.EnableTeleportCarts.Value == false && ValConfig.EnableTeleportMounts.Value == false) {
                    return;
                }
                TeleportWorld portal = ResolveSourcePortal();
                if (portal == null) {
                    return;
                }
                __state.FromPortal = true;
                __state.Center = portal.transform.position;
                __state.Destination = pos;
                if (ValConfig.EnableTeleportMounts.Value) {
                    __state.Mount = GetRiddenMount(__instance);
                }
                if (ValConfig.EnableTeleportCarts.Value) {
                    Vagon cart = GetAttachedCart(__instance);
                    if (cart != null) {
                        if (IsCartTeleportable(cart, portal.m_allowAllItems, out string blockedBy)) {
                            __state.Cart = cart;
                        } else {
                            // The vanilla path refuses the whole teleport before it ever reaches
                            // here. TargetPortal goes straight from its map to TeleportTo, so this
                            // is the only place that sees the cart on that path - leave it and its
                            // contents behind rather than letting the progression allowances be
                            // walked around by opening a different interface.
                            __state.CartBlockedBy = blockedBy;
                        }
                    }
                }
            }

            [HarmonyPatch(nameof(Player.TeleportTo))]
            [HarmonyPostfix]
            private static void Postfix(Player __instance, bool __result, PortalTrip __state) {
                // A false result is a teleport that never started - already teleporting, still on
                // cooldown, or handed off to the owning client.
                if (__state.FromPortal == false || __result == false) {
                    return;
                }
                // Deferred to here rather than said in the prefix, where the teleport had not yet
                // survived its own cooldown and owner checks.
                if (string.IsNullOrEmpty(__state.CartBlockedBy) == false) {
                    __instance.Message(MessageHud.MessageType.Center, $"Cart left behind ({__state.CartBlockedBy})");
                }
                CarryCompanions(__instance, __state);
            }

            private static TeleportWorld ResolveSourcePortal() {
                if (vanillaSourcePortal != null && vanillaSourcePortalFrame == Time.frameCount) {
                    return vanillaSourcePortal;
                }
                // TargetPortal teleports from the map instead of from the portal's own trigger, so
                // nothing called TeleportWorld.Teleport and the portal is only known to the
                // compatibility layer that saw the map open.
                return Compatibility.GetActiveSourcePortal();
            }
        }
    }
}
