# Adjustable Portals

This mod aims to make portal progression more gradual, and encourage portals only in large bases. 


## Features

**Portal Requirements**
- Portals can require a configurable number of building pieces nearby to become functional (default 300, configurable)
- Portals can require a fuel to operate on (configurable), default is surtling codes 1 core = 20 teleports (cost only taken from source portal).

**Portal Item Teleportation progression**
- Portals can allow designated non-teleportable items through based on defeated bosses.
- Progression can be per player instead of world wide. Turn on `UsePrivateKeys` and each player
  unlocks items by the bosses they helped defeat, rather than by any boss defeated on the server.
  Everyone who landed a hit on a boss is credited with it. Characters that killed bosses before
  this was turned on may have no record of it, since vanilla only credits one player per kill.
- Specific items can be made never teleportable with `NonTeleportableItems`. This holds even at
  portals and under world settings that would otherwise let every item through.
- Item lists are comma separated prefab names, e.g. `Copper, CopperScrap`. Spaces and
  capitalisation do not matter.

**Travelling companions**
- Ride your mount straight through a portal. It comes with you and you are put back in the saddle
  on the other side.
- Tamed creatures near the portal can travel with you. By default only tames you have told to
  follow you come along - turn `TeleportTamesRequireFollowing` off to take every tame standing
  near the portal, penned livestock included. Radius and headcount are configurable.
- A cart you are pulling can travel with you, and is hitched back up on the other side. The cart
  may only hold what you could carry through yourself: anything the boss progression above has not
  unlocked yet blocks the teleport, and the portal tells you which item is the problem.

**Compatibility**
- TargetPortal (Smoothbrain): portal requirements and fuel apply before its map opens.
- Backpacks (Smoothbrain): what a backpack may carry through a portal follows the same boss
  progression as your own inventory.


## Reporting issues or feedback
Got a bug to report or just want to chat about the mod? Drop by the discord or github.

[![discord logo](https://i.imgur.com/uE6umQE.png)](https://discord.gg/Dmr9PQTy9m)
[![github logo](https://i.imgur.com/lvbP5OF.png)](https://github.com/MidnightsFX/Valheim_Adjustable_Portals)

## In development!
If you have feature suggestions, or improvement suggestions feel free to ask in Discord.

The following features are currently planned:
- Configure portal building costs
- Alternate interface options for teleporting