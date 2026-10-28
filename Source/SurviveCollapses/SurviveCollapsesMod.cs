using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SurviveCollapses;

public class SurviveCollapsesMod : Mod
{
    public const string HarmonyId = "JitbyJones.SurviveCollapses";

    public static SurviveCollapsesSettings Settings { get; private set; }

    private static Vector2 settingsScroll;
    private static float settingsViewHeight = 900f;

    public SurviveCollapsesMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<SurviveCollapsesSettings>();
        HarmonyPatches.Apply(new Harmony(HarmonyId));
    }

    public override string SettingsCategory() => "SC_SettingsCategory".Translate();

    public override void WriteSettings()
    {
        base.WriteSettings();
        SurviveCollapsesStartup.ApplySettingsToDefs();
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        SurviveCollapsesSettings s = Settings;
        Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, settingsViewHeight);
        Widgets.BeginScrollView(inRect, ref settingsScroll, viewRect);

        Listing_Standard l = new Listing_Standard();
        l.Begin(viewRect);

        Header(l, "SC_Settings_HeaderOutcome");
        s.mountainDeathChance = Percent(l, "SC_Settings_MountainDeathChance", s.mountainDeathChance, 0f, 1f);
        s.roofDeathChance = Percent(l, "SC_Settings_RoofDeathChance", s.roofDeathChance, 0f, 1f);
        s.injurySeverity = Percent(l, "SC_Settings_InjurySeverity", s.injurySeverity, 0.25f, 2f);
        s.buriedBleedFactor = Percent(l, "SC_Settings_BuriedBleedFactor", s.buriedBleedFactor, 0f, 1f);
        l.CheckboxLabeled("SC_Settings_AffectNonPlayerPawns".Translate(), ref s.affectNonPlayerPawns, "SC_Settings_AffectNonPlayerPawns_Desc".Translate());
        l.CheckboxLabeled("SC_Settings_PreserveCorpses".Translate(), ref s.preserveCorpses, "SC_Settings_PreserveCorpses_Desc".Translate());

        Header(l, "SC_Settings_HeaderArmor");
        string ceStatus = CollapseArmorUtility.CombatExtendedActive
            ? "SC_Settings_CEDetected".Translate()
            : "SC_Settings_CENotDetected".Translate();
        l.Label(ceStatus);
        l.CheckboxLabeled("SC_Settings_ArmorWithCE".Translate(), ref s.armorWithCombatExtended, "SC_Settings_ArmorWithCE_Desc".Translate());
        l.CheckboxLabeled("SC_Settings_ArmorWithoutCE".Translate(), ref s.armorWithoutCombatExtended, "SC_Settings_ArmorWithoutCE_Desc".Translate());
        s.immunityThresholdScale = Percent(l, "SC_Settings_ImmunityScale", s.immunityThresholdScale, 0.25f, 3f);
        l.Label("SC_Settings_ImmunityThreshold".Translate(
            CollapseArmorUtility.FormatArmor(CollapseArmorUtility.ImmunityThreshold),
            CollapseArmorUtility.ThresholdSourceLabel));
        l.Label(CollapseArmorUtility.ArmorApplies
            ? "SC_Settings_ArmorActive".Translate()
            : "SC_Settings_ArmorInactive".Translate());
        l.CheckboxLabeled("SC_Settings_DamageApparel".Translate(), ref s.damageApparel, "SC_Settings_DamageApparel_Desc".Translate());

        Header(l, "SC_Settings_HeaderRescue");
        l.CheckboxLabeled("SC_Settings_AutoDesignate".Translate(), ref s.autoDesignateDigging, "SC_Settings_AutoDesignate_Desc".Translate());
        l.CheckboxLabeled("SC_Settings_SendLetters".Translate(), ref s.sendLetters, "SC_Settings_SendLetters_Desc".Translate());
        l.CheckboxLabeled("SC_Settings_BuriedAliveThought".Translate(), ref s.buriedAliveThought, "SC_Settings_BuriedAliveThought_Desc".Translate());

        l.Gap();
        if (l.ButtonText("SC_Settings_Reset".Translate()))
        {
            s.ResetToDefaults();
        }

        settingsViewHeight = Mathf.Max(400f, l.CurHeight + 12f);
        l.End();
        Widgets.EndScrollView();
    }

    private static void Header(Listing_Standard l, string key)
    {
        l.Gap(10f);
        Text.Font = GameFont.Medium;
        l.Label(key.Translate());
        Text.Font = GameFont.Small;
        l.GapLine(6f);
    }

    private static float Percent(Listing_Standard l, string key, float value, float min, float max)
    {
        float result = l.SliderLabeled((key).Translate(value.ToStringPercent()), value, min, max, 0.6f, (key + "_Desc").Translate());
        return Mathf.Round(result * 100f) / 100f;
    }
}
