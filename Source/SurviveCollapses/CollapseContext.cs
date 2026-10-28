using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SurviveCollapses;

// One cell being processed by RoofCollapserImmediate.DropRoofInCellPhaseOne.
internal sealed class CollapseFrame
{
    public IntVec3 cell;
    public Map map;
    public RoofDef roof;
    public CollapseKind kind;

    // Taken off the map, to be buried once vanilla is done with the cell.
    public readonly List<Thing> extracted = new List<Thing>();

    public bool active => kind != CollapseKind.None;
}

// While vanilla crushes a cell that will be filled with rubble, pawns (and corpses) on that cell are
// pulled out of the world before vanilla can obliterate them, then buried in the rubble afterwards.
internal static class CollapseContext
{
    private static readonly List<CollapseFrame> frames = new List<CollapseFrame>();
    private static int activeFrames;

    public static bool AnyActive => activeFrames > 0;

    public static void Push(IntVec3 cell, Map map, RoofDef roof, CollapseKind kind)
    {
        CollapseFrame frame = new CollapseFrame { cell = cell, map = map, roof = roof, kind = kind };
        frames.Add(frame);
        if (frame.active)
        {
            activeFrames++;
        }
    }

    public static CollapseFrame Pop()
    {
        if (frames.Count == 0)
        {
            return null;
        }
        CollapseFrame frame = frames[frames.Count - 1];
        frames.RemoveAt(frames.Count - 1);
        if (frame.active)
        {
            activeFrames--;
        }
        return frame;
    }

    public static CollapseFrame Current => frames.Count > 0 ? frames[frames.Count - 1] : null;

    // Pulls everything we want to save off the collapsing cell before vanilla starts crushing it.
    public static void ExtractInitialOccupants(CollapseFrame frame)
    {
        ExtractFromCell(frame, addBattleLog: true);
    }

    // After vanilla has dropped the rubble: anything still standing in the cell is now inside the rock.
    public static void ExtractStragglers(CollapseFrame frame)
    {
        ExtractFromCell(frame, addBattleLog: true);
    }

    private static void ExtractFromCell(CollapseFrame frame, bool addBattleLog)
    {
        if (frame.map == null || !frame.cell.InBounds(frame.map))
        {
            return;
        }
        List<Thing> things = frame.map.thingGrid.ThingsListAtFast(frame.cell);
        if (things.Count == 0)
        {
            return;
        }
        List<Thing> snapshot = new List<Thing>(things);
        for (int i = 0; i < snapshot.Count; i++)
        {
            Thing t = snapshot[i];
            if (t.Spawned && ShouldBury(t))
            {
                Extract(t, frame, addBattleLog && t is Pawn);
            }
        }
    }

    // Called when vanilla is about to crush or kill something: takes over pawns (and corpses) on a
    // collapsing cell that appeared mid-collapse (e.g. ejected from a destroyed casket). Returns true if
    // vanilla's damage/kill must be skipped.
    public static bool TryIntercept(Thing t)
    {
        if (!t.Spawned)
        {
            return false;
        }
        for (int i = frames.Count - 1; i >= 0; i--)
        {
            CollapseFrame frame = frames[i];
            if (!frame.active || t.Map != frame.map || t.Position != frame.cell)
            {
                continue;
            }
            if (!ShouldBury(t))
            {
                return false;
            }
            // Vanilla already logged the falling ceiling for pawns it is about to damage.
            Extract(t, frame, addBattleLog: false);
            return true;
        }
        return false;
    }

    // True while a collapse is being processed on this exact cell.
    public static bool IsCollapsing(Map map, IntVec3 cell)
    {
        for (int i = frames.Count - 1; i >= 0; i--)
        {
            if (frames[i].active && frames[i].map == map && frames[i].cell == cell)
            {
                return true;
            }
        }
        return false;
    }

    // True for things we already took over in an ongoing collapse; vanilla must not touch them.
    public static bool IsHandled(Thing t)
    {
        if (activeFrames == 0)
        {
            return false;
        }
        for (int i = frames.Count - 1; i >= 0; i--)
        {
            if (frames[i].active && frames[i].extracted.Contains(t))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ShouldBury(Thing t)
    {
        SurviveCollapsesSettings s = SurviveCollapsesMod.Settings;
        if (t is Pawn pawn)
        {
            if (pawn.Dead || pawn.Destroyed)
            {
                return false;
            }
            return s.affectNonPlayerPawns || BurialUtility.IsPlayerRelevant(pawn);
        }
        if (t is Corpse corpse)
        {
            return s.preserveCorpses && corpse.InnerPawn != null && !corpse.Destroyed;
        }
        return false;
    }

    private static void Extract(Thing t, CollapseFrame frame, bool addBattleLog)
    {
        if (t is Pawn pawn)
        {
            // Whoever they were carrying shares their fate: bury them as a separate victim.
            Thing carried = pawn.carryTracker?.CarriedThing;
            if (carried is Pawn || carried is Corpse)
            {
                Thing taken = pawn.carryTracker.innerContainer.Take(carried);
                if (taken != null)
                {
                    frame.extracted.Add(taken);
                }
            }
            if (addBattleLog)
            {
                Find.BattleLog.Add(new BattleLogEntry_DamageTaken(pawn, RulePackDefOf.DamageEvent_Ceiling));
            }
        }
        t.DeSpawnOrDeselect();
        frame.extracted.Add(t);
    }
}
