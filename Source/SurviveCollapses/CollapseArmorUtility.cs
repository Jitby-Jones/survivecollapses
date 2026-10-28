using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SurviveCollapses;

// Armor model for collapse damage.
//
// The falling rock is treated as a blunt hit whose penetration equals the immunity threshold
// (by default the blunt rating of masterwork cataphract armor). Every armor layer covering the hit part
// plus natural armor subtracts from that penetration, exactly like Combat Extended's blunt armor math:
// damage factor = 1 - protection / threshold. At or above the threshold the part takes no damage.
//
// By default this is only used when Combat Extended is loaded; in vanilla, armor does nothing against
// a collapse (players can opt in from the mod settings).
public static class CollapseArmorUtility
{
    private const float VanillaFallbackThreshold = 0.725f; // vanilla cataphract blunt 50% x masterwork 1.45
    private const float CEFallbackThreshold = 75f;         // CE cataphract helmet blunt 50 MPa x masterwork 1.5
    private const float VanillaMasterworkFactor = 1.45f;
    private const float CEMasterworkFactor = 1.5f;

    private static readonly string[] ReferenceArmorDefs = { "Apparel_ArmorCataphract", "Apparel_ArmorHelmetCataphract" };

    private static Func<Apparel, StatDef, BodyPartRecord, float> cePartialStatApparel;
    private static Func<Pawn, StatDef, BodyPartRecord, float> cePartialStatPawn;

    public static bool CombatExtendedActive { get; private set; }

    public static float BaseThreshold { get; private set; } = VanillaFallbackThreshold;

    public static string ThresholdSourceLabel { get; private set; } = "";

    public static bool ArmorApplies
    {
        get
        {
            SurviveCollapsesSettings s = SurviveCollapsesMod.Settings;
            return CombatExtendedActive ? s.armorWithCombatExtended : s.armorWithoutCombatExtended;
        }
    }

    public static float ImmunityThreshold => Mathf.Max(0.0001f, BaseThreshold * SurviveCollapsesMod.Settings.immunityThresholdScale);

    public static void Initialize()
    {
        Type armorUtilityCE = GenTypes.GetTypeInAnyAssembly("CombatExtended.ArmorUtilityCE");
        if (armorUtilityCE != null)
        {
            MethodInfo apparelMethod = AccessTools.Method(armorUtilityCE, "PartialStat", new[] { typeof(Apparel), typeof(StatDef), typeof(BodyPartRecord) });
            MethodInfo pawnMethod = AccessTools.Method(armorUtilityCE, "PartialStat", new[] { typeof(Pawn), typeof(StatDef), typeof(BodyPartRecord) });
            try
            {
                if (apparelMethod != null)
                {
                    cePartialStatApparel = (Func<Apparel, StatDef, BodyPartRecord, float>)Delegate.CreateDelegate(typeof(Func<Apparel, StatDef, BodyPartRecord, float>), apparelMethod);
                }
                if (pawnMethod != null)
                {
                    cePartialStatPawn = (Func<Pawn, StatDef, BodyPartRecord, float>)Delegate.CreateDelegate(typeof(Func<Pawn, StatDef, BodyPartRecord, float>), pawnMethod);
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Survive Collapses] Combat Extended found but its partial armor API could not be bound; using whole-item armor values. " + e.Message);
            }
            CombatExtendedActive = true;
        }

        BaseThreshold = ComputeThreshold(out string source);
        ThresholdSourceLabel = source;
        Log.Message($"[Survive Collapses] Initialized. Combat Extended: {(CombatExtendedActive ? "yes" : "no")}. Collapse immunity threshold: {FormatArmor(BaseThreshold)} ({source}).");
    }

    private static float ComputeThreshold(out string source)
    {
        float best = float.MaxValue;
        List<string> used = new List<string>();
        foreach (string defName in ReferenceArmorDefs)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                continue;
            }
            float value = MasterworkBlunt(def);
            if (value > 0f && value < best)
            {
                best = value;
            }
            used.Add(def.label);
        }
        if (best < float.MaxValue)
        {
            source = "SC_ThresholdSourceMasterwork".Translate(string.Join(" / ", used));
            return best;
        }
        source = "SC_ThresholdSourceFallback".Translate();
        return CombatExtendedActive ? CEFallbackThreshold : VanillaFallbackThreshold;
    }

    private static float MasterworkBlunt(ThingDef def)
    {
        try
        {
            ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            StatWorker worker = StatDefOf.ArmorRating_Blunt.Worker;
            float normal = worker.GetValue(StatRequest.For(def, stuff, QualityCategory.Normal));
            float masterwork = worker.GetValue(StatRequest.For(def, stuff, QualityCategory.Masterwork));
            if (masterwork <= normal)
            {
                // Quality didn't apply to an abstract request; apply the standard masterwork factor ourselves.
                masterwork = normal * (CombatExtendedActive ? CEMasterworkFactor : VanillaMasterworkFactor);
            }
            return masterwork;
        }
        catch (Exception e)
        {
            Log.Warning($"[Survive Collapses] Could not evaluate blunt armor of {def.defName}: {e.Message}");
            return 0f;
        }
    }

    public static string FormatArmor(float value)
    {
        return CombatExtendedActive ? value.ToString("0.#") + " MPa" : value.ToStringPercent();
    }

    // Total blunt protection over a body part: every worn layer that covers it plus natural armor.
    public static float BluntProtection(Pawn pawn, BodyPartRecord part)
    {
        StatDef stat = StatDefOf.ArmorRating_Blunt;
        float total = 0f;
        List<Apparel> worn = pawn.apparel?.WornApparel;
        if (worn != null)
        {
            for (int i = 0; i < worn.Count; i++)
            {
                total += ApparelProtection(worn[i], stat, part);
            }
        }
        total += NaturalProtection(pawn, stat, part);
        return total;
    }

    private static float ApparelProtection(Apparel apparel, StatDef stat, BodyPartRecord part)
    {
        if (cePartialStatApparel != null)
        {
            try
            {
                return Mathf.Max(0f, cePartialStatApparel(apparel, stat, part));
            }
            catch (Exception)
            {
                cePartialStatApparel = null;
            }
        }
        return apparel.def.apparel != null && apparel.def.apparel.CoversBodyPart(part) ? Mathf.Max(0f, apparel.GetStatValue(stat)) : 0f;
    }

    private static float NaturalProtection(Pawn pawn, StatDef stat, BodyPartRecord part)
    {
        if (cePartialStatPawn != null)
        {
            try
            {
                return Mathf.Max(0f, cePartialStatPawn(pawn, stat, part));
            }
            catch (Exception)
            {
                cePartialStatPawn = null;
            }
        }
        return Mathf.Max(0f, pawn.GetStatValue(stat));
    }

    // 0 = no protection, 1 = immune. Always 0 when armor isn't in play.
    public static float Mitigation(Pawn pawn, BodyPartRecord part)
    {
        if (!ArmorApplies || part == null)
        {
            return 0f;
        }
        return Mathf.Clamp01(BluntProtection(pawn, part) / ImmunityThreshold);
    }

    public static bool IsImmune(Pawn pawn, BodyPartRecord part) => Mitigation(pawn, part) >= 0.999f;
}
