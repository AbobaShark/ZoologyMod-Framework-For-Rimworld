using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    internal static class LargeMammalPredationUtility
    {
        private static readonly Func<Pawn, Pawn, bool> IsPreyProtectedFromPredatorByFenceFunc = CreateFenceProtectionDelegate();

        private const float LargeMammalPredatorMinBodySize = 0.5f;
        private const float PreferredPreyRatioMin = 0.5f;
        private const float PreferredPreyRatioMax = 1.5f;
        private const float TinyPreyPenaltyScale = 1.2f;
        private const float OversizedPreyPenaltyScale = 0.15f;
        private const float MinScoreFactor = 0.05f;
        private const float MinimumUndersizedFit = 0.1f;
        private const float OversizedSelectionFalloff = 2.4f;
        private const float PreferredSizeScore = 38f;
        private const float CombatEaseScore = 18f;
        private const float VulnerabilityScore = 16f;
        private const float PredatorPreyNeutralSizeFit = 0.45f;

        public static bool UsesLargeMammalSizing(Pawn predator)
        {
            return predator != null
                && predator.RaceProps?.predator == true
                && predator.BodySize >= LargeMammalPredatorMinBodySize
                && (predator.IsMammal() || ZoologyCacheUtility.IsPhotonozoa(predator.def));
        }

        public static float GetThreatSizeFactor(Pawn predator, Pawn prey)
        {
            float predatorBodySize = predator?.BodySize ?? 0f;
            float preyBodySize = prey?.BodySize ?? 0f;
            if (predatorBodySize <= 0f || preyBodySize <= 0f)
            {
                return preyBodySize > 0f ? preyBodySize : 1f;
            }

            if (prey?.RaceProps?.predator == true)
            {
                return preyBodySize;
            }

            float preyToPredatorRatio = preyBodySize / predatorBodySize;
            if (preyToPredatorRatio < PreferredPreyRatioMin)
            {
                float undersizeDelta = PreferredPreyRatioMin - preyToPredatorRatio;
                return preyBodySize * (1f + undersizeDelta * TinyPreyPenaltyScale);
            }

            if (preyToPredatorRatio <= PreferredPreyRatioMax)
            {
                return predatorBodySize;
            }

            float oversizeDelta = preyToPredatorRatio - PreferredPreyRatioMax;
            float oversizePenaltyPerRatio = OversizedPreyPenaltyScale / Math.Max(predatorBodySize, LargeMammalPredatorMinBodySize);
            return predatorBodySize * (1f + oversizeDelta * oversizePenaltyPerRatio);
        }

        public static float GetPreySelectionScore(Pawn predator, Pawn prey)
        {
            float predatorBodySize = predator?.BodySize ?? 0f;
            float preyBodySize = prey?.BodySize ?? 0f;
            float predatorCombatPower = Math.Max(predator?.kindDef?.combatPower ?? 0f, 0.01f);
            float preyCombatPower = PreyCombatPowerUtility.GetAdjustedCombatPower(prey);
            if (predatorBodySize <= 0f || preyBodySize <= 0f || prey == null)
            {
                return 0f;
            }

            float distance = 0f;
            if (predator.Spawned && prey.Spawned && predator.Map == prey.Map)
            {
                distance = (predator.Position - prey.Position).LengthHorizontal;
            }

            float healthPercent = prey.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
            if (prey.Downed)
            {
                healthPercent = Mathf.Min(healthPercent, 0.2f);
            }

            float sizeFit = prey.RaceProps?.predator == true
                ? GetPredatorPreySizeFit(predatorBodySize, preyBodySize)
                : GetPreySizeFit(predatorBodySize, preyBodySize);
            float combatEase = Mathf.Clamp01(1f - preyCombatPower / predatorCombatPower);
            float vulnerability = 1f - healthPercent * healthPercent;
            float score = PreferredSizeScore * sizeFit
                + CombatEaseScore * sizeFit * combatEase
                + VulnerabilityScore * vulnerability
                - distance;

            if (prey.RaceProps?.Humanlike == true)
            {
                score -= 35f;
            }
            else if (IsPreyProtectedFromPredatorByFence(predator, prey))
            {
                score -= 17f;
            }

            return score;
        }

        private static float GetPredatorPreySizeFit(float predatorBodySize, float preyBodySize)
        {
            float preyToPredatorRatio = preyBodySize / predatorBodySize;
            if (preyToPredatorRatio < PreferredPreyRatioMin)
            {
                float normalized = preyToPredatorRatio / PreferredPreyRatioMin;
                return PredatorPreyNeutralSizeFit * normalized * normalized;
            }

            if (preyToPredatorRatio <= PreferredPreyRatioMax)
            {
                return PredatorPreyNeutralSizeFit;
            }

            float oversizeDelta = preyToPredatorRatio - PreferredPreyRatioMax;
            float result = PredatorPreyNeutralSizeFit / (1f + oversizeDelta * OversizedSelectionFalloff);
            return result < MinScoreFactor ? MinScoreFactor : result;
        }

        private static float GetPreySizeFit(float predatorBodySize, float preyBodySize)
        {
            float preyToPredatorRatio = preyBodySize / predatorBodySize;
            if (preyToPredatorRatio < PreferredPreyRatioMin)
            {
                float normalized = preyToPredatorRatio / PreferredPreyRatioMin;
                return MinimumUndersizedFit + (1f - MinimumUndersizedFit) * normalized * normalized;
            }

            if (preyToPredatorRatio <= PreferredPreyRatioMax)
            {
                return 1f;
            }

            float oversizeDelta = preyToPredatorRatio - PreferredPreyRatioMax;
            float result = 1f / (1f + oversizeDelta * OversizedSelectionFalloff);
            return result < MinScoreFactor ? MinScoreFactor : result;
        }

        private static Func<Pawn, Pawn, bool> CreateFenceProtectionDelegate()
        {
            try
            {
                var method = AccessTools.Method(typeof(FoodUtility), "IsPreyProtectedFromPredatorByFence", new[] { typeof(Pawn), typeof(Pawn) });
                if (method == null)
                {
                    return null;
                }

                return (Func<Pawn, Pawn, bool>)Delegate.CreateDelegate(typeof(Func<Pawn, Pawn, bool>), method);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsPreyProtectedFromPredatorByFence(Pawn predator, Pawn prey)
        {
            if (IsPreyProtectedFromPredatorByFenceFunc == null || predator == null || prey == null)
            {
                return false;
            }

            try
            {
                return IsPreyProtectedFromPredatorByFenceFunc(predator, prey);
            }
            catch
            {
                return false;
            }
        }
    }

    internal static class PreyCombatPowerUtility
    {
        private const float BabyCombatPowerFactor = 0.2f;
        private const float JuvenileCombatPowerFactor = 0.5f;

        private static LifeStageDef animalBabyLifeStageDef;
        private static LifeStageDef animalBabyTinyLifeStageDef;
        private static LifeStageDef eusocialLarvaLifeStageDef;
        private static LifeStageDef animalJuvenileLifeStageDef;
        private static LifeStageDef eusocialJuvenileLifeStageDef;

        public static float GetAdjustedCombatPower(Pawn prey)
        {
            if (prey?.kindDef == null)
            {
                return 0f;
            }

            float basePower = prey.kindDef.combatPower;
            float factor = GetStageFactor(prey);
            if (factor <= 0f || factor == 1f)
            {
                return basePower;
            }

            return basePower * factor;
        }

        private static float GetStageFactor(Pawn pawn)
        {
            if (pawn == null)
            {
                return 1f;
            }

            var stages = pawn.RaceProps?.lifeStageAges;
            if (stages == null || stages.Count <= 1)
            {
                return 1f;
            }

            LifeStageDef stage = pawn.ageTracker?.CurLifeStage;
            if (stage == null)
            {
                return 1f;
            }

            if (IsBabyStage(stage))
            {
                return BabyCombatPowerFactor;
            }

            if (IsJuvenileStage(stage))
            {
                return JuvenileCombatPowerFactor;
            }

            return 1f;
        }

        private static bool IsBabyStage(LifeStageDef stage)
        {
            var cached = animalBabyLifeStageDef ?? (animalBabyLifeStageDef = DefDatabase<LifeStageDef>.GetNamedSilentFail("AnimalBaby"));
            if (stage == cached || string.Equals(stage.defName, "AnimalBaby", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var tiny = animalBabyTinyLifeStageDef ?? (animalBabyTinyLifeStageDef = DefDatabase<LifeStageDef>.GetNamedSilentFail("AnimalBabyTiny"));
            if (stage == tiny || string.Equals(stage.defName, "AnimalBabyTiny", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var larva = eusocialLarvaLifeStageDef ?? (eusocialLarvaLifeStageDef = DefDatabase<LifeStageDef>.GetNamedSilentFail("EusocialInsectLarva"));
            return stage == larva || string.Equals(stage.defName, "EusocialInsectLarva", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsJuvenileStage(LifeStageDef stage)
        {
            var cached = animalJuvenileLifeStageDef ?? (animalJuvenileLifeStageDef = DefDatabase<LifeStageDef>.GetNamedSilentFail("AnimalJuvenile"));
            if (stage == cached || string.Equals(stage.defName, "AnimalJuvenile", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var eusocial = eusocialJuvenileLifeStageDef ?? (eusocialJuvenileLifeStageDef = DefDatabase<LifeStageDef>.GetNamedSilentFail("EusocialInsectJuvenile"));
            return stage == eusocial || string.Equals(stage.defName, "EusocialInsectJuvenile", StringComparison.OrdinalIgnoreCase);
        }
    }

    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.GetPreyScoreFor), new[] { typeof(Pawn), typeof(Pawn) })]
    public static class Patch_GetPreyScoreForPredator
    {
        public static bool Prepare()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnableAdvancedPredationLogic;
        }

        public static void Postfix(Pawn predator, Pawn prey, ref float __result)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && !settings.EnableAdvancedPredationLogic)
                {
                    return;
                }

                if (!LargeMammalPredationUtility.UsesLargeMammalSizing(predator) || prey == null || predator?.kindDef == null || prey.kindDef == null)
                {
                    return;
                }

                if (PredationDecisionCache.TryGetPreyScore(predator, prey, out float cachedScore))
                {
                    __result = cachedScore;
                    return;
                }

                __result = LargeMammalPredationUtility.GetPreySelectionScore(predator, prey);
                PredationDecisionCache.StorePreyScore(predator, prey, __result);
            }
            catch (Exception ex)
            {
                Log.Error($"[Zoology] Patch_GetPreyScoreForPredator error: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(FoodUtility), "IsAcceptablePreyFor", new[] { typeof(Pawn), typeof(Pawn) })]
    public static class Patch_IsAcceptablePreyForPredator
    {
        private const float PackHuntCombatPowerMultiplier = 2f;

        private static bool AreFactionsHostileSafe(Faction sourceFaction, Faction targetFaction)
        {
            if (sourceFaction == null || targetFaction == null || ReferenceEquals(sourceFaction, targetFaction))
            {
                return false;
            }

            FactionRelation relation = sourceFaction.RelationWith(targetFaction, allowNull: true);
            return relation != null && relation.kind == FactionRelationKind.Hostile;
        }

        public static bool Prepare()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnableAdvancedPredationLogic;
        }

        
        
        
        
        static bool IsMammalBaby(Pawn p)
        {
            try
            {
                if (p == null) return false;
                if (!p.IsMammal()) return false;
                if (p.needs?.food == null) return false; 
                return AnimalLactationUtility.IsAnimalBaby(p);
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] IsMammalBaby check failed: {ex}");
                return false;
            }
        }

        internal static bool IsAcceptablePhotonozoaPreyForFlee(Pawn predator, Pawn prey)
        {
            try
            {
                if (predator == null || prey == null)
                {
                    return false;
                }

                if (predator.kindDef == null || prey.kindDef == null)
                {
                    return false;
                }

                if (predator.RaceProps?.predator != true)
                {
                    return false;
                }

                if (!ZoologyCacheUtility.IsPhotonozoa(predator.def) || !ZoologyCacheUtility.IsPhotonozoa(prey.def))
                {
                    return false;
                }

                if (!prey.RaceProps.canBePredatorPrey || !prey.RaceProps.IsFlesh)
                {
                    return false;
                }

                if (prey.BodySize > predator.RaceProps.maxPreyBodySize)
                {
                    return false;
                }

                float preyCombatPowerAdjusted = PreyCombatPowerUtility.GetAdjustedCombatPower(prey);
                float predatorCombatPower = predator.kindDef.combatPower;
                if (prey.RaceProps.predator && !prey.Downed)
                {
                    float requiredPredatorCP = preyCombatPowerAdjusted * (4f / 3f);
                    if (predatorCombatPower < requiredPredatorCP)
                    {
                        return false;
                    }
                }

                if (prey.Downed)
                {
                    return true;
                }

                bool useLargeMammalSizing = LargeMammalPredationUtility.UsesLargeMammalSizing(predator);
                bool packSupport = false;
                bool packSupportChecked = false;
                float packMultiplier = 1f;

                if (useLargeMammalSizing)
                {
                    if (preyCombatPowerAdjusted > predatorCombatPower)
                    {
                        packSupport = HerdPredatorHuntPatch.HasPackSupport(predator, prey);
                        packSupportChecked = true;
                        if (packSupport)
                        {
                            packMultiplier = PackHuntCombatPowerMultiplier;
                        }

                        if (preyCombatPowerAdjusted > predatorCombatPower * packMultiplier)
                        {
                            return false;
                        }
                    }
                }
                else if (preyCombatPowerAdjusted > 2f * predatorCombatPower)
                {
                    return false;
                }

                float preyHealthPercent = prey.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
                float predatorHealthPercent = predator.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
                float preySizeFactor = useLargeMammalSizing
                    ? LargeMammalPredationUtility.GetThreatSizeFactor(predator, prey)
                    : prey.BodySize;
                float preyScore = preyCombatPowerAdjusted * preyHealthPercent * preySizeFactor;
                float predatorScore = predatorCombatPower * predatorHealthPercent * predator.BodySize;
                if (packSupport)
                {
                    predatorScore *= packMultiplier;
                }

                if (preyScore >= predatorScore)
                {
                    if (useLargeMammalSizing && !packSupportChecked)
                    {
                        packSupport = HerdPredatorHuntPatch.HasPackSupport(predator, prey);
                        if (packSupport)
                        {
                            predatorScore *= PackHuntCombatPowerMultiplier;
                        }
                    }

                    if (preyScore >= predatorScore)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool Prefix(Pawn predator, Pawn prey, ref bool __result)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && !settings.EnableAdvancedPredationLogic)
                {
                    return true;
                }

                bool allowCache = !(settings != null && settings.EnablePackHunt
                    && predator?.RaceProps?.herdAnimal == true
                    && predator.RaceProps.predator == true
                    && prey != null
                    && !prey.Downed);

                bool skipCache = !allowCache;
                void StoreAcceptablePrey(bool value)
                {
                    if (!skipCache)
                    {
                        PredationDecisionCache.StoreAcceptablePrey(predator, prey, value);
                    }
                }

                if (allowCache && PredationDecisionCache.TryGetAcceptablePrey(predator, prey, out bool cachedAcceptable))
                {
                    __result = cachedAcceptable;
                    return false;
                }

                if (predator == null || prey == null)
                {
                    __result = false;
                    return false;
                }

                
                if (settings != null && ZoologyModSettings.EnableMammalLactation)
                {
                    if (IsMammalBaby(predator))
                    {
                        __result = false;
                        StoreAcceptablePrey(false);
                        return false; 
                    }
                }

                var comp = ZoologyPursuitGameComponent.Instance;
                if (comp != null && comp.IsPairBlockedNow(predator, prey))
                {
                    __result = false;
                    StoreAcceptablePrey(false);
                    return false;
                }
                
                if (predator.kindDef == null || prey.kindDef == null)
                {
                    __result = false;
                    StoreAcceptablePrey(false);
                    return false;
                }
				
				
				bool predIsPhotonozoa = false;
				bool preyIsPhotonozoa = false;
				try
				{
					predIsPhotonozoa = ZoologyCacheUtility.IsPhotonozoa(predator.def);
					preyIsPhotonozoa = ZoologyCacheUtility.IsPhotonozoa(prey.def);
				}
				catch
				{
					predIsPhotonozoa = false;
					preyIsPhotonozoa = false;
				}

				
				if (preyIsPhotonozoa && !predIsPhotonozoa)
				{
					__result = false;
                    StoreAcceptablePrey(false);
					return false;
				}

				
				bool photonozoaPairInTheirFaction = predIsPhotonozoa
					&& preyIsPhotonozoa
					&& ZoologyCacheUtility.IsPhotonozoaPairInTheirFaction(predator, prey);
				

                bool predatorMammal = predator.IsMammal();
                bool preyMammal = prey.IsMammal();

                bool sameDef = predator.def == prey.def;
                bool inCrossbreedRelation = ZoologyCacheUtility.AreCrossbreedRelated(predator.def, prey.def);

                if ((sameDef || inCrossbreedRelation) && predatorMammal && preyMammal)
                {
                    __result = false;
                    StoreAcceptablePrey(false);
                    return false;
                }

                float preyCombatPowerAdjusted = PreyCombatPowerUtility.GetAdjustedCombatPower(prey);
                float predatorCombatPower = predator.kindDef.combatPower;

                if (prey.RaceProps.predator && !prey.Downed)
                {
                    float requiredPredatorCP = preyCombatPowerAdjusted * (4f / 3f);
                    if (predatorCombatPower < requiredPredatorCP)
                    {
                        __result = false;
                        StoreAcceptablePrey(false);
                        return false;
                    }
                }

                if (!prey.RaceProps.canBePredatorPrey) { __result = false; StoreAcceptablePrey(false); return false; }
                if (!prey.RaceProps.IsFlesh) { __result = false; StoreAcceptablePrey(false); return false; }
                if (!Find.Storyteller.difficulty.predatorsHuntHumanlikes && prey.RaceProps.Humanlike) { __result = false; StoreAcceptablePrey(false); return false; }
                if (prey.BodySize > predator.RaceProps.maxPreyBodySize) {__result = false; StoreAcceptablePrey(false); return false; }

                if (!prey.Downed)
                {
                    bool useLargeMammalSizing = LargeMammalPredationUtility.UsesLargeMammalSizing(predator);
                    float preyCombatPower = preyCombatPowerAdjusted;
                    bool packSupport = false;
                    bool packSupportChecked = false;
                    float packMultiplier = 1f;

                    if (useLargeMammalSizing)
                    {
                        if (preyCombatPower > predatorCombatPower)
                        {
                            packSupport = HerdPredatorHuntPatch.HasPackSupport(predator, prey);
                            packSupportChecked = true;
                            skipCache = true;
                            if (packSupport)
                            {
                                packMultiplier = PackHuntCombatPowerMultiplier;
                            }

                            if (preyCombatPower > predatorCombatPower * packMultiplier)
                            {
                                __result = false;
                                StoreAcceptablePrey(false);
                                return false;
                            }
                        }
                    }
                    else if (preyCombatPower > 2f * predatorCombatPower)
                    {
                        __result = false;
                        StoreAcceptablePrey(false);
                        return false;
                    }

                    float preySizeFactor = useLargeMammalSizing
                        ? LargeMammalPredationUtility.GetThreatSizeFactor(predator, prey)
                        : prey.BodySize;
                    float preyScore = preyCombatPower * prey.health.summaryHealth.SummaryHealthPercent * preySizeFactor;
                    float predatorScore = predator.kindDef.combatPower * predator.health.summaryHealth.SummaryHealthPercent * predator.BodySize;
                    if (packSupport)
                    {
                        predatorScore *= packMultiplier;
                    }

                    if (preyScore >= predatorScore)
                    {
                        if (useLargeMammalSizing && !packSupportChecked)
                        {
                            packSupport = HerdPredatorHuntPatch.HasPackSupport(predator, prey);
                            packSupportChecked = true;
                            skipCache = true;
                            if (packSupport)
                            {
                                predatorScore *= PackHuntCombatPowerMultiplier;
                            }
                        }

                        if (preyScore >= predatorScore)
                        {
                            __result = false;
                            StoreAcceptablePrey(false);
                            return false;
                        }
                    }
                }

				bool hostileToPreyFactionOrMissing = predator.Faction == null
                    || prey.Faction == null
                    || AreFactionsHostileSafe(predator.Faction, prey.Faction);
                bool hostileToHostFactionOrMissing = predator.Faction == null
                    || prey.HostFaction == null
                    || AreFactionsHostileSafe(predator.Faction, prey.HostFaction);

				if (!photonozoaPairInTheirFaction && !(hostileToPreyFactionOrMissing
					&& hostileToHostFactionOrMissing
					&& (predator.Faction != Faction.OfPlayer || prey.Faction != Faction.OfPlayer)
					&& (!predator.RaceProps.herdAnimal || predator.def != prey.def)
					&& !prey.IsHiddenFromPlayer()
					&& !prey.IsPsychologicallyInvisible()
					&& (!ModsConfig.AnomalyActive || !prey.IsMutant || prey.mutant.Def.canBleed)))
				{
					__result = false;
                    StoreAcceptablePrey(false);
					return false;
				}

                __result = true;
                StoreAcceptablePrey(true);
                return false; 
            }
            catch (Exception ex)
            {
                Log.Error($"[Zoology] Patch_IsAcceptablePreyForPredator error: {ex}");
                return true; 
            }
        }
    }
}
