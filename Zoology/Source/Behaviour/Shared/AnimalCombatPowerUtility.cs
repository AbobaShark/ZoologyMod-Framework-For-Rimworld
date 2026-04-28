using RimWorld;
using Verse;

namespace ZoologyMod
{
    internal static class AnimalCombatPowerUtility
    {
        public static float GetAdjustedCombatPower(Pawn pawn)
        {
            if (pawn?.kindDef == null)
            {
                return 0f;
            }

            float basePower = pawn.kindDef.combatPower;
            float factor = GetLifeStageCombatPowerFactor(pawn);
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
