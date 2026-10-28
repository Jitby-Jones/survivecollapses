using Verse;

namespace SurviveCollapses;

public class SurviveCollapsesSettings : ModSettings
{
    public const float DefaultRoofDeathChance = 0.20f;
    public const float DefaultMountainDeathChance = 0.45f;
    public const float DefaultInjurySeverity = 1f;
    public const float DefaultBuriedBleedFactor = 0.35f;
    public const float DefaultImmunityThresholdScale = 1f;

    // Only collapses that bury the cell under rubble (and so obliterate pawns in vanilla) are changed.
    // Vanilla: overhead mountain. This is for any other rubble-leaving roof, e.g. from mods.
    public float roofDeathChance = DefaultRoofDeathChance;

    // Chance that a pawn caught under a collapsing overhead mountain is killed.
    public float mountainDeathChance = DefaultMountainDeathChance;

    // Scales the amount of crush damage and the odds of extra crushed limbs/organs.
    public float injurySeverity = DefaultInjurySeverity;

    // Bleeding multiplier while pinned (the weight of the rubble compresses wounds).
    public float buriedBleedFactor = DefaultBuriedBleedFactor;

    // Armor only matters when Combat Extended is loaded, unless the player opts in for vanilla.
    public bool armorWithCombatExtended = true;
    public bool armorWithoutCombatExtended = false;

    // Scales the armor value at which a body part becomes immune (default: masterwork cataphract).
    public float immunityThresholdScale = DefaultImmunityThresholdScale;

    public bool damageApparel = true;
    public bool affectNonPlayerPawns = true;
    public bool preserveCorpses = true;
    public bool autoDesignateDigging = true;
    public bool sendLetters = true;
    public bool buriedAliveThought = true;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref roofDeathChance, "roofDeathChance", DefaultRoofDeathChance);
        Scribe_Values.Look(ref mountainDeathChance, "mountainDeathChance", DefaultMountainDeathChance);
        Scribe_Values.Look(ref injurySeverity, "injurySeverity", DefaultInjurySeverity);
        Scribe_Values.Look(ref buriedBleedFactor, "buriedBleedFactor", DefaultBuriedBleedFactor);
        Scribe_Values.Look(ref armorWithCombatExtended, "armorWithCombatExtended", true);
        Scribe_Values.Look(ref armorWithoutCombatExtended, "armorWithoutCombatExtended", false);
        Scribe_Values.Look(ref immunityThresholdScale, "immunityThresholdScale", DefaultImmunityThresholdScale);
        Scribe_Values.Look(ref damageApparel, "damageApparel", true);
        Scribe_Values.Look(ref affectNonPlayerPawns, "affectNonPlayerPawns", true);
        Scribe_Values.Look(ref preserveCorpses, "preserveCorpses", true);
        Scribe_Values.Look(ref autoDesignateDigging, "autoDesignateDigging", true);
        Scribe_Values.Look(ref sendLetters, "sendLetters", true);
        Scribe_Values.Look(ref buriedAliveThought, "buriedAliveThought", true);
    }

    public void ResetToDefaults()
    {
        roofDeathChance = DefaultRoofDeathChance;
        mountainDeathChance = DefaultMountainDeathChance;
        injurySeverity = DefaultInjurySeverity;
        buriedBleedFactor = DefaultBuriedBleedFactor;
        armorWithCombatExtended = true;
        armorWithoutCombatExtended = false;
        immunityThresholdScale = DefaultImmunityThresholdScale;
        damageApparel = true;
        affectNonPlayerPawns = true;
        preserveCorpses = true;
        autoDesignateDigging = true;
        sendLetters = true;
        buriedAliveThought = true;
    }
}
