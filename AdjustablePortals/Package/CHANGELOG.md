**0.6.0**
---
```
- Server admins can now name items that may never be taken through a portal.
- Fixes item lists in the config not matching items when written with spaces or different capitalisation.
- Fixes backpacks from the Backpacks mod all being allowed or refused together, and they now follow boss progression for what they hold.
- Logs when a joining player receives the server's settings
```

**0.5.0**
---
```
- Boss item unlocks can now be earned per player instead of world wide, as an option.
- With per player unlocks, everyone who fought a boss is credited with the kill, not just one player.
```

**0.4.0**
---
```
- Riding a mount into a portal now takes the mount with you, and puts you back in the saddle on
  arrival. Controlled by EnableTeleportMounts.
- Tamed creatures standing near the portal now travel with you. On by default for tames you have
  told to follow; TeleportTamesRequireFollowing can be turned off to take every tame in range
  instead. Radius (5m) and headcount (5, nearest first) are configurable, and a tame somebody is
  riding is left where it is.
- Carts now travel with you, and are hitched back up on the far side. Only a cart you are actually
  pulling comes along, and only if everything in it is something you could carry through yourself -
  a cart holding ore the boss progression has not unlocked yet blocks the teleport and says which
  item is stopping it, rather than leaving you searching your own inventory.
```

**0.3.3**
---
```
- Deep North update!
```

**0.3.2**
---
```
- Nearby piece counts no longer scan every loaded building piece in the world once per portal. A
  base with many portals was paying that scan for each of them, and the scan ignored the configured
  distance - it always walked the whole list. Counts now come from a shared position snapshot, and
  the cost no longer grows with the number of portals.
- Portals no longer all recount on the same frame. Every portal in a zone was seeded together and
  stayed in lockstep for the rest of the session, turning the recount into a periodic stall.
- Nearby piece counts for portals that have unloaded are now released instead of being held until
  you leave the world.
- Portal activation no longer detours through this mod at all when both nearby piece and fuel
  requirements are disabled.
```

**0.3.1**
---
```
- BREAKING: nearby structures are now counted as building pieces instead of physics colliders,
  which is a much smaller number. Default lowered from 1000 to 300 - existing worlds keep their
  configured value and will need to retune PortalNearbyPiecesForActivation.
- Fixed the fuel requirement not blocking teleports when nearby piece requirements were disabled
- Fixed fuel being consumed on teleports that were blocked or otherwise did not happen
- Fixed allowed-after-boss items intermittently showing and behaving as non-teleportable
- Fixed boss progression not unlocking items for other players on a dedicated server until relog
- Fixed live config reload never firing (the file watcher was watching the wrong filename)
- Fixed portals sharing a nearby-piece count between different players' portals
- Fixed the nearby-piece count only refreshing for one portal at a time, leaving new portals dead
- Fixed portal fuel being lost when added or spent by a player who does not own the portal
- Fixed fuel payment being rejected when the cost was split across more than one stack
```

**0.3.0**
---
```
- Caching persistence for piece counts
- Live updating of teleportable item display status (inventory icon will show as teleportable if allowed)
- Improves compatibility with TargetPortal
- Adds support for TargetPortals teleportation system (require fuel/pieces etc)
```

**0.2.0**
---
```
- Caching improvements for portal requirements
- Adds portal fuel options (configurable)
- Makes nearby piece requirements configurably optional
```

**0.1.0**
---
```
- Initial release
```