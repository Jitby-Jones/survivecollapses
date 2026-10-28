using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace SurviveCollapses;

public class CompProperties_BuriedContents : CompProperties
{
    public CompProperties_BuriedContents()
    {
        compClass = typeof(CompBuriedContents);
    }
}

// Lives on collapse rubble (vanilla: CollapsedRocks). Holds whoever was caught under the collapse until
// the rubble is mined out or otherwise destroyed, then puts them back on the map.
public class CompBuriedContents : ThingComp, IThingHolder
{
    private const int MaintenanceInterval = 250;

    private ThingOwner<Thing> innerContainer;
    private IntVec3 lastPosition = IntVec3.Invalid;
    private Map lastMap;

    public CompBuriedContents()
    {
        innerContainer = new ThingOwner<Thing>(this, oneStackOnly: false, LookMode.Deep);
    }

    public ThingOwner<Thing> Contents => innerContainer;

    public bool HasContents => innerContainer != null && innerContainer.Count > 0;

    // IThingHolder.ParentHolder is implemented by ThingComp.ParentHolder.

    public ThingOwner GetDirectlyHeldThings() => innerContainer;

    public void GetChildHolders(List<IThingHolder> outChildren)
    {
        ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
    }

    public IEnumerable<Pawn> LivingPawns
    {
        get
        {
            for (int i = 0; i < innerContainer.Count; i++)
            {
                if (innerContainer[i] is Pawn pawn && !pawn.Dead)
                {
                    yield return pawn;
                }
            }
        }
    }

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);
        lastPosition = parent.Position;
        lastMap = parent.Map;
        if (HasContents)
        {
            MapComponent_BuriedTracker.For(parent.Map)?.Register(this);
        }
    }

    public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
    {
        base.PostDeSpawn(map, mode);
        MapComponent_BuriedTracker.For(map)?.Deregister(this);
    }

    public override void PostDestroy(DestroyMode mode, Map previousMap)
    {
        base.PostDestroy(mode, previousMap);
        if (HasContents)
        {
            ReleaseContents(previousMap ?? lastMap, lastPosition);
        }
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        // Most rubble never holds anything; don't write an empty container for every rock.
        if (Scribe.mode != LoadSaveMode.Saving || HasContents)
        {
            Scribe_Deep.Look(ref innerContainer, "SC_buriedContents", this);
        }
        innerContainer ??= new ThingOwner<Thing>(this, oneStackOnly: false, LookMode.Deep);
    }

    public void Bury(Thing thing, CollapseTier tier)
    {
        Map map = parent.Map;
        if (!innerContainer.TryAdd(thing, canMergeWithExistingStacks: false))
        {
            Log.Warning($"[Survive Collapses] Could not bury {thing} under {parent}; placing it nearby instead.");
            BurialUtility.PlaceNear(thing, parent.Position, map);
            return;
        }
        MapComponent_BuriedTracker tracker = MapComponent_BuriedTracker.For(map);
        tracker?.Register(this);

        Pawn victim;
        BurialStatus status;
        if (thing is Pawn pawn)
        {
            victim = pawn;
            status = CollapseOutcomeUtility.Resolve(pawn, tier) ? BurialStatus.Alive : BurialStatus.Killed;
        }
        else if (thing is Corpse corpse)
        {
            victim = corpse.InnerPawn;
            status = BurialStatus.BodyBuried;
        }
        else
        {
            return;
        }

        tracker?.QueueReport(new BurialReport(victim, status, parent, tier));
        if (SurviveCollapsesMod.Settings.autoDesignateDigging && BurialUtility.IsPlayerRelevant(victim))
        {
            // Wait until the whole collapse is over so the dig path isn't routed through cells that are
            // about to fill with rubble.
            tracker?.QueueDigOut(parent);
        }
    }

    // Called every tick by the map's burial tracker while something is buried here.
    public void TickContents()
    {
        if (!HasContents)
        {
            return;
        }
        innerContainer.DoTick();
        if (parent.IsHashIntervalTick(MaintenanceInterval))
        {
            Maintain();
        }
    }

    private void Maintain()
    {
        HediffDef pinnedDef = SC_DefOf.SC_PinnedUnderRubble;
        for (int i = innerContainer.Count - 1; i >= 0; i--)
        {
            Thing t = innerContainer[i];
            if (t is Pawn pawn && !pawn.Dead && !pawn.health.hediffSet.HasHediff(pinnedDef))
            {
                CollapseOutcomeUtility.Pin(pawn);
            }
            else if (t is Corpse corpse && corpse.InnerPawn != null)
            {
                CollapseOutcomeUtility.Unpin(corpse.InnerPawn);
            }
        }

        // A pawn that dies down here should stay down here. If anything put its corpse on top of the
        // rubble instead of inside it, take it back.
        if (parent.Spawned)
        {
            List<Thing> things = parent.Map.thingGrid.ThingsListAtFast(parent.Position);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                if (things[i] is Corpse loose && innerContainer.Count > 0)
                {
                    loose.DeSpawnOrDeselect();
                    innerContainer.TryAdd(loose, canMergeWithExistingStacks: false);
                }
            }
        }
    }

    public void ReleaseContents(Map map, IntVec3 cell)
    {
        MapComponent_BuriedTracker.For(map)?.Deregister(this);
        if (!HasContents)
        {
            return;
        }
        if (map == null || !cell.IsValid)
        {
            innerContainer.ClearAndDestroyContentsOrPassToWorld();
            return;
        }

        // Rubble crushed by a fresh collapse on the same cell: whoever was inside is about to be buried
        // again by that collapse, so don't announce a rescue.
        bool silent = CollapseContext.IsCollapsing(map, cell);

        List<Thing> things = new List<Thing>(innerContainer.InnerListForReading);
        foreach (Thing t in things)
        {
            Pawn pawn = t as Pawn ?? (t as Corpse)?.InnerPawn;
            if (pawn != null)
            {
                CollapseOutcomeUtility.Unpin(pawn);
            }
            innerContainer.Remove(t);
            if (!BurialUtility.PlaceNear(t, cell, map) || silent)
            {
                continue;
            }
            if (t is Pawn freed && !freed.Dead)
            {
                OnDugOutAlive(freed);
            }
            else if (t is Corpse body && body.InnerPawn != null && BurialUtility.IsPlayerRelevant(body.InnerPawn))
            {
                Messages.Message("SC_MessageBodyRecovered".Translate(body.InnerPawn.LabelShortCap), body, MessageTypeDefOf.NeutralEvent);
            }
        }
    }

    private static void OnDugOutAlive(Pawn pawn)
    {
        if (SurviveCollapsesMod.Settings.buriedAliveThought && pawn.needs?.mood?.thoughts?.memories != null)
        {
            pawn.needs.mood.thoughts.memories.TryGainMemory(SC_DefOf.SC_BuriedAlive);
        }
        bool ours = BurialUtility.IsPlayerRelevant(pawn);
        if (ours || pawn.HostileTo(Faction.OfPlayer))
        {
            Messages.Message("SC_MessageDugOutAlive".Translate(pawn.LabelShortCap), pawn,
                ours ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.ThreatSmall);
        }
    }

    public override string CompInspectStringExtra()
    {
        if (!HasContents)
        {
            return null;
        }
        StringBuilder sb = new StringBuilder();
        sb.Append("SC_InspectBuriedHeader".Translate());
        foreach (Thing t in innerContainer)
        {
            sb.AppendLine();
            sb.Append("  - ");
            if (t is Pawn pawn && !pawn.Dead)
            {
                int ticksToDeath = HealthUtility.TicksUntilDeathDueToBloodLoss(pawn);
                if (ticksToDeath < GenDate.TicksPerDay * 3)
                {
                    sb.Append("SC_InspectAliveBleeding".Translate(pawn.LabelShortCap, ticksToDeath.ToStringTicksToPeriod()));
                }
                else
                {
                    sb.Append("SC_InspectAlive".Translate(pawn.LabelShortCap));
                }
            }
            else if (t is Corpse corpse && corpse.InnerPawn != null)
            {
                sb.Append("SC_InspectDead".Translate(corpse.InnerPawn.LabelShortCap));
            }
            else
            {
                sb.Append(t.LabelCap);
            }
        }
        return sb.ToString();
    }

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        foreach (Gizmo gizmo in base.CompGetGizmosExtra())
        {
            yield return gizmo;
        }
        if (DebugSettings.ShowDevGizmos && HasContents)
        {
            yield return new Command_Action
            {
                defaultLabel = "DEV: Release buried",
                action = () => ReleaseContents(parent.Map, parent.Position)
            };
        }
    }
}
