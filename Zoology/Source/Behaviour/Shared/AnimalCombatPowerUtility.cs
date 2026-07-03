using RimWorld;
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

            return GetAdjustedCombatPower(pawn.kindDef, pawn.ageTracker?.CurLifeStage);
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
            if (pawn == null)
            {
                return 1f;
            }

            return GetLifeStageCombatPowerFactor(pawn.ageTracker?.CurLifeStage);
        }

        public static float GetLifeStageCombatPowerFactor(LifeStageDef stage)
        {
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
