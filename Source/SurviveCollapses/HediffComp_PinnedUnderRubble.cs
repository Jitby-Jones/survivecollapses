using Verse;

namespace SurviveCollapses;

public class HediffCompProperties_PinnedUnderRubble : HediffCompProperties
{
    public HediffCompProperties_PinnedUnderRubble()
    {
        compClass = typeof(HediffComp_PinnedUnderRubble);
    }
}

// The pinned state only makes sense while the pawn is actually held by collapse rubble. If the pawn
// ends up anywhere else (dug out, moved by another mod, dev tools), the hediff removes itself.
public class HediffComp_PinnedUnderRubble : HediffComp
{
    private const int CheckInterval = 120;

    private int ticksUntilCheck = CheckInterval;

    public override bool CompShouldRemove => base.CompShouldRemove || (ticksUntilCheck <= 0 && !IsBuried(Pawn));

    public override void CompPostTickInterval(ref float severityAdjustment, int delta)
    {
        base.CompPostTickInterval(ref severityAdjustment, delta);
        if (ticksUntilCheck > 0)
        {
            ticksUntilCheck -= delta;
        }
    }

    public override void CompExposeData()
    {
        base.CompExposeData();
        Scribe_Values.Look(ref ticksUntilCheck, "ticksUntilCheck", CheckInterval);
    }

    public static bool IsBuried(Pawn pawn)
    {
        return pawn != null && !pawn.Spawned && pawn.ParentHolder is CompBuriedContents;
    }
}
