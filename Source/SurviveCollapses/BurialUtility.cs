using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SurviveCollapses;

public static class BurialUtility
{
    public static bool IsPlayerRelevant(Pawn pawn)
    {
        if (pawn == null)
        {
            return false;
        }
        Faction player = Faction.OfPlayerSilentFail;
        if (player == null)
        {
            return false;
        }
        return pawn.Faction == player || pawn.HostFaction == player || pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony;
    }

    public static CompBuriedContents FindBurialHolder(IntVec3 cell, Map map)
    {
        if (map == null || !cell.InBounds(map))
        {
            return null;
        }
        List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
        for (int i = 0; i < things.Count; i++)
        {
            CompBuriedContents comp = things[i].TryGetComp<CompBuriedContents>();
            if (comp != null)
            {
                return comp;
            }
        }
        return null;
    }

    // Called once vanilla has finished crushing a cell and dropped rubble on it.
    internal static void BuryExtracted(CollapseFrame frame, List<Thing> outCrushedThings)
    {
        if (outCrushedThings != null)
        {
            // The vanilla "things were crushed" letter shouldn't list pawns we buried; we send our own.
            outCrushedThings.RemoveAll(t => frame.extracted.Contains(t));
        }

        CollapseTier tier = RubbleRegistry.TierFor(frame.roof);
        CompBuriedContents holder = FindBurialHolder(frame.cell, frame.map);
        if (holder == null)
        {
            // No rubble ended up on the cell (the roof didn't actually come down, or something replaced
            // the rubble). Put everyone back; if the cell became impassable anyway, they still get hurt.
            bool hit = frame.cell.InBounds(frame.map) && frame.cell.Impassable(frame.map);
            foreach (Thing t in frame.extracted)
            {
                PlaceNear(t, frame.cell, frame.map);
                if (hit && t is Pawn pawn && pawn.Spawned)
                {
                    CollapseOutcomeUtility.ResolveWithoutRubble(pawn, tier);
                }
            }
            return;
        }

        CollapseContext.ExtractStragglers(frame);
        foreach (Thing t in frame.extracted)
        {
            holder.Bury(t, tier);
        }
    }

    // Last resort if burying threw: never leave pawns floating outside the world.
    internal static void EmergencyRelease(CollapseFrame frame)
    {
        foreach (Thing t in frame.extracted)
        {
            if (t.Destroyed || t.Spawned || t.holdingOwner != null)
            {
                continue;
            }
            PlaceNear(t, frame.cell, frame.map);
        }
    }

    public static bool PlaceNear(Thing t, IntVec3 cell, Map map)
    {
        if (t.Destroyed || t.Spawned)
        {
            return t.Spawned;
        }
        if (map == null)
        {
            if (t is Pawn worldPawn && !worldPawn.Dead && !Find.WorldPawns.Contains(worldPawn))
            {
                Find.WorldPawns.PassToWorld(worldPawn);
            }
            return false;
        }
        if (t is Pawn pawn)
        {
            IntVec3 spot = cell;
            if (!spot.InBounds(map) || !spot.Standable(map))
            {
                if (!CellFinder.TryFindRandomCellNear(cell, map, 6, c => c.Standable(map) && !c.Fogged(map), out spot)
                    && !CellFinder.TryFindRandomCellNear(cell, map, 30, c => c.Standable(map), out spot))
                {
                    spot = CellFinder.RandomClosewalkCellNear(cell, map, 10);
                }
            }
            GenSpawn.Spawn(pawn, spot, map);
            return true;
        }
        return GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Near);
    }
}
