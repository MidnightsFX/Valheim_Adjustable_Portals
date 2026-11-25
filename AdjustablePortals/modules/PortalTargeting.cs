using AdjustablePortals.ui;
using HarmonyLib;
using Splatform;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace AdjustablePortals.modules {

    /// <summary>
    /// Lets the player choose which portal they come out of, instead of the one that shares the
    /// source portal's name. Stepping into a portal asks the server for every portal in the world
    /// and opens a picker - a list, the map, or a starfield - and picking one travels there.
    /// </summary>
    /// <remarks>
    /// The teleport itself does not go through TeleportWorld.Teleport, which only knows how to
    /// reach the tag partner. It calls Player.TeleportTo directly, so this module stands in for
    /// the things Teleport would otherwise have done: vanilla's own refusals, the cart check,
    /// telling TeleportCompanions which portal the trip started from, and charging fuel.
    /// </remarks>
    internal static class PortalTargeting {

        internal enum TargetingMode {
            Vanilla,
            SelectFromList,
            SelectOnMap,
            SelectInSpace,
        }

        /// <summary>
        /// A portal the player may travel to, as the server described it.
        /// </summary>
        internal class Destination {
            public ZDOID Id;
            public string Name;
            public Vector3 Position;
            public Quaternion Rotation;
            public int PrefabHash;
            // Measured from the source portal along the ground, for telling apart portals that
            // share a name - and every vanilla pair does.
            public float Distance;
            public string Direction;

            public string DistanceText {
                get { return $"{FormatDistance(Distance)} {Direction}"; }
            }
        }

        private enum SessionState {
            None,
            // Waiting on the server's portal list.
            Pending,
            // A picker is showing.
            Open,
        }

        private const string RequestPortalsRPC = "AJP_RequestPortals";
        private const string PortalListRPC = "AJP_PortalList";

        // Player.TeleportTo refuses anything for this long after the last teleport ends, and the
        // player usually arrives standing in the destination portal's trigger. Vanilla's trigger
        // does nothing in that window, so the picker should not pop up over a fresh arrival either.
        internal const float TeleportCooldownSeconds = 2f;
        // A list the server never answered is not worth leaving the portal stuck on.
        private const float RequestTimeoutSeconds = 5f;
        private const string UnnamedPortal = "Unnamed portal";

        private static readonly string[] CompassPoints = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        private static SessionState state = SessionState.None;
        private static TargetingMode openMode = TargetingMode.Vanilla;
        private static TeleportWorld sourcePortal = null;
        private static Player sessionPlayer = null;
        // Only the answer to the latest request is wanted. One that arrives after the player has
        // walked off, or after a newer request, is dropped by its id.
        private static int requestId = 0;
        private static float requestTime = 0f;

        // Reused by the server for each request; only ever touched on the main thread.
        private static readonly List<ZDO> eligibleBuffer = new List<ZDO>();

        /// <summary>
        /// Whether portals are being picked rather than paired by name.
        /// </summary>
        internal static bool IsActive {
            get { return ValConfig.PortalTargetingMode.Value != TargetingMode.Vanilla; }
        }

        internal static void TargetingModeChanged(object sender, EventArgs e) {
            EndSession();
        }

        [HarmonyPatch(typeof(Game), nameof(Game.Start))]
        private static class RegisterRPCs {
            // Both on every peer: a host is the server answering and the client asking at once.
            private static void Postfix() {
                ZRoutedRpc.instance.Register<int>(RequestPortalsRPC, RPC_RequestPortals);
                ZRoutedRpc.instance.Register<int, ZPackage>(PortalListRPC, RPC_PortalList);
            }
        }

        [HarmonyPatch(typeof(TeleportWorldTrigger), nameof(TeleportWorldTrigger.OnTriggerEnter))]
        private static class ChooseDestinationOnEnter {
            private static bool Prefix(TeleportWorldTrigger __instance, Collider colliderIn) {
                if (IsActive == false) {
                    return true;
                }
                Player player = colliderIn != null ? colliderIn.GetComponent<Player>() : null;
                // Vanilla ignores everyone but the local player as well.
                if (player == null || player != Player.m_localPlayer) {
                    return true;
                }
                // Never hand this on to vanilla, whatever BeginSelection decides. Game.ConnectPortals
                // goes on pairing portals by name in the background, and TargetFound below says
                // every portal has a target - so the original would quietly send the player to the
                // name partner they did not pick.
                BeginSelection(__instance.m_teleportWorld, player);
                return false;
            }
        }

        // Every portal leads somewhere once the player is the one choosing, so the "target found"
        // swirl shows whenever a player who could travel stands nearby. Activation requirements
        // still have their say: ActivationRequirements' TargetFound postfix runs after this and
        // clears it for a portal that is not ready.
        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.TargetFound))]
        private static class EveryPortalHasATarget {
            private static bool Prefix(TeleportWorld __instance, ref bool __result) {
                if (IsActive == false || __instance.m_nview == null || __instance.m_nview.IsValid() == false) {
                    return true;
                }
                __result = true;
                return false;
            }
        }

        private static void BeginSelection(TeleportWorld source, Player player) {
            if (state != SessionState.None) {
                return;
            }
            if (source == null || source.m_nview == null || source.m_nview.IsValid() == false) {
                return;
            }
            if (player.IsTeleporting() || player.m_teleportCooldown < TeleportCooldownSeconds) {
                return;
            }
            if (CanTravelFrom(source, player, out string reason) == false) {
                player.Message(MessageHud.MessageType.Center, reason);
                return;
            }

            // All of this is set before the request goes out. On a host the request is answered
            // on the spot, inside this very call, and the answer checks against it.
            state = SessionState.Pending;
            sourcePortal = source;
            sessionPlayer = player;
            requestTime = Time.time;
            requestId++;
            ZRoutedRpc.instance.InvokeRoutedRPC(RequestPortalsRPC, requestId);
        }

        /// <summary>
        /// Everything vanilla's TeleportWorld.Teleport would refuse a trip for, plus this mod's own
        /// requirements on the portal being left. Checked when the picker opens and again when a
        /// destination is picked, since the player can pick up something they may not take through
        /// in between.
        /// </summary>
        internal static bool CanTravelFrom(TeleportWorld source, Player player, out string reason) {
            if (ActivationRequirements.PortalInstanceActivatable.AreActivationRequirementsMet(source, out reason) == false) {
                return false;
            }
            ZoneSystem zones = ZoneSystem.instance;
            if (zones.GetGlobalKey(GlobalKeys.NoPortals)) {
                reason = "$msg_blocked";
                return false;
            }
            if (BossFightPortals.BlocksTravel()) {
                reason = "$msg_blockedbyboss";
                return false;
            }
            // Ahead of IsTeleportable, which also says no for the cart but cannot say why.
            if (TeleportCompanions.CartBlocksPortal(player, source, out string blockedBy)) {
                reason = $"$msg_noteleport ({blockedBy})";
                return false;
            }
            if (player.IsTeleportable(source.m_allowAllItems) == false) {
                reason = "$msg_noteleport";
                return false;
            }
            reason = "";
            return true;
        }

        /// <summary>
        /// Answers a client with every portal it may travel to.
        /// </summary>
        /// <remarks>
        /// Asked of the server because a client only holds the portals that have been near it this
        /// session, plus the ones vanilla happened to push out while pairing names. The server has
        /// every portal ZDO from the moment the world loads.
        /// </remarks>
        private static void RPC_RequestPortals(long sender, int id) {
            try {
                if (ZNet.instance == null || ZNet.instance.IsServer() == false) {
                    return;
                }
                eligibleBuffer.Clear();
                foreach (ZDO portal in ZDOMan.instance.GetPortalList()) {
                    if (ActivationRequirements.PortalInstanceActivatable.StoredPiecesMeetRequirement(portal)) {
                        eligibleBuffer.Add(portal);
                    }
                }

                ZPackage package = new ZPackage();
                package.Write(eligibleBuffer.Count);
                foreach (ZDO portal in eligibleBuffer) {
                    package.Write(portal.m_uid);
                    package.Write(portal.GetPosition());
                    package.Write(portal.GetRotation());
                    package.Write(portal.GetPrefab());
                    // Raw, along with who wrote it. The name is filtered on the client that shows
                    // it, the same as vanilla filters the portal's own hover text.
                    package.Write(portal.GetString(ZDOVars.s_tag));
                    package.Write(portal.GetString(ZDOVars.s_tagauthor));
                }
                eligibleBuffer.Clear();
                Logger.LogDebug($"Sending {package.Size()} bytes of portal destinations to {sender}.");
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, PortalListRPC, id, package);
            } catch (Exception ex) {
                // A host runs this inside its own portal trigger, where an exception would
                // otherwise surface out of OnTriggerEnter.
                eligibleBuffer.Clear();
                Logger.LogWarning($"Unable to list portal destinations: {ex}");
            }
        }

        private static void RPC_PortalList(long sender, int id, ZPackage package) {
            try {
                if (state != SessionState.Pending || id != requestId) {
                    return;
                }
                // The answer can take a moment to come back from a dedicated server, and the
                // player may have walked off or opened something else in the meantime.
                if (SessionStillValid() == false || Menu.IsVisible() || Console.IsVisible() || TextInput.IsVisible()) {
                    EndSession();
                    return;
                }

                List<Destination> destinations = ReadDestinations(package, sourcePortal);
                if (destinations.Count == 0) {
                    sessionPlayer.Message(MessageHud.MessageType.Center, "There are no other portals to travel to.");
                    EndSession();
                    return;
                }
                OpenPicker(destinations);
            } catch (Exception ex) {
                Logger.LogWarning($"Unable to show portal destinations: {ex}");
                EndSession();
            }
        }

        private static List<Destination> ReadDestinations(ZPackage package, TeleportWorld source) {
            ZDOID sourceId = source.m_nview.GetZDO().m_uid;
            Vector3 origin = source.transform.position;
            int count = package.ReadInt();
            List<Destination> destinations = new List<Destination>(count);
            for (int i = 0; i < count; i++) {
                Destination destination = new Destination();
                destination.Id = package.ReadZDOID();
                destination.Position = package.ReadVector3();
                destination.Rotation = package.ReadQuaternion();
                destination.PrefabHash = package.ReadInt();
                string tag = package.ReadString();
                string author = package.ReadString();
                if (destination.Id == sourceId) {
                    continue;
                }

                PlatformUserID authorId = string.IsNullOrEmpty(author) ? PlatformUserID.None : new PlatformUserID(author);
                string name = string.IsNullOrEmpty(tag) ? "" : CensorShittyWords.FilterUGC(tag, UGCType.Text, authorId, 0L);
                // Shown on map pins and buttons that would otherwise render someone's markup.
                name = string.IsNullOrEmpty(name) ? "" : name.RemoveRichTextTags().Trim();
                destination.Name = name.Length > 0 ? name : UnnamedPortal;

                Vector3 offset = destination.Position - origin;
                offset.y = 0f;
                destination.Distance = offset.magnitude;
                destination.Direction = CompassPoint(offset);
                destinations.Add(destination);
            }
            return destinations;
        }

        private static void OpenPicker(List<Destination> destinations) {
            TargetingMode mode = ValConfig.PortalTargetingMode.Value;
            // m_noMap covers the world modifier and the player's own nomap setting alike, and while
            // it is set the minimap refuses to open at all.
            if (mode == TargetingMode.SelectOnMap && (Game.m_noMap || Minimap.instance == null)) {
                mode = TargetingMode.SelectInSpace;
            }
            if (InventoryGui.IsVisible()) {
                InventoryGui.instance.Hide();
            }

            state = SessionState.Open;
            openMode = mode;
            bool opened;
            switch (mode) {
                case TargetingMode.SelectOnMap:
                    opened = PortalMapPicker.Open(sourcePortal, destinations);
                    break;
                case TargetingMode.SelectInSpace:
                    CloseLargeMap();
                    opened = PortalSpacePicker.Open(sourcePortal, destinations);
                    break;
                default:
                    CloseLargeMap();
                    opened = PortalListPicker.Open(sourcePortal, destinations);
                    break;
            }
            if (opened == false) {
                EndSession();
            }
        }

        // A panel opened over the large map would leave the map taking clicks underneath it.
        private static void CloseLargeMap() {
            if (Minimap.instance != null && Minimap.instance.m_mode == Minimap.MapMode.Large) {
                Minimap.instance.SetMapMode(Minimap.MapMode.Small);
            }
        }

        /// <summary>
        /// Sends the player to <paramref name="destination"/>. Called by whichever picker is open.
        /// </summary>
        internal static void TravelTo(Destination destination) {
            if (state != SessionState.Open || destination == null) {
                return;
            }
            if (SessionStillValid() == false) {
                EndSession();
                return;
            }
            TeleportWorld source = sourcePortal;
            Player player = sessionPlayer;
            if (CanTravelFrom(source, player, out string reason) == false) {
                player.Message(MessageHud.MessageType.Center, reason);
                EndSession();
                return;
            }

            // Closed first: the map picker ends on the map closing, and the teleport below should
            // not have the picker's teardown running in the middle of it.
            EndSession();
            ActivationRequirements.PortalFuel.SpendThen(source, traveller => Travel(source, traveller, destination));
        }

        private static bool Travel(TeleportWorld source, Player player, Destination destination) {
            // Vanilla's exit, from TeleportWorld.Teleport: a step out of the far portal's front,
            // facing away from it.
            Vector3 exit = destination.Position + destination.Rotation * Vector3.forward * source.m_exitDistance + Vector3.up;
            TeleportCompanions.NoteSourcePortal(source);
            if (player.TeleportTo(exit, destination.Rotation, distantTeleport: true) == false) {
                return false;
            }
            Logger.LogDebug($"Travelling to portal \"{destination.Name}\" {destination.DistanceText}.");
            Game.instance.IncrementPlayerStat(PlayerStatType.PortalsUsed);
            return true;
        }

        /// <summary>
        /// Closes the picker without travelling. Safe to call from a picker's own teardown.
        /// </summary>
        internal static void Cancel() {
            EndSession();
        }

        /// <summary>
        /// Ends a session that should not outlive what it was opened for. Called every frame from
        /// the plugin's Update.
        /// </summary>
        internal static void Update() {
            if (state == SessionState.None) {
                return;
            }
            if (SessionStillValid() == false) {
                EndSession();
                return;
            }
            if (state == SessionState.Pending && Time.time - requestTime > RequestTimeoutSeconds) {
                Logger.LogDebug("The server did not answer with portal destinations in time.");
                EndSession();
            }
        }

        private static bool SessionStillValid() {
            Player player = Player.m_localPlayer;
            if (player == null || player != sessionPlayer || player.IsDead() || player.IsTeleporting()) {
                return false;
            }
            if (sourcePortal == null || sourcePortal.m_nview == null || sourcePortal.m_nview.IsValid() == false) {
                return false;
            }
            if (IsActive == false) {
                return false;
            }
            // The map does not stop a keyboard player walking, so walking out of the portal is
            // how a player turns down the map picker.
            return Vector3.Distance(player.transform.position, sourcePortal.transform.position) <= sourcePortal.m_activationRange;
        }

        private static void EndSession() {
            if (state == SessionState.None) {
                return;
            }
            // Cleared before the pickers close, so a picker whose teardown cancels the session
            // finds it already over.
            SessionState previous = state;
            TargetingMode mode = openMode;
            state = SessionState.None;
            openMode = TargetingMode.Vanilla;
            sourcePortal = null;
            sessionPlayer = null;
            if (previous != SessionState.Open) {
                return;
            }
            switch (mode) {
                case TargetingMode.SelectOnMap:
                    PortalMapPicker.Close();
                    break;
                case TargetingMode.SelectInSpace:
                    PortalSpacePicker.Close();
                    break;
                default:
                    PortalListPicker.Close();
                    break;
            }
        }

        internal static void ClearSessionState() {
            EndSession();
            eligibleBuffer.Clear();
        }

        private static string CompassPoint(Vector3 offset) {
            if (offset.sqrMagnitude < 0.01f) {
                return "";
            }
            // North is +z on Valheim's map, and east +x.
            float bearing = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            int index = Mathf.RoundToInt(Mathf.Repeat(bearing, 360f) / 45f) % CompassPoints.Length;
            return CompassPoints[index];
        }

        internal static string FormatDistance(float metres) {
            if (metres < 1000f) {
                return $"{Mathf.RoundToInt(metres)} m";
            }
            return (metres / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km";
        }
    }
}
