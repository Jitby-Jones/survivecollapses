using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace SurviveCollapses;

internal static class HarmonyPatches
{
    public static void Apply(Harmony harmony)
    {
        MethodInfo phaseOne = AccessTools.Method(typeof(RoofCollapserImmediate), "DropRoofInCellPhaseOne",
            new[] { typeof(IntVec3), typeof(Map), typeof(List<Thing>) });
        if (phaseOne == null)
        {
            Log.Error("[Survive Collapses] Could not find RoofCollapserImmediate.DropRoofInCellPhaseOne. Collapses will behave as in vanilla.");
            return;
        }

        harmony.Patch(phaseOne,
            prefix: Patch(nameof(PhaseOne_Prefix)),
            finalizer: Patch(nameof(PhaseOne_Finalizer)));

        // Safety nets for pawns that land on the cell while vanilla is crushing it.
        harmony.Patch(AccessTools.Method(typeof(Thing), nameof(Thing.TakeDamage)), prefix: Patch(nameof(TakeDamage_Prefix), Priority.First));
        harmony.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill)), prefix: Patch(nameof(Kill_Prefix), Priority.First));
        harmony.Patch(AccessTools.Method(typeof(Corpse), nameof(Corpse.Kill)), prefix: Patch(nameof(Kill_Prefix), Priority.First));

        // Colonists buried alive are still on the map: don't let the game end (or a map close) over them.
        try
        {
            MethodInfo colonistCount = AccessTools.PropertyGetter(typeof(MapPawns), nameof(MapPawns.FreeColonistsSpawnedOrInPlayerEjectablePodsCount));
            mapPawnsMap = AccessTools.FieldRefAccess<MapPawns, Map>("map");
            if (colonistCount != null && mapPawnsMap != null)
            {
                harmony.Patch(colonistCount, postfix: Patch(nameof(FreeColonistsCount_Postfix)));
            }
        }
        catch (Exception e)
        {
            Log.Warning("[Survive Collapses] Could not make buried colonists count as present; the game may end if every colonist is buried alive. " + e.Message);
        }
    }

    private static AccessTools.FieldRef<MapPawns, Map> mapPawnsMap;

    private static void FreeColonistsCount_Postfix(MapPawns __instance, ref int __result)
    {
        if (__result > 0 || mapPawnsMap == null)
        {
            return;
        }
        __result += MapComponent_BuriedTracker.CountBuriedLivingFreeColonists(mapPawnsMap(__instance));
    }

    private static HarmonyMethod Patch(string name, int priority = -1)
    {
        HarmonyMethod method = new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), name));
        if (priority >= 0)
        {
            method.priority = priority;
        }
        return method;
    }

    private static void PhaseOne_Prefix(IntVec3 c, Map map)
    {
        RoofDef roof = map?.roofGrid.RoofAt(c);
        CollapseKind kind = RubbleRegistry.KindFor(roof);
        CollapseContext.Push(c, map, roof, kind);
        if (kind == CollapseKind.None)
        {
            return;
        }
        try
        {
            CollapseContext.ExtractInitialOccupants(CollapseContext.Current);
        }
        catch (Exception e)
        {
            Log.Error("[Survive Collapses] Error pulling pawns out of a collapsing cell: " + e);
        }
    }

    private static Exception PhaseOne_Finalizer(Exception __exception, List<Thing> outCrushedThings)
    {
        CollapseFrame frame = CollapseContext.Pop();
        if (frame != null && frame.active)
        {
            try
            {
                BurialUtility.BuryExtracted(frame, outCrushedThings);
            }
            catch (Exception e)
            {
                Log.Error("[Survive Collapses] Error burying pawns under collapsed rubble: " + e);
                BurialUtility.EmergencyRelease(frame);
            }
        }
        return __exception;
    }

    private static bool TakeDamage_Prefix(Thing __instance, DamageInfo dinfo, ref DamageWorker.DamageResult __result)
    {
        if (!CollapseContext.AnyActive || dinfo.Category != DamageInfo.SourceCategory.Collapse)
        {
            return true;
        }
        if (CollapseContext.IsHandled(__instance) || CollapseContext.TryIntercept(__instance))
        {
            __result = new DamageWorker.DamageResult();
            return false;
        }
        return true;
    }

    private static bool Kill_Prefix(Thing __instance, DamageInfo? dinfo)
    {
        if (!CollapseContext.AnyActive)
        {
            return true;
        }
        if (CollapseContext.IsHandled(__instance))
        {
            return false;
        }
        if (dinfo.HasValue && dinfo.Value.Category == DamageInfo.SourceCategory.Collapse && CollapseContext.TryIntercept(__instance))
        {
            return false;
        }
        return true;
    }
}
