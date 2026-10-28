using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace SurviveCollapses;

public static class CollapseOutcomeUtility
{
    public static float BaseDeathChance(CollapseTier tier)
    {
        SurviveCollapsesSettings s = SurviveCollapsesMod.Settings;
        return tier == CollapseTier.Mountain ? s.mountainDeathChance : s.roofDeathChance;
    }

    // Armor (when in play) over the head, neck and torso scales the odds down; full immunity there means
    // the collapse can't kill.
    public static float DeathChance(Pawn pawn, CollapseTier tier)
    {
        return Mathf.Clamp01(BaseDeathChance(tier) * (1f - CrushInjuryUtility.VitalProtection(pawn)));
    }

    // Decides and applies the fate of a pawn that was just buried. Returns true if they are still alive.
    public static bool Resolve(Pawn pawn, CollapseTier tier)
    {
        bool dies = Rand.Chance(DeathChance(pawn, tier));

        // Pin first: the pawn goes down without a damage source, which also stops vanilla from rolling
        // its own death-on-downed chance partway through the injuries below.
        Pin(pawn);
        try
        {
            if (dies)
            {
                CrushInjuryUtility.ApplyLethalInjuries(pawn, tier);
            }
            else
            {
                CrushInjuryUtility.ApplySurvivorInjuries(pawn, tier);
            }
        }
        catch (Exception e)
        {
            Log.Error($"[Survive Collapses] Error applying collapse injuries to {pawn}: {e}");
        }

        if (dies && !pawn.Dead)
        {
            pawn.Kill(new DamageInfo(DamageDefOf.Crush, 9999f, 999f, -1f, null, null, null, DamageInfo.SourceCategory.Collapse));
        }

        if (pawn.Dead)
        {
            Unpin(pawn);
            return false;
        }
        Pin(pawn);
        return true;
    }

    // Fallback when there was no rubble to bury the pawn in: they were hit where they stand.
    public static void ResolveWithoutRubble(Pawn pawn, CollapseTier tier)
    {
        bool dies = Rand.Chance(DeathChance(pawn, tier));
        try
        {
            if (dies)
            {
                CrushInjuryUtility.ApplyLethalInjuries(pawn, tier);
            }
            else
            {
                CrushInjuryUtility.ApplySurvivorInjuries(pawn, tier);
            }
        }
        catch (Exception e)
        {
            Log.Error($"[Survive Collapses] Error applying collapse injuries to {pawn}: {e}");
        }
        if (dies && !pawn.Dead)
        {
            pawn.Kill(new DamageInfo(DamageDefOf.Crush, 9999f, 999f, -1f, null, null, null, DamageInfo.SourceCategory.Collapse));
        }
    }

    public static void Pin(Pawn pawn)
    {
        if (pawn == null || pawn.Dead || pawn.health.hediffSet.HasHediff(SC_DefOf.SC_PinnedUnderRubble))
        {
            return;
        }
        pawn.health.AddHediff(SC_DefOf.SC_PinnedUnderRubble);
    }

    public static void Unpin(Pawn pawn)
    {
        Hediff pinned = pawn?.health?.hediffSet?.GetFirstHediffOfDef(SC_DefOf.SC_PinnedUnderRubble);
        if (pinned != null)
        {
            pawn.health.RemoveHediff(pinned);
        }
    }
}
