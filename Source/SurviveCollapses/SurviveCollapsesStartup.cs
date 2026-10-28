using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SurviveCollapses;

[DefOf]
public static class SC_DefOf
{
    public static HediffDef SC_PinnedUnderRubble;
    public static ThoughtDef SC_BuriedAlive;

    static SC_DefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(SC_DefOf));
    }
}

[StaticConstructorOnStartup]
public static class SurviveCollapsesStartup
{
    static SurviveCollapsesStartup()
    {
        try
        {
            RubbleRegistry.InjectBurialComps();
        }
        catch (Exception e)
        {
            Log.Error("[Survive Collapses] Failed to attach burial comps to collapse rubble: " + e);
        }

        try
        {
            CollapseArmorUtility.Initialize();
        }
        catch (Exception e)
        {
            Log.Error("[Survive Collapses] Failed to initialize armor handling: " + e);
        }

        ApplySettingsToDefs();
    }

    // Pushes settings that live on defs (the pinned hediff's bleed factor) into the loaded defs.
    public static void ApplySettingsToDefs()
    {
        HediffDef pinned = SC_DefOf.SC_PinnedUnderRubble;
        if (pinned?.stages == null || SurviveCollapsesMod.Settings == null)
        {
            return;
        }
        foreach (HediffStage stage in pinned.stages)
        {
            stage.totalBleedFactor = SurviveCollapsesMod.Settings.buriedBleedFactor;
        }
    }
}

// Tracks which things a collapsing roof leaves behind that can hold buried pawns.
public static class RubbleRegistry
{
    private static readonly HashSet<ThingDef> rubbleDefs = new HashSet<ThingDef>();

    public static IEnumerable<ThingDef> RubbleDefs => rubbleDefs;

    public static void InjectBurialComps()
    {
        foreach (RoofDef roof in DefDatabase<RoofDef>.AllDefsListForReading)
        {
            ThingDef rubble = roof.collapseLeavingThingDef;
            if (rubble == null || rubble.passability != Traversability.Impassable || rubbleDefs.Contains(rubble))
            {
                continue;
            }
            if (rubble.thingClass == null || !typeof(ThingWithComps).IsAssignableFrom(rubble.thingClass))
            {
                Log.Warning($"[Survive Collapses] {rubble.defName} (left by collapsing {roof.defName}) cannot hold comps; pawns caught under it will die as in vanilla.");
                continue;
            }
            rubble.comps ??= new List<CompProperties>();
            if (!rubble.comps.Any(c => c is CompProperties_BuriedContents))
            {
                rubble.comps.Add(new CompProperties_BuriedContents());
            }
            // Compressed things are rebuilt from their def on load, which would silently drop whoever
            // is buried inside. Rubble is rare enough that saving it normally costs next to nothing.
            rubble.saveCompressible = false;
            rubbleDefs.Add(rubble);
        }
    }

    // Only collapses that fill the cell with rubble obliterate pawns in vanilla (overhead mountain).
    // Every other roof collapse (constructed, thin rock) is left exactly as vanilla has it. If vanilla
    // ends up not dropping rubble after all, the burial step puts everyone back unharmed.
    public static CollapseKind KindFor(RoofDef roof)
    {
        ThingDef leaving = roof?.collapseLeavingThingDef;
        return leaving != null && rubbleDefs.Contains(leaving) ? CollapseKind.Rubble : CollapseKind.None;
    }

    public static CollapseTier TierFor(RoofDef roof)
    {
        return roof != null && roof.isThickRoof ? CollapseTier.Mountain : CollapseTier.Roof;
    }
}

public enum CollapseTier
{
    Roof,
    Mountain
}

public enum CollapseKind
{
    // Not ours to handle: vanilla behavior.
    None,
    // The roof leaves impassable rubble on the cell and obliterates whatever is there (overhead mountain).
    Rubble
}
