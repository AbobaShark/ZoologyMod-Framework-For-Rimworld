using Verse;

namespace ZoologyMod
{
    public static class ModConstants
    {
        public const float DefaultSmallPetBodySizeThreshold = 0.45f;
        public const float DefaultSafePredatorBodySizeThreshold = 0.7f;
        public const float DefaultSafeNonPredatorBodySizeThreshold = 3f;
        public const int DefaultMinCombatPowerToDefendPreyFromHumans = 70;
        public const float DefaultAnimalInfantCombatPowerFactor = 0.2f;
        public const float DefaultAnimalJuvenileCombatPowerFactor = 0.5f;
        public const float CombatPowerDominanceFactor = 1.3f;

        public static ZoologyModSettings Settings => ZoologyModSettings.Instance ?? ZoologyMod.Settings;

        public static float SmallPetBodySizeThreshold => Settings?.SmallPetBodySizeThreshold ?? DefaultSmallPetBodySizeThreshold;
        public static float SafePredatorBodySizeThreshold => Settings?.SafePredatorBodySizeThreshold ?? DefaultSafePredatorBodySizeThreshold;
        public static float SafeNonPredatorBodySizeThreshold => Settings?.SafeNonPredatorBodySizeThreshold ?? DefaultSafeNonPredatorBodySizeThreshold;
        public static int MinCombatPowerToDefendPreyFromHumans => Settings?.MinCombatPowerToDefendPreyFromHumans ?? DefaultMinCombatPowerToDefendPreyFromHumans;
        public static float AnimalInfantCombatPowerFactor => DefaultAnimalInfantCombatPowerFactor;
        public static float AnimalJuvenileCombatPowerFactor => DefaultAnimalJuvenileCombatPowerFactor;
    }
}
