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
// The falling rock is a blunt hit whose penetration, per body part, equals what a full masterwork
// cataphract set (armor + helmet) provides on that part. Every armor layer covering the hit part plus
// natural armor subtracts from that penetration, exactly like Combat Extended's blunt armor math:
// damage factor = 1 - protection / threshold. At or above the threshold the part takes no damage, so a
// pawn in full masterwork cataphract is immune wherever the set covers them.
//
// By default this is only used when Combat Extended is loaded; in vanilla, armor does nothing against
// a collapse (players can opt in from the mod settings).
public static class CollapseArmorUtility
{
    // Masterwork cataphract body armor / helmet blunt values, used when Royalty isn't loaded.
    private const float VanillaFallbackBody = 0.725f;   // 50% x masterwork 1.45
    private const float VanillaFallbackHelmet = 0.725f;
    private const float CEFallbackBody = 90f;           // CE 60 MPa x masterwork 1.5
    private const float CEFallbackHelmet = 75f;         // CE 50 MPa x masterwork 1.5
    private const float VanillaMasterworkFactor = 1.45f;
    private const float CEMasterworkFactor = 1.5f;

    private const string ReferenceArmorDef = "Apparel_ArmorCataphract";
    private const string ReferenceHelmetDef = "Apparel_ArmorHelmetCataphract";

    private static Func<Apparel, StatDef, BodyPartRecord, float> cePartialStatApparel;
    private static Func<Pawn, StatDef, BodyPartRecord, float> cePartialStatPawn;

    // Masterwork reference pieces, made on first use (making things needs a running game).
    private static List<Apparel> referenceSet;
    private static readonly Dictionary<BodyPartRecord, float> partThresholds = new Dictionary<BodyPartRecord, float>();

    public static bool CombatExtendedActive { get; private set; }

    // Masterwork cataphract body armor blunt value: the threshold for parts the set doesn't cover.
    public static float BodyThreshold { get; private set; } = VanillaFallbackBody;

    public static float HelmetThreshold { get; private set; } = VanillaFallbackHelmet;

    public static bool ReferenceArmorLoaded { get; private set; }

    public static bool ArmorApplies
    {
        get
        {
            SurviveCollapsesSettings s = SurviveCollapsesMod.Settings;
            return CombatExtendedActive ? s.armorWithCombatExtended : s.armorWithoutCombatExtended;
        }
    }

    private static float Scale => SurviveCollapsesMod.Settings.immunityThresholdScale;

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

        ThingDef armor = DefDatabase<ThingDef>.GetNamedSilentFail(ReferenceArmorDef);
        ThingDef helmet = DefDatabase<ThingDef>.GetNamedSilentFail(ReferenceHelmetDef);
        float armorValue = armor != null ? MasterworkBlunt(armor) : 0f;
        float helmetValue = helmet != null ? MasterworkBlunt(helmet) : 0f;
        ReferenceArmorLoaded = armorValue > 0f;
        BodyThreshold = armorValue > 0f ? armorValue : (CombatExtendedActive ? CEFallbackBody : VanillaFallbackBody);
        HelmetThreshold = helmetValue > 0f ? helmetValue : (CombatExtendedActive ? CEFallbackHelmet : VanillaFallbackHelmet);

        Log.Message($"[Survive Collapses] Initialized. Combat Extended: {(CombatExtendedActive ? "yes" : "no")}. "
            + $"Collapse immunity: masterwork cataphract, body {FormatArmor(BodyThreshold)}, helmet {FormatArmor(HelmetThreshold)}"
            + (ReferenceArmorLoaded ? "." : " (built-in values; cataphract armor isn't loaded)."));
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

    // Blunt protection a full masterwork cataphract set gives this exact part (per-part values from CE
    // included). Parts the set doesn't cover, and non-humanlike bodies, use the body armor's value.
    public static float ImmunityThreshold(BodyPartRecord part)
    {
        return Mathf.Max(0.0001f, UnscaledThreshold(part) * Scale);
    }

    private static float UnscaledThreshold(BodyPartRecord part)
    {
        if (part == null)
        {
            return BodyThreshold;
        }
        if (partThresholds.TryGetValue(part, out float cached))
        {
            return cached;
        }
        EnsureReferenceSet();
        float sum = 0f;
        if (referenceSet != null)
        {
            StatDef stat = StatDefOf.ArmorRating_Blunt;
            for (int i = 0; i < referenceSet.Count; i++)
            {
                sum += ApparelProtection(referenceSet[i], stat, part);
            }
        }
        float threshold = sum > 0f ? sum : BodyThreshold;
        if (referenceSet != null)
        {
            partThresholds[part] = threshold;
        }
        return threshold;
    }

    private static void EnsureReferenceSet()
    {
        if (referenceSet != null || Current.Game == null)
        {
            return;
        }
        referenceSet = new List<Apparel>();
        foreach (string defName in new[] { ReferenceArmorDef, ReferenceHelmetDef })
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                continue;
            }
            try
            {
                if (ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null) is Apparel apparel)
                {
                    apparel.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Masterwork, ArtGenerationContext.Outsider);
                    apparel.HitPoints = apparel.MaxHitPoints;
                    referenceSet.Add(apparel);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[Survive Collapses] Could not make reference {defName}: {e.Message}");
            }
        }
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
                // Like CE's own crush handling: an internal hit goes through every layer out to the first
                // outer part, and each layer covered by natural armor (a mech's plating, a thrumbo's hide)
                // counts.
                float total = 0f;
                for (BodyPartRecord cur = part; cur != null; cur = cur.parent)
                {
                    total += Mathf.Max(0f, cePartialStatPawn(pawn, stat, cur));
                    if (cur.depth != BodyPartDepth.Inside)
                    {
                        break;
                    }
                }
                return total;
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
        return Mathf.Clamp01(BluntProtection(pawn, part) / ImmunityThreshold(part));
    }
}
