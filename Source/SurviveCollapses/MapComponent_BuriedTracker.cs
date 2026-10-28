using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace SurviveCollapses;

public enum BurialStatus
{
    Alive,
    Killed,
    BodyBuried
}

public class BurialReport
{
    public readonly Pawn pawn;
    public readonly BurialStatus status;
    public readonly Thing rubble;
    public readonly CollapseTier tier;

    public BurialReport(Pawn pawn, BurialStatus status, Thing rubble, CollapseTier tier)
    {
        this.pawn = pawn;
        this.status = status;
        this.rubble = rubble;
        this.tier = tier;
    }
}

// Ticks buried pawns (rubble itself never ticks) and batches burial notifications into one letter.
// Nothing is saved here: rubble holding pawns re-registers itself when it spawns on load.
public class MapComponent_BuriedTracker : MapComponent
{
    private readonly List<CompBuriedContents> holders = new List<CompBuriedContents>();
    private readonly List<CompBuriedContents> tickBuffer = new List<CompBuriedContents>();
    private readonly List<BurialReport> pendingReports = new List<BurialReport>();
    private readonly List<Thing> pendingDigOuts = new List<Thing>();

    public MapComponent_BuriedTracker(Map map) : base(map)
    {
    }

    public IReadOnlyList<CompBuriedContents> Holders => holders;

    public static MapComponent_BuriedTracker For(Map map)
    {
        if (map == null)
        {
            return null;
        }
        MapComponent_BuriedTracker tracker = map.GetComponent<MapComponent_BuriedTracker>();
        if (tracker == null)
        {
            tracker = new MapComponent_BuriedTracker(map);
            map.components.Add(tracker);
        }
        return tracker;
    }

    public void Register(CompBuriedContents holder)
    {
        if (!holders.Contains(holder))
        {
            holders.Add(holder);
        }
    }

    public void Deregister(CompBuriedContents holder)
    {
        holders.Remove(holder);
    }

    public void QueueReport(BurialReport report)
    {
        pendingReports.Add(report);
    }

    public void QueueDigOut(Thing rubble)
    {
        if (!pendingDigOuts.Contains(rubble))
        {
            pendingDigOuts.Add(rubble);
        }
    }

    public override void MapComponentTick()
    {
        base.MapComponentTick();
        if (holders.Count > 0)
        {
            tickBuffer.Clear();
            tickBuffer.AddRange(holders);
            for (int i = 0; i < tickBuffer.Count; i++)
            {
                CompBuriedContents holder = tickBuffer[i];
                if (holder.parent.Spawned && holder.HasContents)
                {
                    holder.TickContents();
                }
                else if (!holder.HasContents)
                {
                    holders.Remove(holder);
                }
            }
        }
        if (pendingDigOuts.Count > 0)
        {
            List<Thing> digOuts = new List<Thing>(pendingDigOuts);
            pendingDigOuts.Clear();
            foreach (Thing rubble in digOuts)
            {
                if (rubble.Spawned && rubble.Map == map)
                {
                    DigOutDesignator.DesignateDigOut(rubble);
                }
            }
        }
        if (pendingReports.Count > 0)
        {
            SendReports();
        }
    }

    private void SendReports()
    {
        List<BurialReport> reports = new List<BurialReport>(pendingReports);
        pendingReports.Clear();

        bool anyPlayerRelevant = false;
        bool anyPlayerAlive = false;
        List<Thing> targets = new List<Thing>();
        StringBuilder lines = new StringBuilder();
        int otherAlive = 0;
        int otherDead = 0;
        foreach (BurialReport report in reports)
        {
            if (report.pawn == null)
            {
                continue;
            }
            if (report.rubble != null && report.rubble.Spawned && !targets.Contains(report.rubble))
            {
                targets.Add(report.rubble);
            }
            if (!BurialUtility.IsPlayerRelevant(report.pawn))
            {
                if (report.status == BurialStatus.Alive)
                {
                    otherAlive++;
                }
                else if (report.status == BurialStatus.Killed)
                {
                    otherDead++;
                }
                continue;
            }
            anyPlayerRelevant = true;
            string key = report.status switch
            {
                BurialStatus.Alive => "SC_LetterLineAlive",
                BurialStatus.Killed => "SC_LetterLineKilled",
                _ => "SC_LetterLineBody"
            };
            if (report.status == BurialStatus.Alive)
            {
                anyPlayerAlive = true;
            }
            lines.AppendLine("  - " + key.Translate(report.pawn.LabelShortCap));
        }

        if (anyPlayerRelevant && SurviveCollapsesMod.Settings.sendLetters)
        {
            if (otherAlive + otherDead > 0)
            {
                lines.AppendLine("  - " + "SC_LetterLineOthers".Translate(otherAlive, otherDead));
            }
            TaggedString text = (anyPlayerAlive ? "SC_LetterTextAlive" : "SC_LetterTextDead").Translate(lines.ToString().TrimEndNewlines());
            Find.LetterStack.ReceiveLetter("SC_LetterLabel".Translate(), text,
                anyPlayerAlive ? LetterDefOf.ThreatSmall : LetterDefOf.NegativeEvent, new LookTargets(targets));
        }
        else if (otherAlive > 0 && targets.Count > 0)
        {
            Messages.Message("SC_MessageOthersBuried".Translate(otherAlive), new LookTargets(targets), MessageTypeDefOf.NeutralEvent);
        }
    }

    public static int CountBuriedLivingFreeColonists(Map map)
    {
        MapComponent_BuriedTracker tracker = map?.GetComponent<MapComponent_BuriedTracker>();
        if (tracker == null || tracker.holders.Count == 0)
        {
            return 0;
        }
        int count = 0;
        foreach ((Pawn pawn, Thing _) in tracker.BuriedLivingPawns())
        {
            if (pawn.IsFreeColonist)
            {
                count++;
            }
        }
        return count;
    }

    public IEnumerable<(Pawn pawn, Thing rubble)> BuriedLivingPawns()
    {
        for (int i = 0; i < holders.Count; i++)
        {
            CompBuriedContents holder = holders[i];
            if (!holder.parent.Spawned)
            {
                continue;
            }
            foreach (Pawn pawn in holder.LivingPawns)
            {
                yield return (pawn, holder.parent);
            }
        }
    }
}
