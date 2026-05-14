using Verse;

namespace ZoologyMod
{
    public static class ModConstants
    {
        public const bool DefaultEnableCustomFleeDanger = true;
        public const bool DefaultEnableIgnoreSmallPetsByRaiders = true;
        public const bool DefaultEnableSmallPetNoMeleeRetaliation = true;
        public const bool DefaultEnablePreyFleeFromPredators = true;
        public const bool DefaultAnimalsFleeFromNonHostilePredators = true;
        public const bool DefaultEnablePackHunt = true;
        public const bool DefaultEnableAdvancedPredationLogic = true;
        public const bool DefaultEnableHumanBionicOnAnimal = true;
        public const bool DefaultEnableAgroAtSlaughter = true;
        public const bool DefaultAnimalsFreeFromHumans = true;
        public const bool DefaultEnableCannotBeMutatedProtection = true;
        public const bool DefaultEnableCannotBeAugmentedProtection = true;
        public const bool DefaultEnableNoFleeExtension = true;
        public const bool DefaultEnableFleeFromCarrier = true;
        public const bool DefaultEnableFlyingFleeStart = true;
        public const bool DefaultEnableGenderRestrictedAttacks = true;
        public const bool DefaultEnableEctothermicPatch = true;
        public const bool DefaultEnableAgelessPatch = true;
        public const bool DefaultEnableDrugsImmunePatch = true;
        public const bool DefaultEnableAnimalRegenerationComp = true;
        public const bool DefaultEnableAnimalClottingComp = true;
        public const bool DefaultEnableNoPorcupineQuillPatch = true;
        public const bool DefaultEnableMammalLactation = true;
        public const bool DefaultEnableAnimalChildcare = true;
        public const bool DefaultEnableAnimalEggProtection = true;
        public const bool DefaultPreventFleeFromHumansWhileProtectingYoung = false;
        public const bool DefaultPreventFleeFromHumansWhileProtectingEggClutches = true;
        public const bool DefaultEnableAnimalWoundLicking = true;
        public const bool DefaultEnableWildAnimalReproduction = true;
        public const bool DefaultEnableCannotChewExtension = true;
        public const bool DefaultEnablePredatorDefendCorpse = true;
        public const bool DefaultEnablePredatorDefendPreyFromHumansAndMechanoids = true;
        public const bool DefaultEnableScavengering = true;
        public const bool DefaultAllowSlaughterLactating = false;
        public const bool DefaultDisableAllRuntimePatches = false;
        public const bool DefaultEnableAnimalDamageReduction = true;
        public const bool DefaultEnableAnimalDraftControl = true;
        public const bool DefaultEnableOverrideCEPenetration = false;

        public const int DefaultPredatorSearchRadius = 18;
        public const int DefaultNonHostilePredatorSearchRadius = 12;
        public const int DefaultHumanSearchRadius = 12;
        public const int DefaultFleeDistancePredator = 16;
        public const int DefaultFleeDistanceTargetPredator = 24;
        public const int DefaultFleeDistanceHuman = 16;
        public const int DefaultPreyProtectionRange = 20;
        public const float DefaultSmallPetBodySizeThreshold = 0.45f;
        public const float DefaultSafePredatorBodySizeThreshold = 0.7f;
        public const float DefaultSafeNonPredatorBodySizeThreshold = 3f;
        public const int DefaultMinCombatPowerToDefendPreyFromHumans = 70;
        public const int DefaultChildcareProtectionRange = 10;
        public const int DefaultMinCombatPowerToDefendYoungFromHumans = 70;
        public const int DefaultCorpseUnownedSizeMultiplier = 5;
        public const float DefaultAnimalInfantCombatPowerFactor = 0.2f;
        public const float DefaultAnimalJuvenileCombatPowerFactor = 0.5f;
        public const float CombatPowerDominanceFactor = 1.3f;

        public static ZoologyModSettings Settings => ZoologyModSettings.Instance ?? ZoologyMod.Settings;

        public static float SmallPetBodySizeThreshold => Settings?.SmallPetBodySizeThreshold ?? DefaultSmallPetBodySizeThreshold;
        public static float SafePredatorBodySizeThreshold => Settings?.SafePredatorBodySizeThreshold ?? DefaultSafePredatorBodySizeThreshold;
        public static float SafeNonPredatorBodySizeThreshold => Settings?.SafeNonPredatorBodySizeThreshold ?? DefaultSafeNonPredatorBodySizeThreshold;
        public static int MinCombatPowerToDefendPreyFromHumans => Settings?.MinCombatPowerToDefendPreyFromHumans ?? DefaultMinCombatPowerToDefendPreyFromHumans;
        public static int ChildcareProtectionRange => Settings?.ChildcareProtectionRange ?? DefaultChildcareProtectionRange;
        public static int MinCombatPowerToDefendYoungFromHumans => Settings?.MinCombatPowerToDefendYoungFromHumansAndMechanoids ?? DefaultMinCombatPowerToDefendYoungFromHumans;
        public static float AnimalInfantCombatPowerFactor => DefaultAnimalInfantCombatPowerFactor;
        public static float AnimalJuvenileCombatPowerFactor => DefaultAnimalJuvenileCombatPowerFactor;
    }
}
