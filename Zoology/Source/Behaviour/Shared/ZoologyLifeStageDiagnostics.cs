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
                AppendCeArmorDurability(pawn, output);

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
                    output.AppendLine();
                }
            }

            Log.Message(output.ToString());
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
