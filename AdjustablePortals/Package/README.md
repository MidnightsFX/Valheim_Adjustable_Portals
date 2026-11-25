# Adjustable Portals

This mod aims to make portal progression more gradual, and encourage portals only in large bases. 


## Features

**Portal Requirements**
- Portals can require a configurable number of building pieces nearby to become functional (default 300, configurable).
  The server counts the pieces around each portal, so every player sees the same count, whatever
  part of the base their own game has loaded.
- Portals can require a fuel to operate on (configurable), default is surtling codes 1 core = 20 teleports (cost only taken from source portal).
- In worlds with the world modifier that turns portals off during boss fights, only players near
  the boss being fought are kept from portals. Turn on `BossFightsElsewhereBlockPortals` to block
  every portal in the world while any boss is fighting, as the game does.

**Portal Item Teleportation progression**
- Portals can allow designated non-teleportable items through based on defeated bosses. Every
  boss up to the Deep North's final boss has its own list, and that last one only unlocks once the
  boss's final phase is defeated.
- Turn off `EnablePortalProgression` to keep the game's usual item restrictions instead. The
  never-teleportable list below still applies.
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

**Choosing a destination**
- Instead of only reaching the portal with the same name, players can choose where a portal sends
  them. `PortalTargetingMode` picks how:
  - `Vanilla` (default): portals connect to the portal with the same name.
  - `SelectFromList`: stepping into a portal opens a list of every other portal, which you can
    filter by typing. Each entry shows how far away it is and in which direction, which tells
    apart portals that share a name.
  - `SelectOnMap`: stepping into a portal opens the map with every other portal marked on it,
    each with an orange and red glow. Click one to travel there, or close the map to stay. When the map is turned off, this uses
    `SelectInSpace` instead.
  - `SelectInSpace`: shows every other portal where it lies relative to the others, against a
    starfield instead of the map, so it works in worlds without a map. Scroll to zoom, drag to look
    around, and click a portal to travel there.
- A portal is only offered as a destination once it has the nearby building pieces it needs to
  activate itself. The server counts every portal, including ones nobody has visited lately.
- Fuel, item restrictions, carts, mounts and tames all work the same as through a portal pair.
- Every qualifying portal can be travelled to, whatever it is named and whether or not it is
  inside a ward.

**Compatibility**
- TargetPortal (Smoothbrain) is not compatible. `PortalTargetingMode` covers what it does, and this
  mod does not load while TargetPortal is installed.
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

<!--
AI_Assisted_Creation: This assembly was partially or fully created with the assistance of generative AI (Claude Code, using Anthropic's Claude models).
AI_Model_Vendor: Anthropic
-->

---

If you enjoy my mods donations are appreciated. My mods will never be paid and donations are not required.

<a href="https://ko-fi.com/midnightsfx"><img height="36" src="https://storage.ko-fi.com/cdn/kofi2.png?v=3" alt="Support me on Ko-fi"></a>
