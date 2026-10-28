using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SurviveCollapses;

// Turns a collapse into concrete crush wounds: crushed (destroyed) limbs, crushed organs, cracked bones
// and general crush injuries. Survivors are hurt badly but never fatally; the dead are mangled first.
public static class CrushInjuryUtility
{
    // Survivors are kept below this share of the lethal total-injury threshold.
    private const float SurvivorInjuryBudget = 0.6f;

    // A lethal part (neck, head, torso, heart...) keeps at least this share of its health on survivors.
    private const float SurvivorLethalPartFloor = 0.35f;

    // Share of the incoming damage that each covering apparel layer takes.
    private const float ApparelDamageShare = 0.25f;

    private static readonly List<BodyPartRecord> tmpParts = new List<BodyPartRecord>();

    private struct InjuryPlan
    {
        public int limbCrushes;
        public int organCrushes;
        public int boneHits;
        public int bruises;
        public FloatRange boneFraction;
        public FloatRange bruiseFraction;
    }

    private static float Severity => SurviveCollapsesMod.Settings.injurySeverity;

    private static int Roll(float chance) => Rand.Chance(Mathf.Clamp01(chance)) ? 1 : 0;

    private static InjuryPlan SurvivorPlan(CollapseTier tier)
    {
        float sev = Severity;
        if (tier == CollapseTier.Mountain)
        {
            return new InjuryPlan
            {
                limbCrushes = 1 + Roll(0.4f * sev),
                organCrushes = 1 + Roll(0.2f * sev),
                boneHits = Rand.RangeInclusive(2, 3),
                bruises = Rand.RangeInclusive(4, 7),
                boneFraction = new FloatRange(0.55f, 0.95f),
                bruiseFraction = new FloatRange(0.2f, 0.55f)
            };
        }
        return new InjuryPlan
        {
            limbCrushes = Roll(0.85f * sev) + Roll(0.15f * sev),
            organCrushes = Roll(0.55f * sev),
            boneHits = Rand.RangeInclusive(1, 2),
            bruises = Rand.RangeInclusive(3, 5),
            boneFraction = new FloatRange(0.45f, 0.85f),
            bruiseFraction = new FloatRange(0.15f, 0.45f)
        };
    }

    private static InjuryPlan LethalPlan(CollapseTier tier)
    {
        if (tier == CollapseTier.Mountain)
        {
            return new InjuryPlan
            {
                limbCrushes = Rand.RangeInclusive(2, 3),
                organCrushes = Rand.RangeInclusive(2, 3),
                boneHits = Rand.RangeInclusive(3, 4),
                bruises = Rand.RangeInclusive(5, 8),
                boneFraction = new FloatRange(0.7f, 1.2f),
                bruiseFraction = new FloatRange(0.3f, 0.7f)
            };
        }
        return new InjuryPlan
        {
            limbCrushes = Rand.RangeInclusive(1, 2),
            organCrushes = Rand.RangeInclusive(1, 2),
            boneHits = Rand.RangeInclusive(2, 3),
            bruises = Rand.RangeInclusive(4, 6),
            boneFraction = new FloatRange(0.6f, 1.1f),
            bruiseFraction = new FloatRange(0.25f, 0.6f)
        };
    }

    // Heavy, non-fatal injuries: at least one crushed limb (or organ under a mountain), cracked bones, bruising.
    public static void ApplySurvivorInjuries(Pawn pawn, CollapseTier tier)
    {
        InjuryPlan plan = SurvivorPlan(tier);
        if (plan.limbCrushes + plan.organCrushes == 0)
        {
            plan.limbCrushes = 1;
        }

        for (int i = 0; i < plan.limbCrushes && !pawn.Dead; i++)
        {
            BodyPartRecord limb = RandomLimb(pawn, survivor: true);
            if (limb != null)
            {
                CrushPart(pawn, limb, survivor: true);
            }
        }

        for (int i = 0; i < plan.organCrushes && !pawn.Dead; i++)
        {
            BodyPartRecord organ = RandomOrgan(pawn, allowLethal: false);
            if (organ != null)
            {
                CrushPart(pawn, organ, survivor: true);
            }
            else
            {
                // Nothing that can safely be lost: badly damage a vital organ instead (the guards cap it).
                organ = RandomOrgan(pawn, allowLethal: true);
                if (organ != null)
                {
                    HitFraction(pawn, organ, new FloatRange(0.4f, 0.6f), survivor: true);
                }
            }
        }

        ApplyBonesAndBruises(pawn, plan, survivor: true);
    }

    // Mangles the body (crushed limbs and organs) and then delivers a fatal blow. The caller makes sure
    // the pawn is dead afterwards even if armor or death-preventing effects soak the blows.
    public static void ApplyLethalInjuries(Pawn pawn, CollapseTier tier)
    {
        InjuryPlan plan = LethalPlan(tier);

        // Order matters: anything after the first fatal wound never lands, so crush limbs and organs that
        // can be lost first, so the body always shows them.
        for (int i = 0; i < plan.limbCrushes && !pawn.Dead; i++)
        {
            BodyPartRecord limb = RandomLimb(pawn, survivor: false);
            if (limb != null)
            {
                CrushPart(pawn, limb, survivor: false);
            }
        }

        for (int i = 0; i < plan.organCrushes && !pawn.Dead; i++)
        {
            BodyPartRecord organ = RandomOrgan(pawn, allowLethal: false) ?? RandomOrgan(pawn, allowLethal: true);
            if (organ != null)
            {
                CrushPart(pawn, organ, survivor: false);
            }
        }

        ApplyBonesAndBruises(pawn, plan, survivor: false);

        if (!pawn.Dead)
        {
            FatalBlow(pawn, tier);
        }
    }

    private static void ApplyBonesAndBruises(Pawn pawn, InjuryPlan plan, bool survivor)
    {
        float sev = Severity;
        FloatRange boneFraction = new FloatRange(plan.boneFraction.min * sev, plan.boneFraction.max * sev);
        FloatRange bruiseFraction = new FloatRange(plan.bruiseFraction.min * sev, plan.bruiseFraction.max * sev);

        for (int i = 0; i < plan.boneHits && !pawn.Dead; i++)
        {
            BodyPartRecord bone = RandomBone(pawn);
            if (bone != null)
            {
                HitFraction(pawn, bone, boneFraction, survivor);
            }
        }

        for (int i = 0; i < plan.bruises && !pawn.Dead; i++)
        {
            BodyPartRecord part = pawn.health.hediffSet.GetRandomNotMissingPart(DamageDefOf.Crush, BodyPartHeight.Undefined, BodyPartDepth.Outside);
            if (part != null)
            {
                HitFraction(pawn, part, bruiseFraction, survivor);
            }
        }
    }

    // Crushes the most exposed vital region: the head (if any) or the torso.
    private static void FatalBlow(Pawn pawn, CollapseTier tier)
    {
        List<BodyPartRecord> regions = LethalRegions(pawn);
        if (regions.Count == 0)
        {
            return;
        }
        float lowest = float.MaxValue;
        for (int i = 0; i < regions.Count; i++)
        {
            lowest = Mathf.Min(lowest, CollapseArmorUtility.Mitigation(pawn, regions[i]));
        }
        regions.RemoveAll(r => CollapseArmorUtility.Mitigation(pawn, r) > lowest + 0.001f);
        BodyPartRecord target = regions.RandomElement();

        if (target.IsCorePart)
        {
            // The torso itself can't be "lost": crush what's inside it, then cave it in.
            BodyPartRecord heart = FirstNotMissingWithTag(pawn, BodyPartTagDefOf.BloodPumpingSource);
            if (heart != null && !pawn.Dead)
            {
                CrushPart(pawn, heart, survivor: false);
            }
            if (!pawn.Dead)
            {
                ApplyHit(pawn, target, pawn.health.hediffSet.GetPartHealth(target) * 1.5f + 1f, survivor: false);
            }
        }
        else
        {
            CrushPart(pawn, target, survivor: false);
        }

        // Under a mountain the weight may also flatten a second vital region.
        if (!pawn.Dead && tier == CollapseTier.Mountain && Rand.Chance(0.5f))
        {
            BodyPartRecord second = LethalRegions(pawn).Find(r => r != target && !r.IsCorePart);
            if (second != null)
            {
                CrushPart(pawn, second, survivor: false);
            }
        }
    }

    // Enough damage to destroy the part outright (before armor).
    private static void CrushPart(Pawn pawn, BodyPartRecord part, bool survivor)
    {
        float health = pawn.health.hediffSet.GetPartHealth(part);
        if (health <= 0f)
        {
            return;
        }
        ApplyHit(pawn, part, health * 2.5f + 1f, survivor);
    }

    private static void HitFraction(Pawn pawn, BodyPartRecord part, FloatRange fractionOfMaxHealth, bool survivor)
    {
        float amount = part.def.GetMaxHealth(pawn) * fractionOfMaxHealth.RandomInRange;
        ApplyHit(pawn, part, Mathf.Max(1f, amount), survivor);
    }

    // Applies one crush hit after armor (if in play) and, for survivors, after the survival guards.
    public static float ApplyHit(Pawn pawn, BodyPartRecord part, float amount, bool survivor)
    {
        if (pawn.Dead || part == null || pawn.health.hediffSet.PartIsMissing(part))
        {
            return 0f;
        }

        float incoming = amount;
        amount *= 1f - CollapseArmorUtility.Mitigation(pawn, part);
        if (SurviveCollapsesMod.Settings.damageApparel)
        {
            DamageCoveringApparel(pawn, part, incoming);
        }
        if (amount < 0.5f)
        {
            return 0f;
        }

        // Vanilla scales external violence by IncomingDamageFactor (e.g. the Tough trait) after we hand
        // the hit over, so account for it here.
        float damageFactor = DamageDefOf.Crush.ExternalViolenceFor(pawn)
            ? Mathf.Max(0.01f, pawn.GetStatValue(StatDefOf.IncomingDamageFactor))
            : 1f;
        float partHealth = pawn.health.hediffSet.GetPartHealth(part);
        if (amount * damageFactor >= partHealth)
        {
            // Destroying the part: no need to pile extra severity on top of what destroys it.
            amount = Mathf.Min(amount, (partHealth + 0.5f) / damageFactor);
        }
        if (survivor)
        {
            amount = SurvivableAmount(pawn, part, amount, damageFactor);
            if (amount < 0.5f)
            {
                return 0f;
            }
        }

        DamageInfo dinfo = new DamageInfo(DamageDefOf.Crush, amount, 0f, -1f, null, part, null, DamageInfo.SourceCategory.Collapse);
        dinfo.SetIgnoreArmor(true);
        dinfo.SetAllowDamagePropagation(false);
        dinfo.SetIgnoreInstantKillProtection(!survivor);
        pawn.TakeDamage(dinfo);
        return amount;
    }

    // Shrinks a hit until it can't kill: lethal parts keep a margin of health, total injury stays well
    // under the lethal threshold, and vanilla's own death simulation must agree.
    private static float SurvivableAmount(Pawn pawn, BodyPartRecord part, float amount, float damageFactor)
    {
        HediffSet set = pawn.health.hediffSet;
        float partHealth = set.GetPartHealth(part);
        if (partHealth <= 0f)
        {
            return 0f;
        }

        // Fatal to lose, or (the spine) permanently paralysing: badly hurt, never destroyed.
        if (IsLethalToDestroy(pawn, part) || (part.def.tags != null && part.def.tags.Contains(BodyPartTagDefOf.Spine)))
        {
            amount = Mathf.Min(amount, partHealth * (1f - SurvivorLethalPartFloor) / damageFactor);
        }

        // Wounds on a destroyed part are replaced by the missing part, so only wounds on parts that
        // survive the hit count towards the total-injury budget.
        float budget = pawn.health.LethalDamageThreshold * SurvivorInjuryBudget - TotalInjurySeverity(pawn);
        HediffDef injury = HealthUtility.GetHediffDefFromDamage(DamageDefOf.Crush, pawn, part);
        for (int i = 0; i < 8; i++)
        {
            bool destroysPart = amount * damageFactor >= partHealth;
            if (!destroysPart)
            {
                amount = Mathf.Min(amount, budget / damageFactor);
            }
            if (amount < 0.5f)
            {
                return 0f;
            }
            if (!pawn.health.WouldDieAfterAddingHediff(injury, part, amount * damageFactor))
            {
                return amount;
            }
            amount *= 0.5f;
        }
        return 0f;
    }

    private static float TotalInjurySeverity(Pawn pawn)
    {
        float total = 0f;
        List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            if (hediffs[i] is Hediff_Injury)
            {
                total += hediffs[i].Severity;
            }
        }
        return total;
    }

    private static void DamageCoveringApparel(Pawn pawn, BodyPartRecord part, float incoming)
    {
        List<Apparel> worn = pawn.apparel?.WornApparel;
        if (worn == null)
        {
            return;
        }
        for (int i = worn.Count - 1; i >= 0; i--)
        {
            if (i >= worn.Count)
            {
                continue;
            }
            Apparel apparel = worn[i];
            if (apparel.def.apparel == null || !apparel.def.apparel.CoversBodyPart(part))
            {
                continue;
            }
            int damage = GenMath.RoundRandom(incoming * ApparelDamageShare);
            if (damage > 0)
            {
                apparel.TakeDamage(new DamageInfo(DamageDefOf.Crush, damage, 0f, -1f, null, null, null, DamageInfo.SourceCategory.Collapse));
            }
        }
    }

    // ---------- body part selection ----------

    private static HashSet<BodyPartTagDef> vitalTags;

    // Tags whose last working source is fatal to lose. The XML "vital" flag is honoured, but the vanilla
    // vital functions are always included so the survivor guard never depends on it.
    private static bool IsVitalTag(BodyPartTagDef tag)
    {
        if (vitalTags == null)
        {
            vitalTags = new HashSet<BodyPartTagDef>();
            BodyPartTagDef[] known =
            {
                BodyPartTagDefOf.ConsciousnessSource, BodyPartTagDefOf.BloodPumpingSource, BodyPartTagDefOf.BreathingSource,
                BodyPartTagDefOf.BreathingSourceCage, BodyPartTagDefOf.BreathingPathway, BodyPartTagDefOf.BloodFiltrationSource,
                BodyPartTagDefOf.BloodFiltrationLiver, BodyPartTagDefOf.BloodFiltrationKidney, BodyPartTagDefOf.MetabolismSource
            };
            foreach (BodyPartTagDef def in known)
            {
                if (def != null)
                {
                    vitalTags.Add(def);
                }
            }
        }
        return tag != null && (tag.vital || vitalTags.Contains(tag));
    }

    // True if losing this part (and everything attached to it) would kill: the torso, or any subtree
    // holding the last working source of a vital function (neck/head/skull hold the brain, etc.).
    public static bool IsLethalToDestroy(Pawn pawn, BodyPartRecord part)
    {
        if (part.IsCorePart)
        {
            return true;
        }
        HediffSet set = pawn.health.hediffSet;
        foreach (BodyPartRecord sub in part.GetPartAndAllChildParts())
        {
            if (set.PartIsMissing(sub))
            {
                continue;
            }
            List<BodyPartTagDef> tags = sub.def.tags;
            if (tags == null)
            {
                continue;
            }
            for (int i = 0; i < tags.Count; i++)
            {
                if (IsVitalTag(tags[i]) && !HasTagOutside(set, tags[i], part))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool HasTagOutside(HediffSet set, BodyPartTagDef tag, BodyPartRecord excludedRoot)
    {
        foreach (BodyPartRecord other in set.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Undefined, tag))
        {
            if (!IsSelfOrDescendant(other, excludedRoot) && set.GetPartHealth(other) > 0f)
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsSelfOrDescendant(BodyPartRecord part, BodyPartRecord root)
    {
        for (BodyPartRecord cur = part; cur != null; cur = cur.parent)
        {
            if (cur == root)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasAnyTag(BodyPartRecord part, BodyPartTagDef a, BodyPartTagDef b, BodyPartTagDef c, BodyPartTagDef d)
    {
        List<BodyPartTagDef> tags = part.def.tags;
        return tags != null && (tags.Contains(a) || tags.Contains(b) || tags.Contains(c) || tags.Contains(d));
    }

    private static bool Usable(BodyPartRecord part)
    {
        return part.def != null && !part.def.conceptual && part.def.hitPoints > 0;
    }

    // Arms, legs, hands and feet. Whole limbs are a bit less likely than their extremities for survivors.
    public static BodyPartRecord RandomLimb(Pawn pawn, bool survivor)
    {
        tmpParts.Clear();
        foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside))
        {
            if (!Usable(part) || !HasAnyTag(part, BodyPartTagDefOf.MovingLimbCore, BodyPartTagDefOf.MovingLimbSegment,
                    BodyPartTagDefOf.ManipulationLimbCore, BodyPartTagDefOf.ManipulationLimbSegment))
            {
                continue;
            }
            if (survivor && IsLethalToDestroy(pawn, part))
            {
                continue;
            }
            tmpParts.Add(part);
        }
        if (tmpParts.Count == 0)
        {
            return null;
        }
        return tmpParts.RandomElementByWeight(p =>
        {
            bool core = p.def.tags.Contains(BodyPartTagDefOf.MovingLimbCore) || p.def.tags.Contains(BodyPartTagDefOf.ManipulationLimbCore);
            return core && survivor ? 0.6f : 1f;
        });
    }

    // Soft internal organs (kidneys, lungs, liver, stomach, heart...). Bones and the brain are excluded
    // unless a lethal hit is allowed.
    public static BodyPartRecord RandomOrgan(Pawn pawn, bool allowLethal)
    {
        HediffSet set = pawn.health.hediffSet;
        tmpParts.Clear();
        foreach (BodyPartRecord part in set.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Inside))
        {
            if (!Usable(part) || part.def.IsSolid(part, set.hediffs))
            {
                continue;
            }
            if (part.def.tags != null && part.def.tags.Contains(BodyPartTagDefOf.ConsciousnessSource))
            {
                continue;
            }
            if (!allowLethal && IsLethalToDestroy(pawn, part))
            {
                continue;
            }
            tmpParts.Add(part);
        }
        return tmpParts.Count == 0 ? null : tmpParts.RandomElementByWeight(p => Mathf.Max(0.05f, p.coverageAbs));
    }

    // Ribs, pelvis, femurs and the like: crush damage on these becomes fractures.
    public static BodyPartRecord RandomBone(Pawn pawn)
    {
        HediffSet set = pawn.health.hediffSet;
        tmpParts.Clear();
        foreach (BodyPartRecord part in set.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Inside))
        {
            if (Usable(part) && part.def.IsSolid(part, set.hediffs))
            {
                tmpParts.Add(part);
            }
        }
        return tmpParts.Count == 0 ? null : tmpParts.RandomElementByWeight(p => Mathf.Max(0.05f, p.coverageAbs));
    }

    // The torso plus every outer part whose loss is fatal (for humans: torso, neck, head).
    public static List<BodyPartRecord> LethalRegions(Pawn pawn)
    {
        List<BodyPartRecord> regions = new List<BodyPartRecord>();
        BodyPartRecord core = pawn.RaceProps.body.corePart;
        if (core != null && !pawn.health.hediffSet.PartIsMissing(core))
        {
            regions.Add(core);
        }
        foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside))
        {
            if (part != core && Usable(part) && IsLethalToDestroy(pawn, part))
            {
                regions.Add(part);
            }
        }
        return regions;
    }

    // Average protection over the vital regions, used to scale the death chance when armor is in play.
    public static float VitalProtection(Pawn pawn)
    {
        if (!CollapseArmorUtility.ArmorApplies)
        {
            return 0f;
        }
        List<BodyPartRecord> regions = LethalRegions(pawn);
        if (regions.Count == 0)
        {
            return 0f;
        }
        float sum = 0f;
        for (int i = 0; i < regions.Count; i++)
        {
            sum += CollapseArmorUtility.Mitigation(pawn, regions[i]);
        }
        return sum / regions.Count;
    }

    private static BodyPartRecord FirstNotMissingWithTag(Pawn pawn, BodyPartTagDef tag)
    {
        foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Undefined, tag))
        {
            return part;
        }
        return null;
    }
}
