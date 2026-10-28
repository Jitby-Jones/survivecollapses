using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SurviveCollapses;

// Marks the rubble over a buried pawn for mining, plus the shortest run of rock between it and open
// ground, so colonists can actually reach the victim.
public static class DigOutDesignator
{
    private const int MaxSearchCells = 1500;

    // Mining collapsed rocks is quick; cutting through solid rock is not.
    private const int RubbleCost = 1;
    private const int RockCost = 4;

    public static void DesignateDigOut(Thing rubble)
    {
        Map map = rubble?.Map;
        if (map == null)
        {
            return;
        }
        List<IntVec3> path = FindDigPath(rubble.Position, map);
        if (path == null)
        {
            TryDesignate(rubble.Position, map);
            return;
        }
        foreach (IntVec3 cell in path)
        {
            TryDesignate(cell, map);
        }
    }

    private static void TryDesignate(IntVec3 cell, Map map)
    {
        if (!IsMineable(cell, map) || map.designationManager.DesignationAt(cell, DesignationDefOf.Mine) != null)
        {
            return;
        }
        map.designationManager.AddDesignation(new Designation(cell, DesignationDefOf.Mine));
    }

    private static bool IsMineable(IntVec3 cell, Map map)
    {
        Building edifice = cell.GetEdifice(map);
        return edifice != null && edifice.def.mineable;
    }

    // Cheapest path (Dijkstra over 4-neighbours) from the buried cell to any cell a pawn can stand on,
    // passing only through mineable rock. Returns the mineable cells along it, buried cell included.
    private static List<IntVec3> FindDigPath(IntVec3 start, Map map)
    {
        Dictionary<IntVec3, int> cost = new Dictionary<IntVec3, int> { [start] = 0 };
        Dictionary<IntVec3, IntVec3> from = new Dictionary<IntVec3, IntVec3>();
        List<(IntVec3 cell, int cost)> open = new List<(IntVec3, int)> { (start, 0) };
        int explored = 0;

        while (open.Count > 0 && explored < MaxSearchCells)
        {
            int best = 0;
            for (int i = 1; i < open.Count; i++)
            {
                if (open[i].cost < open[best].cost)
                {
                    best = i;
                }
            }
            (IntVec3 cell, int cellCost) = open[best];
            open.RemoveAt(best);
            if (cellCost > cost[cell])
            {
                continue;
            }
            explored++;

            for (int i = 0; i < 4; i++)
            {
                IntVec3 next = cell + GenAdj.CardinalDirections[i];
                if (!next.InBounds(map) || cost.ContainsKey(next) && cost[next] <= cellCost)
                {
                    continue;
                }
                if (next.Standable(map) && !next.Fogged(map))
                {
                    // Reached open ground: walk back to the start collecting the cells to dig.
                    List<IntVec3> path = new List<IntVec3>();
                    for (IntVec3 cur = cell; ; cur = from[cur])
                    {
                        path.Add(cur);
                        if (cur == start)
                        {
                            break;
                        }
                    }
                    return path;
                }
                if (!IsMineable(next, map))
                {
                    continue;
                }
                Building edifice = next.GetEdifice(map);
                int step = edifice.TryGetComp<CompBuriedContents>() != null || !edifice.def.building.isNaturalRock ? RubbleCost : RockCost;
                int nextCost = cellCost + step;
                if (cost.TryGetValue(next, out int known) && known <= nextCost)
                {
                    continue;
                }
                cost[next] = nextCost;
                from[next] = cell;
                open.Add((next, nextCost));
            }
        }
        return null;
    }
}
