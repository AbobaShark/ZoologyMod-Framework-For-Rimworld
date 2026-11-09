using System;
using System.Text;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Available only through RimWorld's own development-mode action menu.
    // No tick hooks, no persistent components and no runtime stat recalculation.
    public static class ZoologyLifeStageDiagnostics
    {
        private static readonly string[] InspectStats =
        {
            "MoveSpeed", "FilthRate", "MeleeDodgeChance", "MeleeCritChance",
            "MeleeParryChance", "ArmorRating_Sharp", "ArmorRating_Blunt",
            "MarketValue"
        };

        [DebugAction("Zoology", "Inspect animal life-stage profile",
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Inspect(Pawn pawn)
        {
            if (pawn?.RaceProps?.Animal != true)
            {
                Log.Warning("[Zoology] Select an animal pawn to inspect its life-stage profile.");
                return;
            }

            var stage = pawn.ageTracker?.CurLifeStage;
            if (stage == null)
            {
                Log.Warning("[Zoology] Selected animal has no current LifeStageDef.");
                return;
            }

            var origin = stage.GetModExtension<ZoologyLifeStageOrigin>();
            var source = origin?.sourceStage ?? stage;
            var output = new StringBuilder(2048);
            output.Append("[Zoology] Life-stage inspection: ")
                .Append(pawn.def.defName)
                .Append(" (").Append(pawn.LabelShortCap).AppendLine(")");
            output.Append("Original stage: ").Append(source.defName)
                .Append("; actual stage: ").Append(stage.defName)
                .Append("; specialized: ").Append(origin != null).AppendLine();
            output.Append("BaseBodySize: ").Append(pawn.RaceProps.baseBodySize)
                .Append("; stage bodySizeFactor: ").Append(stage.bodySizeFactor)
                .Append("; effective pawn BodySize: ").Append(pawn.BodySize)
                .Append("; healthScaleFactor: ").Append(stage.healthScaleFactor)
                .Append("; foodMaxFactor: ").Append(stage.foodMaxFactor)
                .Append("; hungerRateFactor: ").Append(stage.hungerRateFactor)
                .Append("; marketValueFactor: ").Append(stage.marketValueFactor)
                .AppendLine();
            output.Append("Biological age ticks: ")
                .Append(pawn.ageTracker.AgeBiologicalTicks)
                .Append("; biological day: ")
                .Append(pawn.ageTracker.AgeBiologicalTicks / GenDate.TicksPerDay)
                .AppendLine();
            output.Append("Growth Harmony hooks active: BodySize=")
                .Append(HasRuntimePostfix(AccessTools.PropertyGetter(typeof(Pawn), "BodySize")))
                .Append("; HealthScale=")
                .Append(HasRuntimePostfix(AccessTools.PropertyGetter(typeof(Pawn), "HealthScale")))
                .Append("; render scale=")
                .Append(HasRuntimePostfix(AccessTools.Method(typeof(PawnRenderNodeWorker),
                    "ScaleFor", new[] { typeof(PawnRenderNode), typeof(PawnDrawParms) })))
                .AppendLine();
            float growthProgress;
            if (ZoologyAnimalGrowth.TryGetProgress(pawn, out growthProgress))
            {
                var drawScale = ZoologyAnimalGrowth.DrawRatio(pawn);
                output.Append("Daily growth interval: ")
                    .Append((growthProgress * 100f).ToString("0.#"))
                    .Append("%; BodySize ratio x").Append(ZoologyAnimalGrowth.BodyRatio(pawn).ToString("0.####"))
                    .Append("; HealthScale ratio x").Append(ZoologyAnimalGrowth.HealthRatio(pawn).ToString("0.####"))
                    .Append("; graphic root scale (")
                    .Append(drawScale.x.ToString("0.####")).Append(", ")
                    .Append(drawScale.y.ToString("0.####")).Append(")")
                    .AppendLine();
            }
            else
                output.Append("Daily growth: inactive: ")
                    .Append(ZoologyAnimalGrowth.GrowthUnavailableReason(pawn)
                        ?? "unknown reason")
                    .AppendLine();

            // Explicit developer inspection also reports the alpha
            // calibration used by the actual PawnKind graphic transition.
            // This may populate the per-kind texture cache once, but never
            // runs on ordinary ticks or during rendering for adults.
            ZoologyAnimalGraphicBounds.AppendDiagnostics(
                pawn.kindDef, pawn.ageTracker.CurLifeStageIndex,
                pawn.gender == Gender.Female,
                ZoologyAnimalGrowth.RawDrawRatio(pawn),
                ZoologyAnimalGrowth.GraphicProgress(pawn), output);

            output.Append("Effective CombatPower: ")
                .Append(AnimalCombatPowerUtility.GetAdjustedCombatPower(pawn))
                .AppendLine();

            for (int i = 0; i < InspectStats.Length; i++)
            {
                string name = InspectStats[i];
                var stat = DefDatabase<StatDef>.GetNamedSilentFail(name);
                if (stat == null)
                {
                    continue;
                }
                try
                {
                    output.Append("  ").Append(name).Append(" = ")
                        .Append(pawn.GetStatValue(stat)).AppendLine();
                }
                catch (Exception ex)
                {
                    output.Append("  ").Append(name).Append(" unavailable: ")
                        .Append(ex.GetType().Name).AppendLine();
                }
            }

            if (stage.statFactors != null)
            {
                output.AppendLine("LifeStageDef stat factors:");
                for (int i = 0; i < stage.statFactors.Count; i++)
                {
                    var modifier = stage.statFactors[i];
                    if (modifier?.stat == null)
                    {
                        continue;
                    }
                    output.Append("  ").Append(modifier.stat.defName)
                        .Append(" x").Append(modifier.value).AppendLine();
                }
            }

            if (CEChecker.IsCEInstalled())
            {
                AppendCeArmorDurability(pawn, output);
                AppendCePenetrationHookStatus(output);
            }

            output.Append("Natural attacks (CE active: ")
                .Append(CEChecker.IsCEInstalled()).AppendLine("):");
            var tools = pawn.def.tools;
            if (tools == null)
            {
                output.AppendLine("  none");
            }
            else
            {
                for (int i = 0; i < tools.Count; i++)
                {
                    var tool = tools[i];
                    if (tool == null)
                    {
                        continue;
                    }
                    output.Append("  [").Append(i).Append("] ")
                        .Append(tool.label).Append(" / ")
                        .Append(tool.linkedBodyPartsGroup?.defName);
                    ZoologyLifeStageToolFactor factors;
                    if (ZoologyLifeStageProfileInstaller.TryGetToolFactor(pawn, tool, out factors))
                    {
                        output.Append(" :: Vanilla dmg x").Append(factors.vanillaDamageFactor)
                            .Append(", cooldown x").Append(factors.vanillaCooldownFactor)
                            .Append("; CE dmg x").Append(factors.ceDamageFactor)
                            .Append(", cooldown x").Append(factors.ceCooldownFactor)
                            .Append(", sharp AP x").Append(factors.ceSharpPenetrationFactor)
                            .Append(", blunt AP x").Append(factors.ceBluntPenetrationFactor);
                    }
                    else
                    {
                        output.Append(" :: no individual stage modifier");
                    }
                    AppendLiveMeleeValues(pawn, tool, output);
                    output.AppendLine();
                }
            }

            Log.Message(output.ToString());
        }

        // Invoked only when the player explicitly inspects a pawn.
        private static bool HasRuntimePostfix(System.Reflection.MethodBase target)
        {
            var info = target == null ? null : Harmony.GetPatchInfo(target);
            if (info == null)
                return false;

            foreach (var patch in info.Postfixes)
            {
                if (patch.owner == "com.abobashark.zoology.bionic")
                    return true;
            }
            return false;
        }

        // Exercises the same VerbProperties overloads patched by Zoology.
        // This is called only from a manual debug action, never during ticks.
        private static void AppendLiveMeleeValues(Pawn pawn, Tool tool, StringBuilder output)
        {
            if (tool?.capacities == null)
                return;

            var maneuvers = DefDatabase<ManeuverDef>.AllDefsListForReading;
            for (int i = 0; i < maneuvers.Count; i++)
            {
                var maneuver = maneuvers[i];
                if (maneuver?.verb == null || maneuver.requiredCapacity == null
                    || !tool.capacities.Contains(maneuver.requiredCapacity))
                    continue;

                try
                {
                    float damage = maneuver.verb.AdjustedMeleeDamageAmount(
                        tool, pawn, null, null);
                    float cooldown = maneuver.verb.AdjustedCooldown(tool, pawn, null);
                    output.Append(" :: live damage ").Append(damage.ToString("0.###"))
                        .Append(", live cooldown ").Append(cooldown.ToString("0.###"))
                        .Append(" s");
                }
                catch (Exception ex)
                {
                    output.Append(" :: live melee unavailable: ")
                        .Append(ex.GetType().Name);
                }
                break;
            }
        }

        // Verifies the actual combat path, not just the CE PawnCombat stat
        // display. The displayed AP uses its own StatWorker and a distinct patch.
        private static void AppendCePenetrationHookStatus(StringBuilder output)
        {
            var ceVerbType = AccessTools.TypeByName("CombatExtended.Verb_MeleeAttackCE");
            if (ceVerbType == null)
            {
                output.AppendLine("CE combat penetration: verb type unavailable");
                return;
            }

            foreach (var property in new[] { "ArmorPenetrationSharp", "ArmorPenetrationBlunt" })
            {
                var getter = AccessTools.PropertyGetter(ceVerbType, property);
                var patchInfo = getter == null ? null : Harmony.GetPatchInfo(getter);
                bool zoologyActive = false;
                if (patchInfo != null)
                {
                    foreach (var postfix in patchInfo.Postfixes)
                    {
                        if (postfix.owner == "com.abobashark.zoology.mod.melee")
                        {
                            zoologyActive = true;
                            break;
                        }
                    }
                }
                output.Append("CE combat ").Append(property)
                    .Append(" Zoology postfix active: ").Append(zoologyActive).AppendLine();
            }
        }

        // Debug-only inspection of CE's live comp; no assembly reference or
        // ongoing pawn scan. Calling maxDurability invokes our life-stage patch.
        private static void AppendCeArmorDurability(Pawn pawn, StringBuilder output)
        {
            var ceCompType = AccessTools.TypeByName("CombatExtended.CompArmorDurability");
            var comps = pawn.AllComps;
            if (ceCompType == null || comps == null)
                return;

            for (int i = 0; i < comps.Count; i++)
            {
                var comp = comps[i];
                if (comp == null || !ceCompType.IsInstanceOfType(comp))
                    continue;

                try
                {
                    var maxGetter = AccessTools.PropertyGetter(ceCompType, "maxDurability");
                    var currentField = AccessTools.Field(ceCompType, "curDurability");
                    if (maxGetter == null || currentField == null)
                        return;

                    float max = (float)maxGetter.Invoke(comp, null);
                    float current = (float)currentField.GetValue(comp);
                    float stageFactor;
                    bool mapped = ZoologyLifeStageProfileInstaller.TryGetArmorDurabilityFactor(
                        pawn, out stageFactor);
                    output.Append("CE natural armor durability: ")
                        .Append(current).Append(" / ").Append(max)
                        .Append("; AnimalStats stage factor: ")
                        .Append(mapped ? stageFactor.ToString("0.######") : "unmodified")
                        .AppendLine();
                }
                catch (Exception ex)
                {
                    output.Append("CE natural armor durability unavailable: ")
                        .Append(ex.GetType().Name).AppendLine();
                }
            }
        }
    }
}
