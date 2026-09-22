# ValheimTweaks

Video and network tweaks the Valheim menu does not offer.

Everything is optional and ships off or at the game's default, except anisotropic
filtering and anti-aliasing, which are practically free.

> In-game text follows the language you picked in the game menu (English or
> Portuguese). This is the main document; the Portuguese version is
> [README.pt-BR.md](README.pt-BR.md).

---

## Requirements

- BepInEx (r2modman installs it for you)
- Configuration Manager — it is what opens the options menu with **F1**

## Main options

**Anisotropic filtering** — the ground, roads and floors stop looking blurry when
seen at an angle. This is the tweak with the best payoff and almost no cost.

**Adjustable fog** — Valheim draws a lot of fog, and it washes out distant colors.
Lowering it to 0.4 opens up the horizon without removing the mood. No performance
cost.

**Quality anti-aliasing** — the game only turns it on and off. Here you can pick
between five levels of FXAA or TAA, which cleans up foliage and fences far better.

**Contact shadows (SSAO)** — at full resolution and with higher quality than the
menu allows. This is what adds depth and removes the "pasted on the ground" look.

**Exclusive fullscreen** — the game menu only toggles between windowed and
borderless. Exclusive usually makes motion smoother, especially on a
high-refresh monitor.

**Network timeout** — the game gives up on a stalled connection after 30 seconds,
which drops people with unstable internet. Here you can raise it.

**Simulation distance** — lets you go past the menu's limit, to see enemies and
animals from farther away. It weighs on whoever is hosting.

**Fast portal** — the crossing is slow because the game waits for the destination
area to finish loading, and it competes for the queue with over a hundred
surrounding zones. The mod shrinks that range only during the trip and restores it
on arrival. Measured: from 15 seconds to under 2. It only applies to you and
changes nothing for others.

**Field of view** — fixed at 65 in the game, adjustable here.

**Auto-mining** — one key toggles it; with the pickaxe in hand and ore under the
crosshair, the character swings by itself until the target breaks and stops. Ore
only, not stone: the mod looks at what the target drops.

**Store in chests (keys)** — mark items in your inventory and one key dumps
everything into nearby chests, fitting into stacks that already exist. A list in
the corner shows what was stored and where. It ships with **no keys assigned**:
the panel's Ctrl+click covers the same case. If you prefer the keys, pick them in
the F1 menu (`StoreMarkKey`, `StoreMarkedKey`, `StoreHoveredKey`).

**Chest panel** — open your inventory and click **Nearby chests** (or bind a key
to `ChestSearchKey`). In place of the crafting panel you get everything in the
surrounding chests, with a name search that ignores accents. A bar at the top
shows how many slots are still free across all chests.

Clicking an item takes one stack, just like taking from an open chest.
**Shift+click** opens the game's own split dialog to choose the amount, and
**Ctrl+click** takes everything. The footer also has shortcuts for 1, 10, one
stack or all, with `-/+` and a field to type the exact number.

To store, **Ctrl+click** an item in your inventory sends it straight to the
chests. (With the panel closed, that same shortcut drops the item on the ground —
the game's behavior.) Or pick the item up as you normally would and click the
panel. Before confirming, it shows where each part goes and why: first topping off
stacks that already exist, then chests that already hold that item, and only then
free space. If something does not fit, it tells you how much.

While the panel is open the character cannot walk, so typing in the search does
not make him move. Close it with the button or with Esc. If you would rather walk
with it open, turn off `ChestSearchBlockMove`.

By default it also stores into chests the other player's client currently owns
(`StoreNonOwnedChests`). Those are handed over by the game's own ownership
handshake before anything is written, and the move measures both ends, so nothing
is lost. Your own chests are always used first; a chest owned by the other player
is only touched when needed. When you host, the chest is claimed directly (safe,
instant) instead of waiting on their client, and if they have it open the request
is retried for a few seconds instead of failing until you open it by hand. A chest
whose owner is offline is skipped instead of stalling. Turn the option off to only
ever touch chests you own.

A moment after storing, it checks that the chest really kept the items; if they
vanished from it, they go back to your backpack (`StoreRollback`).

**Peek chest** — aiming at a nearby chest shows its contents on the right side
without opening it.

**Auto-repair** — repairs all worn equipment when you open your inventory near a
workbench or forge, instead of one item per click. Repair in Valheim costs no
material, so this only saves clicks.

**Build from nearby chests** — while building with the hammer, the wood and stone
in chests, carts and ships around you count as if they were in your backpack. The
build menu marks a piece as buildable when the nearby storage covers it, and the
materials are taken from there on placement. Building only: crafting at a station
still uses your backpack. Useful to keep a cart of wood by the build site instead
of a stack in every slot.

Beyond those, there is control over shadows, grass density and range, torch light
limits, terrain tessellation and a few performance tweaks.

## In multiplayer

Most options are visual and apply only to whoever set them — your tweak changes
nothing on other people's screens.

Two exceptions:

- **Network timeout**: each player closes their own connection, so everyone needs
  the mod and the same value. A single person without the mod is enough to drop
  it.
- **Simulation distance**: whoever hosts sets the ceiling. Others can use less,
  never more.

## Tools

There is a diagnostics section that writes a summary of the active video settings
to the log and measures performance for a few seconds, with average, percentiles
and a stutter count. Useful to compare tweaks with numbers instead of impressions.

It comes with an option to freeze the time of day and the weather on your screen,
without affecting the world or other players, so you can compare two tweaks in the
same scene.

There is also a **chest audit** (`ChestAuditLog`): it writes to the log every chest
interaction on the network — who opened, stacked or took from which chest, whether
the owner allowed it, the chest id and its owner — and, with `ChestAuditContents`,
the item list before and after each change. With `ChestAuditContents` it also logs,
timestamped, what entered or left your own backpack every half second, so an item
that disappears without reaching a chest is visible too. It exists to answer "where
did my item go" with facts. Turn it off on a busy base to keep the log small.

## About performance

This mod does not try to optimize the game engine. For that, install
**ValheimPerformanceOptimizations**, which does that work in depth — object
streaming, terrain collision, light and sound culling, structural integrity —
without changing the game's behavior. The two coexist: one handles the visuals and
the network, the other the engine.

The few performance tweaks here all ship at the game's default and exist for
specific cases (smoke in a base with many campfires, network interval on a full
server).

## Compatibility

Some mods do the same job as this one. **SmartCraftStorage** and
**AzuCraftyBoxes** pull materials from nearby chests for building and crafting.
Running one of them together with this mod's build-from-chests would count and
remove the same materials twice (you could craft for less, and items could be
eaten). When one of them is detected, that feature here turns itself off and says
so in the log. Everything else in the mod is unaffected.

At boot the log also lists every method of this mod that another mod patches too
("Methods shared with other mods"). Shared methods are where conflicts live; the
game never errors on them, so having the list makes a mystery into a name.

## Changing the options

Through **F1** in game, or by editing directly:

```
BepInEx/config/com.kyoka.valheimtweaks.cfg
```

Changes take effect immediately, no restart needed.
