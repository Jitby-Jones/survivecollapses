using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace SurviveCollapses;

// Red alert while any of the colony's own people or animals are trapped alive under rubble.
public class Alert_BuriedAlive : Alert_Critical
{
    private readonly List<Thing> culprits = new List<Thing>();
    private readonly List<Pawn> victims = new List<Pawn>();

    public Alert_BuriedAlive()
    {
        defaultPriority = AlertPriority.Critical;
    }

    private void Recache()
    {
        culprits.Clear();
        victims.Clear();
        List<Map> maps = Find.Maps;
        for (int i = 0; i < maps.Count; i++)
        {
            MapComponent_BuriedTracker tracker = maps[i].GetComponent<MapComponent_BuriedTracker>();
            if (tracker == null || tracker.Holders.Count == 0)
            {
                continue;
            }
            foreach ((Pawn pawn, Thing rubble) in tracker.BuriedLivingPawns())
            {
                if (!BurialUtility.IsPlayerRelevant(pawn))
                {
                    continue;
                }
                victims.Add(pawn);
                if (!culprits.Contains(rubble))
                {
                    culprits.Add(rubble);
                }
            }
        }
    }

    public override string GetLabel()
    {
        return victims.Count == 1
            ? "SC_AlertLabelSingle".Translate(victims[0].LabelShortCap)
            : "SC_AlertLabelMulti".Translate(victims.Count);
    }

    public override TaggedString GetExplanation()
    {
        StringBuilder sb = new StringBuilder();
        foreach (Pawn pawn in victims)
        {
            sb.AppendLine("  - " + pawn.LabelShortCap);
        }
        return "SC_AlertDesc".Translate(sb.ToString().TrimEndNewlines());
    }

    public override AlertReport GetReport()
    {
        Recache();
        return victims.Count == 0 ? AlertReport.Inactive : AlertReport.CulpritsAre(culprits);
    }
}
