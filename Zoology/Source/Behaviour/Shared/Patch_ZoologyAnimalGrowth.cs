using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(Pawn), "get_BodySize")]
    internal static class Patch_ZoologyDailyBodySize
    {
        private static bool Prepare() => (ZoologyAnimalGrowth.Enabled || ZoologySexAdultProfiles.Enabled);

        private static void Postfix(Pawn __instance, ref float __result)
        {
            // Preserve vanilla, gene, hediff and other-mod modifiers on the
            // original result. Only replace the already applied stage factor.
            if (__instance?.RaceProps?.Animal == true)
                __result *= ZoologyAnimalGrowth.BodyRatio(__instance);
        }
    }

    [HarmonyPatch(typeof(Pawn), "get_HealthScale")]
    internal static class Patch_ZoologyDailyHealthScale
    {
        private static bool Prepare() => (ZoologyAnimalGrowth.Enabled || ZoologySexAdultProfiles.Enabled);

        private static void Postfix(Pawn __instance, ref float __result)
        {
            if (__instance?.RaceProps?.Animal == true)
                __result *= ZoologyAnimalGrowth.HealthRatio(__instance);
        }
    }

    // Adjust basal metabolic expenditure before RimWorld adds lactation,
    // applies gene metabolism, and handles holding-platform suspension.
    [HarmonyPatch]
    internal static class Patch_ZoologyDailyHungerRate
    {
        private static readonly AccessTools.FieldRef<Need, Pawn> NeedPawnRef =
            AccessTools.FieldRefAccess<Need, Pawn>("pawn");

        private static readonly MethodInfo BaseHungerMethod =
            AccessTools.Method(typeof(Need_Food), nameof(Need_Food.BaseHungerRate),
                new[] { typeof(LifeStageDef), typeof(ThingDef) });
        private static readonly MethodInfo ScaleBaseMethod =
            AccessTools.Method(typeof(Patch_ZoologyDailyHungerRate),
                nameof(ScaleBaseHunger));

        private static bool Prepare()
        {
            return AccessTools.Method(typeof(Need_Food),
                    nameof(Need_Food.FoodFallPerTickAssumingCategory),
                    new[] { typeof(HungerCategory), typeof(bool) }) != null
                && BaseHungerMethod != null && ScaleBaseMethod != null;
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Need_Food),
                nameof(Need_Food.FoodFallPerTickAssumingCategory),
                new[] { typeof(HungerCategory), typeof(bool) });
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            int match = -1;
            for (int i = 0; i < code.Count; i++)
            {
                if (!code[i].Calls(BaseHungerMethod))
                    continue;
                if (match >= 0)
                {
                    Log.Error("[Zoology] Multiple BaseHungerRate calls in "
                        + "FoodFallPerTickAssumingCategory; retaining vanilla calculation.");
                    return code;
                }
                match = i;
            }

            if (match < 0)
            {
                Log.Error("[Zoology] BaseHungerRate call not found in "
                    + "FoodFallPerTickAssumingCategory; retaining vanilla calculation.");
                return code;
            }

            // The original call leaves the basal float on the IL stack.
            // Insert a float-to-float adjustment before vanilla multiplies
            // that rate and adds the independent lactation expenditure.
            code.Insert(match + 1, new CodeInstruction(OpCodes.Ldarg_0));
            code.Insert(match + 2, new CodeInstruction(OpCodes.Call, ScaleBaseMethod));
            return code;
        }

        private static float ScaleBaseHunger(float baseHunger, Need_Food need)
        {
            if (need == null)
                return baseHunger;
            Pawn pawn = NeedPawnRef(need);
            if (pawn?.RaceProps?.Animal != true)
                return baseHunger;
            float growth = ZoologyAnimalGrowth.Enabled || ZoologySexAdultProfiles.Enabled
                ? ZoologyAnimalGrowth.HungerRatio(pawn) : 1f;
            return baseHunger * growth * AnimalWinterDormancy.MaintenanceHungerFactor(pawn);
        }

    }

    // RimWorld's nutrition tooltip calculates the baseline from the static
    // LifeStageDef. Explain the runtime modifier that is already included in
    // its displayed final food-consumption value.
    [HarmonyPatch(typeof(RaceProperties),
        nameof(RaceProperties.NutritionEatenPerDayExplanation),
        new Type[] { typeof(Pawn), typeof(bool), typeof(bool), typeof(bool) })]
    internal static class Patch_ZoologyHungerExplanation
    {
        private static void Postfix(Pawn p, bool showCalculations, ref string __result)
        {
            if (!showCalculations || p?.RaceProps?.Animal != true
                || !(ZoologyAnimalGrowth.Enabled || ZoologySexAdultProfiles.Enabled))
                return;

            float factor = ZoologyAnimalGrowth.HungerRatio(p);
            if (Math.Abs(factor - 1f) < 0.000001f)
                return;
            __result += "\n" + "Zoology_HungerRateGrowthFactor".Translate(
                factor.ToString("0.####"));
        }
    }

    // StatsReportUtility caches special stat rows while an information card
    // remains open. Refresh only an observed growing animal on its next
    // biological day; no tick callback, map scan or work on closed cards.
    [HarmonyPatch(typeof(StatsReportUtility), nameof(StatsReportUtility.DrawStatsReport),
        new Type[] { typeof(Rect), typeof(Thing) })]
    internal static class Patch_ZoologyGrowingAnimalStatsCard
    {
        private static System.WeakReference<Pawn> observed;
        private static long observedDay = -1;
        private static int observedStage = -1;

        private static bool Prepare() => ZoologyAnimalGrowth.Enabled;

        private static void Prefix(Thing thing)
        {
            Pawn pawn = thing as Pawn;
            if (pawn?.RaceProps?.Animal != true || !ZoologyAnimalGrowth.Enabled
                || pawn.ageTracker == null)
            {
                observed = null;
                return;
            }

            int stage = pawn.ageTracker.CurLifeStageIndex;
            long day = pawn.ageTracker.AgeBiologicalTicks / GenDate.TicksPerDay;
            Pawn previous;
            if (observed != null && observed.TryGetTarget(out previous)
                && ReferenceEquals(previous, pawn))
            {
                // The last stage has no continuous growth. A transition
                // INTO it still needs a final cache refresh.
                if (observedStage != stage || (observedDay != day
                    && stage < pawn.ageTracker.MaxRaceLifeStageIndex))
                    StatsReportUtility.Notify_QuickSearchChanged();
                observedDay = day;
                observedStage = stage;
                return;
            }

            observed = new System.WeakReference<Pawn>(pawn);
            observedDay = day;
            observedStage = stage;
        }
    }

    // Modify the root render transform exactly once, not each child mesh.
    // All descendant body parts inherit this scale and no graphics are rebuilt.
    [HarmonyPatch(typeof(PawnRenderNodeWorker), "ScaleFor",
        new Type[] { typeof(PawnRenderNode), typeof(PawnDrawParms) })]
    internal static class Patch_ZoologyDailyAnimalRenderScale
    {
        private static bool Prepare() => ZoologyAnimalGrowth.Enabled;

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var code = new List<CodeInstruction>(instructions);
            FieldInfo parent = AccessTools.Field(typeof(PawnRenderNode), "parent");
            MethodInfo scale = AccessTools.Method(typeof(Patch_ZoologyDailyAnimalRenderScale),
                nameof(ScaleRoot));
            if (parent == null || scale == null || !code.Exists(i => i.opcode == OpCodes.Ret)
                || code.Exists(i => i.opcode == OpCodes.Ret && i.blocks.Count != 0))
            {
                Log.Error("[Zoology] Cannot install the root render scale transform.");
                return code;
            }

            var result = new List<CodeInstruction>(code.Count + 12);
            foreach (CodeInstruction instruction in code)
            {
                if (instruction.opcode == OpCodes.Ret)
                {
                    // Keep the original Vector3 on the evaluation stack.
                    // Descendants return directly, without any managed callback.
                    Label done = il.DefineLabel();
                    var first = new CodeInstruction(OpCodes.Ldarg_1);
                    first.labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                    instruction.labels.Add(done);
                    result.Add(first);
                    result.Add(new CodeInstruction(OpCodes.Brfalse, done));
                    result.Add(new CodeInstruction(OpCodes.Ldarg_1));
                    result.Add(new CodeInstruction(OpCodes.Ldfld, parent));
                    result.Add(new CodeInstruction(OpCodes.Brtrue, done));
                    result.Add(new CodeInstruction(OpCodes.Ldarg_1));
                    result.Add(new CodeInstruction(OpCodes.Ldarg_2));
                    result.Add(new CodeInstruction(OpCodes.Call, scale));
                }
                result.Add(instruction);
            }
            return result;
        }

        private static Vector3 ScaleRoot(Vector3 result, PawnRenderNode node, PawnDrawParms parms)
        {
            if (!ReferenceEquals(node.tree?.rootNode, node)) return result;
            Pawn pawn = parms.pawn;
            if (pawn?.RaceProps?.Animal != true) return result;
            Vector2 scale = ZoologyAnimalGrowth.DrawRatio(pawn);
            result.x *= scale.x;
            result.z *= scale.y;
            return result;
        }
    }
}
