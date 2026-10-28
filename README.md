# Survive Collapses

A RimWorld mod (1.6 and 1.7) that stops overhead mountain collapses from erasing whoever is standing underneath.

In vanilla, a collapse that fills the ground with collapsed rocks deals 99,999 crush damage and wipes the body: no corpse, no gear, nothing to bury. With this mod, pawns caught in such a collapse are **buried under the collapsed rocks** instead:

- **Most survive, badly hurt.** Survivors get crushed (destroyed) limbs, crushed organs, cracked bones and deep bruising. Their injuries are capped so the collapse itself never kills them.
- **Some die.** Default death chance is 45% under an overhead mountain. The body stays under the rubble: mangled (missing limbs, crushed organs) but recoverable.
- **Pinned and down.** Living pawns under rubble are forced down by the *pinned under rubble* hediff (Moving is capped at 0), so even pawns with 100% pain resistance can't get up. The rubble's weight slows their bleeding (35% of normal by default), but they still bleed out if left there.
- **Dig them out.** Mine the collapsed rocks to free them. Rubble over your colonists, prisoners, slaves and animals is designated for mining automatically, along with the shortest path through the rock to reach it. A letter says who was buried and where, a red alert tracks everyone still trapped alive, and the rubble's inspect pane shows who is underneath and how long they have before they bleed out.

Roof collapses that don't already kill instantly (constructed roofs, thin rock roofs) are left exactly as they are in vanilla.

## Armor and Combat Extended

- **With Combat Extended loaded,** armor protects against the collapse per body part, using CE's per-part (locational) armor values (`ArmorUtilityCE.PartialStat`) for worn and natural armor. As in CE's own crush handling, a hit to an organ also passes through the natural armor of the outer part around it. The falling rock is treated as a blunt hit whose penetration equals that part's immunity threshold, and every covering layer subtracts from it, the same way CE's blunt armor works. Damage to a part scales by `1 - protection / threshold`. The death chance scales down with the average protection over the head, neck and torso.
- **Immunity:** a body part whose blunt protection is at least what a full **masterwork cataphract** set (armor + helmet) gives that same part takes no damage. That means a pawn in full masterwork cataphract is immune wherever the set covers them, CE's per-part values included. With CE that's 90 MPa where the armor covers, 75 MPa under the helmet, and 72 MPa on the hands. In vanilla it's 72.5%. Parts the set doesn't cover, and animals or mechs, use the body armor's value. If Royalty isn't active, built-in masterwork cataphract values are used. A slider scales the thresholds.
- **Without Combat Extended,** armor does not reduce collapse damage. There is an opt-in setting to use vanilla blunt armor ratings the same way.
- Worn apparel covering a crushed body part takes a share of the damage either way. This can be turned off.

## Settings

Death chance (overhead mountain, plus any other roof that collapses into rubble, e.g. from mods), injury severity, bleeding while pinned, armor with or without CE, immunity threshold scale, apparel damage, whether raiders/visitors/wildlife can survive too, whether corpses already lying on the tile are buried instead of destroyed, automatic dig designation, letters, and the *buried alive* mood penalty (−12 for 6 days).

## Installation

Requires [Harmony](https://steamcommunity.com/workshop/filedetails/?id=2009463077). Copy this folder into `RimWorld/Mods/` and enable it after Harmony (and after Combat Extended, if used). It's safe to add to an existing save. **Removing it while pawns are buried will lose those pawns.**

## How it works

- A Harmony prefix and finalizer on `RoofCollapserImmediate.DropRoofInCellPhaseOne` take pawns and corpses off the cell before vanilla crushes it. Prefixes on `Thing.TakeDamage`, `Pawn.Kill` and `Corpse.Kill` catch anything that lands on the cell partway through the collapse, for example pawns ejected from a destroyed casket. These checks only do anything while a rubble-leaving collapse is being processed.
- At startup, every impassable thing a roof leaves when it collapses (vanilla: `CollapsedRocks`) gets `CompBuriedContents`, a `ThingComp` that holds the buried things and saves them with the rubble. Those defs are also marked non-save-compressible, so the contents survive save/load.
- Buried pawns are ticked by a map component, so wounds, bleeding and needs keep running. A pawn who dies under the rubble leaves the corpse there. Buried-alive colonists count as present on their map, so having everyone buried doesn't end the game.
- Mining or otherwise destroying the rubble releases everything onto the tile. The pinned hediff is then removed, and survivors get the *buried alive* memory.

## Building

The project builds both assemblies from one source tree:

```bash
cd Source/SurviveCollapses
dotnet build -c Release
```

- `net472` → `1.6/Assemblies/SurviveCollapses.dll` (RimWorld 1.6, Harmony 2.x)
- `net10.0` → `1.7/Assemblies/SurviveCollapses.dll` (RimWorld 1.7, Harmony 3.x)

You need the .NET 10 SDK. Reference assemblies come from the [`Krafs.Rimworld.Ref`](https://www.nuget.org/packages/Krafs.Rimworld.Ref) and `Lib.Harmony` NuGet packages, so no game install is required. `LoadFolders.xml` loads `Common/` (defs and translations) plus the folder for the running game version.
