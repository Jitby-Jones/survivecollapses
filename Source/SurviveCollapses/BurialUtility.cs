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
        CrushLeftovers(frame);
        foreach (Thing t in frame.extracted)
        {
            holder.Bury(t, tier);
        }
    }

    // Vanilla obliterates whatever is in a cell that fills with rubble. Anything still there that we
    // didn't take (pawns or bodies the settings leave to vanilla, dropped after vanilla's own pass) gets
    // the same treatment instead of being left standing inside the rock.
    private static void CrushLeftovers(CollapseFrame frame)
    {
        List<Thing> snapshot = new List<Thing>(frame.map.thingGrid.ThingsListAtFast(frame.cell));
        foreach (Thing t in snapshot)
        {
            if (!t.Spawned)
            {
                continue;
            }
            if (t is Pawn pawn && !pawn.Dead)
            {
                pawn.Kill(new DamageInfo(DamageDefOf.Crush, 99999f, 999f, -1f, null, null, null, DamageInfo.SourceCategory.Collapse));
                Corpse body = pawn.Corpse;
                if (body != null && body.Spawned && body.Position == frame.cell)
                {
                    body.Destroy();
                }
            }
            else if (t is Corpse corpse)
            {
                corpse.Destroy();
            }
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
        if (!cell.InBounds(map))
        {
            cell = map.Center;
        }
        if (t is Pawn)
        {
            GenSpawn.Spawn(t, StandableSpotNear(cell, map), map);
            return true;
        }
        if (GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Near))
        {
            return true;
        }
        // Never lose a body to a cramped tunnel: put it down outright.
        GenSpawn.Spawn(t, StandableSpotNear(cell, map), map);
        return t.Spawned;
    }

    private static IntVec3 StandableSpotNear(IntVec3 cell, Map map)
    {
        if (cell.Standable(map))
        {
            return cell;
        }
        if (CellFinder.TryFindRandomCellNear(cell, map, 6, c => c.Standable(map) && !c.Fogged(map), out IntVec3 spot)
            || CellFinder.TryFindRandomCellNear(cell, map, 30, c => c.Standable(map), out spot))
        {
            return spot;
        }
        return CellFinder.RandomClosewalkCellNear(cell, map, 10);
    }
}
