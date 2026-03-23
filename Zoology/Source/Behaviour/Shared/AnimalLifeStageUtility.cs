using System;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    internal static class AnimalLifeStageUtility
    {
        private static LifeStageDef animalBabyLifeStageDef;
        private static LifeStageDef animalBabyTinyLifeStageDef;
        private static LifeStageDef eusocialLarvaLifeStageDef;
        private static LifeStageDef animalJuvenileLifeStageDef;
        private static LifeStageDef eusocialJuvenileLifeStageDef;

        public static bool IsAnimalInfantLifeStage(LifeStageDef stage)
        {
            if (stage == null) return false;

            var baby = animalBabyLifeStageDef ?? (animalBabyLifeStageDef = DefDatabase<LifeStageDef>.GetNamedSilentFail("AnimalBaby"));
            if (stage == baby || string.Equals(stage.defName, "AnimalBaby", StringComparison.OrdinalIgnoreCase))
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

        public static bool IsAnimalJuvenileLifeStage(LifeStageDef stage)
        {
            if (stage == null) return false;

            var juvenile = animalJuvenileLifeStageDef ?? (animalJuvenileLifeStageDef = DefDatabase<LifeStageDef>.GetNamedSilentFail("AnimalJuvenile"));
            if (stage == juvenile || string.Equals(stage.defName, "AnimalJuvenile", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var eusocial = eusocialJuvenileLifeStageDef ?? (eusocialJuvenileLifeStageDef = DefDatabase<LifeStageDef>.GetNamedSilentFail("EusocialInsectJuvenile"));
            return stage == eusocial || string.Equals(stage.defName, "EusocialInsectJuvenile", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAnimalChildLifeStage(LifeStageDef stage)
        {
            return IsAnimalInfantLifeStage(stage) || IsAnimalJuvenileLifeStage(stage);
        }
    }
}
