using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    public static class AnimalCombatPowerUtility
    {
        public static float GetAdjustedCombatPower(Pawn pawn)
        {
            if (pawn?.kindDef == null)
            {
                return 0f;
            }

            float basePower = pawn.kindDef.combatPower;
            float factor = GetLifeStageCombatPowerFactor(pawn);
            return basePower > 0f && factor > 0f ? basePower * factor : 0f;
        }

        public static float GetAdjustedCombatPower(PawnKindDef kindDef, LifeStageDef lifeStage)
        {
            if (kindDef == null)
            {
                return 0f;
            }

            return GetAdjustedCombatPower(kindDef.combatPower, lifeStage);
        }

        public static float GetAdjustedCombatPower(float basePower, LifeStageDef lifeStage)
        {
            if (basePower <= 0f)
            {
                return 0f;
            }

            float factor = GetLifeStageCombatPowerFactor(lifeStage);
            if (factor <= 0f)
            {
                return 0f;
            }

            return factor == 1f
                ? basePower
                : basePower * factor;
        }

        public static float GetLifeStageCombatPowerFactor(Pawn pawn)
        {
            if (!ZoologyAnimalGrowth.Enabled)
                return 1f;
            if (pawn == null)
            {
                return 1f;
            }

            LifeStageDef current, next;
            float t;
            if (ZoologyAnimalGrowth.TryGetStages(pawn, out current, out next, out t))
            {
                return Mathf.Lerp(GetLifeStageCombatPowerFactor(current),
                    GetLifeStageCombatPowerFactor(next), t);
            }
            return GetLifeStageCombatPowerFactor(pawn.ageTracker?.CurLifeStage);
        }

        public static float GetLifeStageCombatPowerFactor(LifeStageDef stage)
        {
            if (!ZoologyAnimalGrowth.Enabled)
                return 1f;

            LifeStageCombatPowerExtension extension = stage?.GetModExtension<LifeStageCombatPowerExtension>();
            if (extension != null)
            {
                return extension.combatPowerFactor;
            }

            if (AnimalLifeStageUtility.IsAnimalInfantLifeStage(stage))
            {
                return ModConstants.AnimalInfantCombatPowerFactor;
            }

            if (AnimalLifeStageUtility.IsAnimalJuvenileLifeStage(stage))
            {
                return ModConstants.AnimalJuvenileCombatPowerFactor;
            }

            return 1f;
        }

        public static bool CanAnimalThreatTriggerTargetedFlee(Pawn threat, Pawn prey)
        {
            if (threat == null || prey == null)
            {
                return false;
            }

            if (!threat.IsAnimal)
            {
                return true;
            }

            float threatPower = GetAdjustedCombatPower(threat);
            float preyPower = GetAdjustedCombatPower(prey);
            if (threatPower <= 0f || preyPower <= 0f)
            {
                return true;
            }

            return threatPower > preyPower * ModConstants.CombatPowerDominanceFactor;
        }
    }
}
