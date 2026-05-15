using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using HarmonyLib;

namespace ZoologyMod
{
    public class ZoologyModSettings : ModSettings
    {
        public static ZoologyModSettings Instance;

        public bool EnableOverrideCEPenetration;

        public bool EnableCustomFleeDanger = ModConstants.DefaultEnableCustomFleeDanger;
        public bool EnableIgnoreSmallPetsByRaiders = ModConstants.DefaultEnableIgnoreSmallPetsByRaiders;
        public bool EnableSmallPetNoMeleeRetaliation = ModConstants.DefaultEnableSmallPetNoMeleeRetaliation;
        public bool EnablePreyFleeFromPredators = ModConstants.DefaultEnablePreyFleeFromPredators;
        public bool AnimalsFleeFromNonHostlePredators = ModConstants.DefaultAnimalsFleeFromNonHostilePredators;
        public bool EnablePackHunt = ModConstants.DefaultEnablePackHunt;
        public bool EnableAdvancedPredationLogic = ModConstants.DefaultEnableAdvancedPredationLogic;
        public bool EnableHumanBionicOnAnimal = ModConstants.DefaultEnableHumanBionicOnAnimal;
        public bool EnableAgroAtSlaughter = ModConstants.DefaultEnableAgroAtSlaughter;
        public bool AnimalsFreeFromHumans = ModConstants.DefaultAnimalsFreeFromHumans;
        public bool EnableCannotBeMutatedProtection = ModConstants.DefaultEnableCannotBeMutatedProtection;
        public bool EnableCannotBeAugmentedProtection = ModConstants.DefaultEnableCannotBeAugmentedProtection;
        public bool EnableNoFleeExtension = ModConstants.DefaultEnableNoFleeExtension;
        public bool EnableFleeFromCarrier = ModConstants.DefaultEnableFleeFromCarrier;
        public bool EnableFlyingFleeStart = ModConstants.DefaultEnableFlyingFleeStart;
        public bool EnableGenderRestrictedAttacks = ModConstants.DefaultEnableGenderRestrictedAttacks;
        public bool EnableEctothermicPatch = ModConstants.DefaultEnableEctothermicPatch;
        public bool EnableAgelessPatch = ModConstants.DefaultEnableAgelessPatch;
        public bool EnableDrugsImmunePatch = ModConstants.DefaultEnableDrugsImmunePatch;
        public bool EnableAnimalRegenerationComp = ModConstants.DefaultEnableAnimalRegenerationComp;
        public bool EnableAnimalClottingComp = ModConstants.DefaultEnableAnimalClottingComp;
        public bool EnableNoPorcupineQuillPatch = ModConstants.DefaultEnableNoPorcupineQuillPatch;
        public static bool EnableMammalLactation = ModConstants.DefaultEnableMammalLactation;
        public bool EnableAnimalChildcare = ModConstants.DefaultEnableAnimalChildcare;
        public bool EnableAnimalEggProtection = ModConstants.DefaultEnableAnimalEggProtection;
        public bool PreventFleeFromHumansWhileProtectingYoung = ModConstants.DefaultPreventFleeFromHumansWhileProtectingYoung;
        public bool PreventFleeFromHumansWhileProtectingEggClutches = ModConstants.DefaultPreventFleeFromHumansWhileProtectingEggClutches;
        public bool EnableAnimalWoundLicking = ModConstants.DefaultEnableAnimalWoundLicking;
        public bool EnableWildAnimalReproduction = ModConstants.DefaultEnableWildAnimalReproduction;
        public bool EnableCannotChewExtension = ModConstants.DefaultEnableCannotChewExtension;
        public bool EnablePredatorDefendCorpse = ModConstants.DefaultEnablePredatorDefendCorpse;
        public bool EnablePredatorDefendPreyFromHumansAndMechanoids = ModConstants.DefaultEnablePredatorDefendPreyFromHumansAndMechanoids;
        public bool EnableScavengering = ModConstants.DefaultEnableScavengering;
        public bool DisableAllRuntimePatches = ModConstants.DefaultDisableAllRuntimePatches;

        public int PredatorSearchRadius = ModConstants.DefaultPredatorSearchRadius;
        public int NonHostilePredatorSearchRadius = ModConstants.DefaultNonHostilePredatorSearchRadius;
        public int HumanSearchRadius = ModConstants.DefaultHumanSearchRadius;
        public int FleeDistancePredator = ModConstants.DefaultFleeDistancePredator;
        public int FleeDistanceTargetPredator = ModConstants.DefaultFleeDistanceTargetPredator;
        public int FleeDistanceHuman = ModConstants.DefaultFleeDistanceHuman;

        private const int SearchRadiusMin = 6;
        private const int SearchRadiusMax = 24;
        private const int FleeDistanceMin = 6;
        private const int FleeDistanceMax = 40;

        public int PreyProtectionRange = ModConstants.DefaultPreyProtectionRange; 
        private const int PreyProtectionRangeMin = 10;
        private const int PreyProtectionRangeMax = 30;

        public int ChildcareProtectionRange = ModConstants.DefaultChildcareProtectionRange;
        private const int ChildcareProtectionRangeMin = 10;
        private const int ChildcareProtectionRangeMax = 40;

        
        public int CorpseUnownedSizeMultiplier = ModConstants.DefaultCorpseUnownedSizeMultiplier; 
        private const int CorpseUnownedSizeMultiplierMin = 2;
        private const int CorpseUnownedSizeMultiplierMax = 10;
        private const int MinCombatPowerToDefendPreyFromHumansMin = 0;
        private const int MinCombatPowerToDefendPreyFromHumansMax = 1000;
        private const int MinCombatPowerToDefendYoungFromHumansMin = 0;
        private const int MinCombatPowerToDefendYoungFromHumansMax = 1000;

        public bool AllowSlaughterLactating = ModConstants.DefaultAllowSlaughterLactating;
        public Dictionary<string, bool> AnimalsFreeFromHumansPerAnimal = new Dictionary<string, bool>();
        public Dictionary<string, bool> RoamersPerAnimal = new Dictionary<string, bool>();
        public Dictionary<string, float> RoamMtbDaysPerAnimal = new Dictionary<string, float>();
        public Dictionary<string, string> NonRoamerTrainabilityPerAnimal = new Dictionary<string, string>();
        public Dictionary<string, bool> AnimalFeatureEnabledOverrides = new Dictionary<string, bool>();
        public Dictionary<string, bool> AnimalFeatureBoolParameterOverrides = new Dictionary<string, bool>();
        public Dictionary<string, int> AnimalFeatureIntParameterOverrides = new Dictionary<string, int>();
        public Dictionary<string, float> AnimalFeatureFloatParameterOverrides = new Dictionary<string, float>();

        private const string FeatureScavenger = "modext_scavenger";
        private const string FeatureFleeFromCarrier = "modext_flee_from_carrier";
        private const string FeatureCompAgeless = "comp_ageless";
        private const string FeatureCompDrugsImmune = "comp_drugs_immune";
        private const string FeatureCompAnimalClotting = "comp_animal_clotting";

        private const string ParamAllowVeryRotten = "allowVeryRotten";
        private const string ParamFleeRadius = "fleeRadius";
        private const string ParamFleeBodySizeLimit = "fleeBodySizeLimit";
        private const string ParamFleeDistance = "fleeDistance";
        private const string ParamCleanupIntervalTicks = "cleanupIntervalTicks";
        private const string ParamCheckInterval = "checkInterval";
        private const string ParamTendingQualityMin = "tendingQualityMin";
        private const string ParamTendingQualityMax = "tendingQualityMax";

        
        
        public bool EnableAnimalDamageReduction = ModConstants.DefaultEnableAnimalDamageReduction;
        public bool EnableAnimalDraftControl = ModConstants.DefaultEnableAnimalDraftControl;

        private float _smallPetBodySizeThreshold = ModConstants.DefaultSmallPetBodySizeThreshold;
        private float _safePredatorBodySizeThreshold = ModConstants.DefaultSafePredatorBodySizeThreshold;
        private float _safeNonPredatorBodySizeThreshold = ModConstants.DefaultSafeNonPredatorBodySizeThreshold;
        private int _minCombatPowerToDefendPreyFromHumans = ModConstants.DefaultMinCombatPowerToDefendPreyFromHumans;
        private int _minCombatPowerToDefendYoungFromHumansAndMechanoids = ModConstants.DefaultMinCombatPowerToDefendYoungFromHumans;

        private Vector2 _scrollPosition = Vector2.zero;
        private SettingsPage _activePage = SettingsPage.PredatorPreyInteraction;

        private readonly bool _cePresent;

        private enum SettingsPage
        {
            PredatorPreyInteraction = 0,
            Physiology = 1,
            Combat = 2,
            OtherBehavior = 3,
            Dev = 4
        }

        public float SmallPetBodySizeThreshold
        {
            get => _smallPetBodySizeThreshold;
            set => _smallPetBodySizeThreshold = Mathf.Clamp(value, 0f, 10f);
        }

        public float SafePredatorBodySizeThreshold
        {
            get => _safePredatorBodySizeThreshold;
            set => _safePredatorBodySizeThreshold = Mathf.Clamp(value, 0f, 10f);
        }

        public float SafeNonPredatorBodySizeThreshold
        {
            get => _safeNonPredatorBodySizeThreshold;
            set => _safeNonPredatorBodySizeThreshold = Mathf.Clamp(value, 0f, 10f);
        }

        public int MinCombatPowerToDefendPreyFromHumans
        {
            get => _minCombatPowerToDefendPreyFromHumans;
            set => _minCombatPowerToDefendPreyFromHumans = Mathf.Clamp(value, MinCombatPowerToDefendPreyFromHumansMin, MinCombatPowerToDefendPreyFromHumansMax);
        }

        public int MinCombatPowerToDefendYoungFromHumansAndMechanoids
        {
            get => _minCombatPowerToDefendYoungFromHumansAndMechanoids;
            set => _minCombatPowerToDefendYoungFromHumansAndMechanoids = Mathf.Clamp(value, MinCombatPowerToDefendYoungFromHumansMin, MinCombatPowerToDefendYoungFromHumansMax);
        }

        public bool CanPredatorDefendPreyFromHumansAndMechanoids(Pawn predator)
        {
            if (!EnablePredatorDefendCorpse || !EnablePredatorDefendPreyFromHumansAndMechanoids)
            {
                return false;
            }

            if (predator?.RaceProps?.predator != true)
            {
                return false;
            }

            return (predator.kindDef?.combatPower ?? 0f) >= MinCombatPowerToDefendPreyFromHumans;
        }

        public static bool CanPredatorDefendPreyFromHumansAndMechanoidsNow(Pawn predator)
        {
            ZoologyModSettings settings = Instance;
            if (settings == null)
            {
                return predator?.RaceProps?.predator == true
                    && (predator.kindDef?.combatPower ?? 0f) >= ModConstants.DefaultMinCombatPowerToDefendPreyFromHumans;
            }

            return settings.CanPredatorDefendPreyFromHumansAndMechanoids(predator);
        }

        public bool CanChildcareDefendYoungFromHumansAndMechanoids(Pawn protector)
        {
            if (!EnableAnimalChildcare || protector == null)
            {
                return false;
            }

            return AnimalCombatPowerUtility.GetAdjustedCombatPower(protector) >= MinCombatPowerToDefendYoungFromHumansAndMechanoids;
        }

        public static bool CanChildcareDefendYoungFromHumansAndMechanoidsNow(Pawn protector)
        {
            ZoologyModSettings settings = Instance;
            if (settings == null)
            {
                return AnimalCombatPowerUtility.GetAdjustedCombatPower(protector) >= ModConstants.DefaultMinCombatPowerToDefendYoungFromHumans;
            }

            return settings.CanChildcareDefendYoungFromHumansAndMechanoids(protector);
        }

        public bool ShouldPreventFleeFromHumansWhileProtectingYoungNow()
        {
            return EnableAnimalChildcare
                && AnimalsFreeFromHumans
                && PreventFleeFromHumansWhileProtectingYoung;
        }

        public bool ShouldPreventFleeFromHumansWhileProtectingEggClutchesNow()
        {
            return EnableAnimalChildcare
                && EnableAnimalEggProtection
                && AnimalsFreeFromHumans
                && PreventFleeFromHumansWhileProtectingEggClutches;
        }

        public static bool ShouldPreventFleeFromHumansWhileProtectingYoungStatic()
        {
            ZoologyModSettings settings = Instance;
            if (settings == null)
            {
                return ModConstants.DefaultEnableAnimalChildcare
                    && ModConstants.DefaultAnimalsFreeFromHumans
                    && ModConstants.DefaultPreventFleeFromHumansWhileProtectingYoung;
            }

            return settings.ShouldPreventFleeFromHumansWhileProtectingYoungNow();
        }

        public static bool ShouldPreventFleeFromHumansWhileProtectingEggClutchesStatic()
        {
            ZoologyModSettings settings = Instance;
            if (settings == null)
            {
                return ModConstants.DefaultEnableAnimalChildcare
                    && ModConstants.DefaultEnableAnimalEggProtection
                    && ModConstants.DefaultAnimalsFreeFromHumans
                    && ModConstants.DefaultPreventFleeFromHumansWhileProtectingEggClutches;
            }

            return settings.ShouldPreventFleeFromHumansWhileProtectingEggClutchesNow();
        }

        public ZoologyModSettings()
        {
            Instance = this;

            _cePresent = AccessTools.TypeByName("CombatExtended.Verb_MeleeAttackCE") != null;
            EnableOverrideCEPenetration = _cePresent ? true : false;

            EnableAnimalDamageReduction = !_cePresent;

            EnsureCollectionsInitialized();
            ZoologyRuntimeAnimalOverrides.EnsureInitialized();
        }

        public void DoWindowContents(Rect inRect)
        {
            var prevFont = Text.Font;
            var prevAnchor = Text.Anchor;
            int runtimePatchToggleHashBefore = GetRuntimePatchToggleHash();

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "Zoology_SettingsWindowTitle".Translate());
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            float tabY = inRect.y + 36f;
            float tabHeight = 30f;
            float tabGap = 6f;
            float tabWidth = (inRect.width - tabGap * 4f) / 5f;

            Rect predPreyTabRect = new Rect(inRect.x, tabY, tabWidth, tabHeight);
            Rect physiologyTabRect = new Rect(predPreyTabRect.xMax + tabGap, tabY, tabWidth, tabHeight);
            Rect combatTabRect = new Rect(physiologyTabRect.xMax + tabGap, tabY, tabWidth, tabHeight);
            Rect otherBehaviorTabRect = new Rect(combatTabRect.xMax + tabGap, tabY, tabWidth, tabHeight);
            Rect devTabRect = new Rect(otherBehaviorTabRect.xMax + tabGap, tabY, tabWidth, tabHeight);

            DrawSettingsTabButton(predPreyTabRect, SettingsPage.PredatorPreyInteraction, "Zoology_TabPredatorPrey".Translate());
            DrawSettingsTabButton(physiologyTabRect, SettingsPage.Physiology, "Zoology_TabPhysiology".Translate());
            DrawSettingsTabButton(combatTabRect, SettingsPage.Combat, "Zoology_TabCombat".Translate());
            DrawSettingsTabButton(otherBehaviorTabRect, SettingsPage.OtherBehavior, "Zoology_TabOtherBehavior".Translate());
            DrawSettingsTabButton(devTabRect, SettingsPage.Dev, "Zoology_TabDev".Translate());

            float contentTop = tabY + tabHeight + 8f;
            Rect outRect = new Rect(inRect.x, contentTop, inRect.width, inRect.yMax - contentTop);
            float contentHeight = CalculatePageContentHeight(_activePage);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, contentHeight));

            Widgets.BeginScrollView(outRect, ref _scrollPosition, viewRect);

            var list = new Listing_Standard();
            list.Begin(viewRect);

            switch (_activePage)
            {
                case SettingsPage.PredatorPreyInteraction:
                    DrawPredatorPreySettings(list);
                    break;
                case SettingsPage.Physiology:
                    DrawPhysiologySettings(list);
                    break;
                case SettingsPage.Combat:
                    DrawCombatSettings(list);
                    break;
                case SettingsPage.OtherBehavior:
                    DrawOtherBehaviorSettings(list);
                    break;
                case SettingsPage.Dev:
                    DrawDevSettings(list);
                    break;
            }

            list.GapLine(24f);
            if (list.ButtonText("Zoology_ResetButtonLabel".Translate()))
            {
                ResetToDefaults();
            }

            list.End();
            Widgets.EndScrollView();

            int runtimePatchToggleHashAfter = GetRuntimePatchToggleHash();
            if (runtimePatchToggleHashBefore != runtimePatchToggleHashAfter)
            {
                ZoologyMod.SyncRuntimePatchesWithSettings(forceRebuild: true);
            }

            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
        }

        private void DrawSettingsTabButton(Rect rect, SettingsPage page, string label)
        {
            string finalLabel = _activePage == page ? $"{label} ●" : label;
            if (Widgets.ButtonText(rect, finalLabel))
            {
                if (_activePage != page)
                {
                    _activePage = page;
                    _scrollPosition = Vector2.zero;
                }
            }
        }

        private float CalculatePageContentHeight(SettingsPage page)
        {
            switch (page)
            {
                case SettingsPage.PredatorPreyInteraction:
                    return 1320f
                        + (EnablePreyFleeFromPredators ? 150f : 0f)
                        + (EnablePredatorDefendCorpse ? 156f : 0f);
                case SettingsPage.Physiology:
                    return 1380f
                        + (!EnableMammalLactation ? 30f : 0f)
                        + (EnableAnimalChildcare && AnimalsFreeFromHumans ? 90f : 0f);
                case SettingsPage.Combat:
                    return 580f;
                case SettingsPage.OtherBehavior:
                    return 1660f
                        + (EnableCustomFleeDanger ? 170f : 0f)
                        + (EnableIgnoreSmallPetsByRaiders ? 200f : 0f)
                        + (AnimalsFreeFromHumans ? 130f : 0f);
                case SettingsPage.Dev:
                default:
                    return 2060f;
            }
        }

        private void DrawPredatorPreySettings(Listing_Standard list)
        {
            list.GapLine(8f);
            list.CheckboxLabeled("Zoology_EnablePreyFleeing_Label".Translate(), ref EnablePreyFleeFromPredators, "Zoology_EnablePreyFleeing_Desc".Translate());

            if (EnablePreyFleeFromPredators)
            {
                list.GapLine(6f);
                list.Label(string.Format("Zoology_PredatorSearchRadius_Label".Translate(), PredatorSearchRadius, SearchRadiusMin, SearchRadiusMax));
                PredatorSearchRadius = (int)list.Slider(PredatorSearchRadius, SearchRadiusMin, SearchRadiusMax);

                list.GapLine(6f);
                list.Label(string.Format("Zoology_FleeDistanceTargetPredator_Label".Translate(), FleeDistanceTargetPredator, FleeDistanceMin, FleeDistanceMax));
                FleeDistanceTargetPredator = (int)list.Slider(FleeDistanceTargetPredator, FleeDistanceMin, FleeDistanceMax);

                list.GapLine(8f);
                list.CheckboxLabeled("Zoology_AnimalsFlee_NonHostile_Label".Translate(), ref AnimalsFleeFromNonHostlePredators, "Zoology_AnimalsFlee_NonHostile_Desc".Translate());

                if (AnimalsFleeFromNonHostlePredators)
                {
                    list.GapLine(6f);
                    list.Label(string.Format("Zoology_NonHostilePredatorSearchRadius_Label".Translate(), NonHostilePredatorSearchRadius, SearchRadiusMin, SearchRadiusMax));
                    NonHostilePredatorSearchRadius = (int)list.Slider(NonHostilePredatorSearchRadius, SearchRadiusMin, SearchRadiusMax);

                    list.GapLine(6f);
                    list.Label(string.Format("Zoology_FleeDistancePredator_Label".Translate(), FleeDistancePredator, FleeDistanceMin, FleeDistanceMax));
                    FleeDistancePredator = (int)list.Slider(FleeDistancePredator, FleeDistanceMin, FleeDistanceMax);
                }
            }

            list.GapLine(12f);
            list.CheckboxLabeled("Zoology_EnablePackHunt_Label".Translate(), ref EnablePackHunt, "Zoology_EnablePackHunt_Desc".Translate());

            list.GapLine(12f);
            list.CheckboxLabeled("Zoology_EnableAdvancedPredation_Label".Translate(), ref EnableAdvancedPredationLogic, "Zoology_EnableAdvancedPredation_Desc".Translate());

            list.GapLine(12f);
            list.CheckboxLabeled("Zoology_EnableScavengering_Label".Translate(), ref EnableScavengering, "Zoology_EnableScavengering_Desc".Translate());

            if (EnableScavengering)
            {
                list.GapLine(6f);
                DrawRuntimeFeatureConfigureButton(list, "modext_scavenger", "Zoology_ConfigureScavenger_Button".Translate());
            }

            list.GapLine(12f);
            list.CheckboxLabeled("Zoology_EnablePredatorDefendCorpse_Label".Translate(), ref EnablePredatorDefendCorpse, "Zoology_EnablePredatorDefendCorpse_Desc".Translate());

            if (EnablePredatorDefendCorpse)
            {
                list.GapLine(6f);
                list.Label(string.Format("Zoology_PreyProtectionRange_Label".Translate(), PreyProtectionRange, PreyProtectionRangeMin, PreyProtectionRangeMax));
                PreyProtectionRange = (int)list.Slider(PreyProtectionRange, PreyProtectionRangeMin, PreyProtectionRangeMax);

                list.GapLine(6f);
                list.Label(string.Format("Zoology_CorpseUnownedSizeMultiplier_Label".Translate(), CorpseUnownedSizeMultiplier, CorpseUnownedSizeMultiplierMin, CorpseUnownedSizeMultiplierMax));
                CorpseUnownedSizeMultiplier = (int)list.Slider(CorpseUnownedSizeMultiplier, CorpseUnownedSizeMultiplierMin, CorpseUnownedSizeMultiplierMax);

                list.GapLine(10f);
                list.CheckboxLabeled(
                    "Zoology_EnablePredatorDefendFromHumans_Label".Translate(),
                    ref EnablePredatorDefendPreyFromHumansAndMechanoids,
                    "Zoology_EnablePredatorDefendFromHumans_Desc".Translate()
                );

                if (EnablePredatorDefendPreyFromHumansAndMechanoids)
                {
                    list.GapLine(6f);
                    list.Label(string.Format("Zoology_MinCombatPowerDefend_Label".Translate(), MinCombatPowerToDefendPreyFromHumans, MinCombatPowerToDefendPreyFromHumansMin, MinCombatPowerToDefendPreyFromHumansMax));
                    MinCombatPowerToDefendPreyFromHumans = (int)list.Slider(MinCombatPowerToDefendPreyFromHumans, MinCombatPowerToDefendPreyFromHumansMin, MinCombatPowerToDefendPreyFromHumansMax);
                }
            }
        }

        private void DrawPhysiologySettings(Listing_Standard list)
        {
            list.GapLine(8f);
            list.CheckboxLabeled("Zoology_EnableMammalLactation_Label".Translate(), ref EnableMammalLactation, "Zoology_EnableMammalLactation_Desc".Translate());
            DrawLactationSettings(list);
            if (EnableMammalLactation)
            {
                list.GapLine(6f);
                DrawRuntimeFeatureConfigureButton(list, "modext_mammal", "Zoology_ConfigureMammal_Button".Translate());
            }

            list.GapLine(12f);
            list.CheckboxLabeled(
                "Zoology_EnableAnimalChildcare_Label".Translate(),
                ref EnableAnimalChildcare,
                "Zoology_EnableAnimalChildcare_Desc".Translate()
            );
            if (EnableAnimalChildcare)
            {
                list.GapLine(6f);
                DrawRuntimeFeatureConfigureButton(list, "modext_childcare", "Zoology_ConfigureChildcare_Button".Translate());

                list.GapLine(6f);
                list.Label(string.Format("Zoology_ChildcareProtectionRange_Label".Translate(), ChildcareProtectionRange, ChildcareProtectionRangeMin, ChildcareProtectionRangeMax));
                ChildcareProtectionRange = (int)list.Slider(ChildcareProtectionRange, ChildcareProtectionRangeMin, ChildcareProtectionRangeMax);

                list.GapLine(6f);
                list.Label(string.Format("Zoology_MinCombatPowerDefendYoung_Label".Translate(), MinCombatPowerToDefendYoungFromHumansAndMechanoids, MinCombatPowerToDefendYoungFromHumansMin, MinCombatPowerToDefendYoungFromHumansMax));
                MinCombatPowerToDefendYoungFromHumansAndMechanoids = (int)list.Slider(MinCombatPowerToDefendYoungFromHumansAndMechanoids, MinCombatPowerToDefendYoungFromHumansMin, MinCombatPowerToDefendYoungFromHumansMax);

                if (AnimalsFreeFromHumans)
                {
                    list.GapLine(8f);
                    list.CheckboxLabeled(
                        "Zoology_PreventFleeFromHumansWhileProtectingYoung_Label".Translate(),
                        ref PreventFleeFromHumansWhileProtectingYoung,
                        "Zoology_PreventFleeFromHumansWhileProtectingYoung_Desc".Translate());

                    bool prevProtectEggClutchGuiEnabled = GUI.enabled;
                    if (!EnableAnimalEggProtection)
                    {
                        GUI.enabled = false;
                    }

                    list.GapLine(6f);
                    list.CheckboxLabeled(
                        "Zoology_PreventFleeFromHumansWhileProtectingEggClutches_Label".Translate(),
                        ref PreventFleeFromHumansWhileProtectingEggClutches,
                        "Zoology_PreventFleeFromHumansWhileProtectingEggClutches_Desc".Translate());

                    GUI.enabled = prevProtectEggClutchGuiEnabled;
                }
            }

            list.GapLine(12f);
            bool prevEggProtectionGuiEnabled = GUI.enabled;
            if (!EnableAnimalChildcare)
            {
                GUI.enabled = false;
            }

            list.CheckboxLabeled(
                "Zoology_EnableAnimalEggProtection_Label".Translate(),
                ref EnableAnimalEggProtection,
                "Zoology_EnableAnimalEggProtection_Desc".Translate()
            );

            GUI.enabled = prevEggProtectionGuiEnabled;

            list.GapLine(12f);
            list.CheckboxLabeled(
                "Zoology_EnableAnimalWoundLicking_Label".Translate(),
                ref EnableAnimalWoundLicking,
                "Zoology_EnableAnimalWoundLicking_Desc".Translate()
            );

            list.GapLine(12f);
            list.CheckboxLabeled(
                "Zoology_EnableEctothermic_Label".Translate(),
                ref EnableEctothermicPatch,
                "Zoology_EnableEctothermic_Desc".Translate()
            );
            if (EnableEctothermicPatch)
            {
                list.GapLine(6f);
                DrawRuntimeFeatureConfigureButton(list, "modext_ectothermic", "Zoology_ConfigureEctothermic_Button".Translate());
            }
        }

        private void DrawCombatSettings(Listing_Standard list)
        {
            list.GapLine(8f);
            if (_cePresent)
            {
                list.CheckboxLabeled(
                    "Zoology_OverrideCEPenetration_Label".Translate(),
                    ref EnableOverrideCEPenetration,
                    "Zoology_OverrideCEPenetration_Desc".Translate()
                );
            }
            else
            {
                list.Label("Zoology_CENotDetected".Translate());
                list.GapLine(6f);
                EnableOverrideCEPenetration = false;
            }

            list.GapLine(12f);
            list.CheckboxLabeled(
                "Zoology_EnableAnimalDraftControl_Label".Translate(),
                ref EnableAnimalDraftControl,
                "Zoology_EnableAnimalDraftControl_Desc".Translate()
            );

            list.GapLine(12f);
            if (_cePresent)
            {
                EnableAnimalDamageReduction = false;
            }

            bool prevGuiEnabled = GUI.enabled;
            if (_cePresent) GUI.enabled = false;

            list.CheckboxLabeled(
                "Zoology_EnableAnimalDamageReduction_Label".Translate(),
                ref EnableAnimalDamageReduction,
                "Zoology_EnableAnimalDamageReduction_Desc".Translate()
            );

            GUI.enabled = prevGuiEnabled;

            if (_cePresent)
            {
                list.Label("Zoology_CENotAvailable".Translate());
                list.GapLine(6f);
            }
        }

        private void DrawOtherBehaviorSettings(Listing_Standard list)
        {
            list.GapLine(8f);
            list.CheckboxLabeled("Zoology_EnableCustomFleeDanger_Label".Translate(), ref EnableCustomFleeDanger, "Zoology_EnableCustomFleeDanger_Desc".Translate());

            if (EnableCustomFleeDanger)
            {
                list.GapLine(6f);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                list.Label("Zoology_SafeBodySizeThresholds_Label".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                list.GapLine(12f);

                _safePredatorBodySizeThreshold = list.Slider(_safePredatorBodySizeThreshold, 0f, 30f);
                list.Label(string.Format("Zoology_SafePredatorBodySize_Label".Translate(), SafePredatorBodySizeThreshold));
                list.GapLine(12f);

                _safeNonPredatorBodySizeThreshold = list.Slider(_safeNonPredatorBodySizeThreshold, 0f, 30f);
                list.Label(string.Format("Zoology_SafeNonPredatorBodySize_Label".Translate(), SafeNonPredatorBodySizeThreshold));
                list.GapLine(12f);
            }

            list.GapLine(12f);
            list.CheckboxLabeled("Zoology_EnableIgnoreSmallPets_Label".Translate(), ref EnableIgnoreSmallPetsByRaiders, "Zoology_EnableIgnoreSmallPets_Desc".Translate());

            if (EnableIgnoreSmallPetsByRaiders)
            {
                list.GapLine(12f);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                list.Label("Zoology_SmallPetBodySize_Label".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                list.GapLine(12f);

                _smallPetBodySizeThreshold = list.Slider(_smallPetBodySizeThreshold, 0f, 30f);
                list.Label(string.Format("Zoology_SmallPetBodySize_Value".Translate(), SmallPetBodySizeThreshold));
                list.GapLine(12f);

                list.CheckboxLabeled(
                    "Zoology_EnableSmallPetNoMelee_Label".Translate(),
                    ref EnableSmallPetNoMeleeRetaliation,
                    "Zoology_EnableSmallPetNoMelee_Desc".Translate()
                );
                list.GapLine(12f);
            }

            list.GapLine(12f);
            list.CheckboxLabeled("Zoology_AnimalsFreeFromHumans_Label".Translate(), ref AnimalsFreeFromHumans, "Zoology_AnimalsFreeFromHumans_Desc".Translate());

            if (AnimalsFreeFromHumans)
            {
                list.GapLine(6f);
                list.Label(string.Format("Zoology_HumanSearchRadius_Label".Translate(), HumanSearchRadius, SearchRadiusMin, SearchRadiusMax));
                HumanSearchRadius = (int)list.Slider(HumanSearchRadius, SearchRadiusMin, SearchRadiusMax);

                list.GapLine(6f);
                list.Label(string.Format("Zoology_FleeDistanceHuman_Label".Translate(), FleeDistanceHuman, FleeDistanceMin, FleeDistanceMax));
                FleeDistanceHuman = (int)list.Slider(FleeDistanceHuman, FleeDistanceMin, FleeDistanceMax);

                list.GapLine(6f);
                if (list.ButtonText("Zoology_ConfigureAnimalsFleeHumans_Button".Translate()))
                {
                    Find.WindowStack.Add(new Dialog_AnimalsFreeFromHumansSelector(this));
                }
            }

            list.GapLine(12f);
            list.CheckboxLabeled(
                "Zoology_EnableWildAnimalReproduction_Label".Translate(),
                ref EnableWildAnimalReproduction,
                "Zoology_EnableWildAnimalReproduction_Desc".Translate()
            );

            list.GapLine(6f);
            if (list.ButtonText("Zoology_ConfigureAnimalRoamers_Button".Translate()))
            {
                Find.WindowStack.Add(new Dialog_AnimalRoamersSelector(this));
            }

            list.GapLine(12f);
            list.CheckboxLabeled("Zoology_EnableHumanBionicOnAnimal_Label".Translate(), ref EnableHumanBionicOnAnimal, "Zoology_EnableHumanBionicOnAnimal_Desc".Translate());

            list.GapLine(12f);
            list.CheckboxLabeled("Zoology_EnableAgroAtSlaughter_Label".Translate(), ref EnableAgroAtSlaughter, "Zoology_EnableAgroAtSlaughter_Desc".Translate());
            if (EnableAgroAtSlaughter)
            {
                list.GapLine(6f);
                DrawRuntimeFeatureConfigureButton(list, "modext_agro_at_slaughter", "Zoology_ConfigureAgroAtSlaughter_Button".Translate());
            }
        }

        private void DrawDevSettings(Listing_Standard list)
        {
            list.GapLine(8f);
            var oldColor = GUI.color;
            GUI.color = new Color(1f, 0.82f, 0.28f, 1f);
            list.Label("Zoology_DevWarning".Translate());
            GUI.color = oldColor;
            list.GapLine(4f);
            Text.Font = GameFont.Tiny;
            list.Label("Zoology_MasterSwitchNote".Translate());
            Text.Font = GameFont.Small;

            list.GapLine(12f);
            bool runtimeDisabled = DisableAllRuntimePatches;
            string runtimeButtonLabel = runtimeDisabled
                ? "Zoology_EnableAllPatches_Button".Translate()
                : "Zoology_DisableAllPatches_Button".Translate();
            if (list.ButtonText(runtimeButtonLabel))
            {
                DisableAllRuntimePatches = !runtimeDisabled;
                Write();
            }

            list.Label(runtimeDisabled ? "Zoology_PatchesDisabled".Translate() : "Zoology_PatchesEnabled".Translate());
            list.GapLine(6f);
            Text.Font = GameFont.Tiny;
            list.Label("Zoology_ReloadWarning".Translate());
            Text.Font = GameFont.Small;

            list.GapLine(12f);
            list.CheckboxLabeled("Zoology_EnableCannotBeMutated_Label".Translate(), ref EnableCannotBeMutatedProtection, "Zoology_EnableCannotBeMutated_Desc".Translate());
            if (EnableCannotBeMutatedProtection)
            {
                list.GapLine(6f);
                DrawRuntimeFeatureConfigureButton(list, "modext_cannot_be_mutated", "Zoology_ConfigureCannotBeMutated_Button".Translate());
            }

            list.GapLine(12f);
            list.CheckboxLabeled("Zoology_EnableCannotBeAugmented_Label".Translate(), ref EnableCannotBeAugmentedProtection, "Zoology_EnableCannotBeAugmented_Desc".Translate());
            if (EnableCannotBeAugmentedProtection)
            {
                list.GapLine(6f);
                DrawRuntimeFeatureConfigureButton(list, "modext_cannot_be_augmented", "Zoology_ConfigureCannotBeAugmented_Button".Translate());
            }

            list.GapLine(12f);
            list.CheckboxLabeled("Zoology_EnableNoFlee_Label".Translate(), ref EnableNoFleeExtension, "Zoology_EnableNoFlee_Desc".Translate());
            if (EnableNoFleeExtension)
            {
                list.GapLine(6f);
                DrawRuntimeFeatureConfigureButton(list, "modext_no_flee", "Zoology_ConfigureNoFlee_Button".Translate());
            }

            list.GapLine(12f);
            list.CheckboxLabeled("Zoology_EnableFleeFromCarrier_Label".Translate(), ref EnableFleeFromCarrier, "Zoology_EnableFleeFromCarrier_Desc".Translate());
            if (EnableFleeFromCarrier)
            {
                list.GapLine(6f);
                DrawRuntimeFeatureConfigureButton(list, "modext_flee_from_carrier", "Zoology_ConfigureFleeFromCarrier_Button".Translate());
            }

            list.GapLine(12f);
            list.CheckboxLabeled("Zoology_EnableFlyingFleeStart_Label".Translate(), ref EnableFlyingFleeStart, "Zoology_EnableFlyingFleeStart_Desc".Translate());

            list.GapLine(12f);
            list.CheckboxLabeled("Zoology_EnableGenderRestrictedAttacks_Label".Translate(), ref EnableGenderRestrictedAttacks, "Zoology_EnableGenderRestrictedAttacks_Desc".Translate());

            list.GapLine(12f);
            bool oldCannotChewEnabled = EnableCannotChewExtension;
            list.CheckboxLabeled("Zoology_EnableCannotChew_Label".Translate(), ref EnableCannotChewExtension, "Zoology_EnableCannotChew_Desc".Translate());
            if (oldCannotChewEnabled != EnableCannotChewExtension)
            {
                CannotChewPresenceCache.RebuildFromCurrentMaps();
            }
            if (EnableCannotChewExtension)
            {
                list.GapLine(6f);
                DrawRuntimeFeatureConfigureButton(list, "modext_cannot_chew", "Zoology_ConfigureCannotChew_Button".Translate());
            }

            list.GapLine(12f);
            list.CheckboxLabeled("Zoology_EnableAgeless_Label".Translate(), ref EnableAgelessPatch, "Zoology_EnableAgeless_Desc".Translate());
            if (EnableAgelessPatch)
            {
                list.GapLine(6f);
                DrawRuntimeFeatureConfigureButton(list, "comp_ageless", "Zoology_ConfigureAgeless_Button".Translate());
            }

            list.GapLine(12f);
            list.CheckboxLabeled("Zoology_EnableDrugsImmune_Label".Translate(), ref EnableDrugsImmunePatch, "Zoology_EnableDrugsImmune_Desc".Translate());
            if (EnableDrugsImmunePatch)
            {
                list.GapLine(6f);
                DrawRuntimeFeatureConfigureButton(list, "comp_drugs_immune", "Zoology_ConfigureDrugsImmune_Button".Translate());
            }

            list.GapLine(12f);
            list.CheckboxLabeled("Zoology_EnableAnimalRegeneration_Label".Translate(), ref EnableAnimalRegenerationComp, "Zoology_EnableAnimalRegeneration_Desc".Translate());

            list.GapLine(12f);
            list.CheckboxLabeled("Zoology_EnableAnimalClotting_Label".Translate(), ref EnableAnimalClottingComp, "Zoology_EnableAnimalClotting_Desc".Translate());
            if (EnableAnimalClottingComp)
            {
                list.GapLine(6f);
                DrawRuntimeFeatureConfigureButton(list, "comp_animal_clotting", "Zoology_ConfigureAnimalClotting_Button".Translate());
            }

            list.GapLine(12f);
            list.CheckboxLabeled("Zoology_EnableNoPorcupineQuill_Label".Translate(), ref EnableNoPorcupineQuillPatch, "Zoology_EnableNoPorcupineQuill_Desc".Translate());
            if (EnableNoPorcupineQuillPatch)
            {
                list.GapLine(6f);
                DrawRuntimeFeatureConfigureButton(list, "modext_no_porcupine_quill", "Zoology_ConfigureNoPorcupineQuill_Button".Translate());
            }
        }

        private void DrawRuntimeFeatureConfigureButton(Listing_Standard list, string featureId, string buttonLabel)
        {
            if (!ZoologyRuntimeAnimalOverrides.TryGetFeature(featureId, out RuntimeAnimalFeatureDefinition feature))
            {
                return;
            }

            string label = string.IsNullOrEmpty(buttonLabel) ? $"Configure {feature.Label} species" : buttonLabel;
            if (list.ButtonText(label))
            {
                Find.WindowStack.Add(new Dialog_AnimalRuntimeFeatureSelector(this, feature));
            }
        }

        private void DrawLactationSettings(Listing_Standard list)
        {
            list.GapLine(12f);
            list.Label("Zoology_LactationAutoSlaughter_Label".Translate());

            bool prevGuiEnabledForLact = GUI.enabled;
            if (!EnableMammalLactation)
            {
                GUI.enabled = false;
                AllowSlaughterLactating = false;
            }

            list.CheckboxLabeled(
                "Zoology_AllowSlaughterLactating_Label".Translate(),
                ref AllowSlaughterLactating,
                "Zoology_AllowSlaughterLactating_Desc".Translate()
            );

            GUI.enabled = prevGuiEnabledForLact;

            if (!EnableMammalLactation)
            {
                Text.Font = GameFont.Tiny;
                list.Label("Zoology_LactationDisabled_Note".Translate());
                Text.Font = GameFont.Small;
                list.GapLine(6f);
            }
        }

        private void ResetToDefaults()
        {
            EnsureCollectionsInitialized();

            EnableCustomFleeDanger = ModConstants.DefaultEnableCustomFleeDanger;
            EnableIgnoreSmallPetsByRaiders = ModConstants.DefaultEnableIgnoreSmallPetsByRaiders;
            EnableSmallPetNoMeleeRetaliation = ModConstants.DefaultEnableSmallPetNoMeleeRetaliation;
            EnablePreyFleeFromPredators = ModConstants.DefaultEnablePreyFleeFromPredators;
            AnimalsFleeFromNonHostlePredators = ModConstants.DefaultAnimalsFleeFromNonHostilePredators;
            EnablePackHunt = ModConstants.DefaultEnablePackHunt;
            EnableAdvancedPredationLogic = ModConstants.DefaultEnableAdvancedPredationLogic;
            EnableHumanBionicOnAnimal = ModConstants.DefaultEnableHumanBionicOnAnimal;
            EnableAgroAtSlaughter = ModConstants.DefaultEnableAgroAtSlaughter;
            AnimalsFreeFromHumans = ModConstants.DefaultAnimalsFreeFromHumans;
            EnableCannotBeMutatedProtection = ModConstants.DefaultEnableCannotBeMutatedProtection;
            EnableCannotBeAugmentedProtection = ModConstants.DefaultEnableCannotBeAugmentedProtection;
            EnableNoFleeExtension = ModConstants.DefaultEnableNoFleeExtension;
            EnableFleeFromCarrier = ModConstants.DefaultEnableFleeFromCarrier;
            EnableFlyingFleeStart = ModConstants.DefaultEnableFlyingFleeStart;
            EnableGenderRestrictedAttacks = ModConstants.DefaultEnableGenderRestrictedAttacks;
            EnableEctothermicPatch = ModConstants.DefaultEnableEctothermicPatch;
            EnableAgelessPatch = ModConstants.DefaultEnableAgelessPatch;
            EnableDrugsImmunePatch = ModConstants.DefaultEnableDrugsImmunePatch;
            EnableAnimalRegenerationComp = ModConstants.DefaultEnableAnimalRegenerationComp;
            EnableAnimalClottingComp = ModConstants.DefaultEnableAnimalClottingComp;
            EnableNoPorcupineQuillPatch = ModConstants.DefaultEnableNoPorcupineQuillPatch;
            EnableMammalLactation = ModConstants.DefaultEnableMammalLactation;
            EnableAnimalChildcare = ModConstants.DefaultEnableAnimalChildcare;
            EnableAnimalEggProtection = ModConstants.DefaultEnableAnimalEggProtection;
            PreventFleeFromHumansWhileProtectingYoung = ModConstants.DefaultPreventFleeFromHumansWhileProtectingYoung;
            PreventFleeFromHumansWhileProtectingEggClutches = ModConstants.DefaultPreventFleeFromHumansWhileProtectingEggClutches;
            EnableAnimalWoundLicking = ModConstants.DefaultEnableAnimalWoundLicking;
            EnableWildAnimalReproduction = ModConstants.DefaultEnableWildAnimalReproduction;
            EnableCannotChewExtension = ModConstants.DefaultEnableCannotChewExtension;
            EnablePredatorDefendCorpse = ModConstants.DefaultEnablePredatorDefendCorpse;
            EnablePredatorDefendPreyFromHumansAndMechanoids = ModConstants.DefaultEnablePredatorDefendPreyFromHumansAndMechanoids;
            PredatorSearchRadius = ModConstants.DefaultPredatorSearchRadius;
            NonHostilePredatorSearchRadius = ModConstants.DefaultNonHostilePredatorSearchRadius;
            HumanSearchRadius = ModConstants.DefaultHumanSearchRadius;
            FleeDistancePredator = ModConstants.DefaultFleeDistancePredator;
            FleeDistanceTargetPredator = ModConstants.DefaultFleeDistanceTargetPredator;
            FleeDistanceHuman = ModConstants.DefaultFleeDistanceHuman;
            _smallPetBodySizeThreshold = ModConstants.DefaultSmallPetBodySizeThreshold;
            _safePredatorBodySizeThreshold = ModConstants.DefaultSafePredatorBodySizeThreshold;
            _safeNonPredatorBodySizeThreshold = ModConstants.DefaultSafeNonPredatorBodySizeThreshold;
            _minCombatPowerToDefendPreyFromHumans = ModConstants.DefaultMinCombatPowerToDefendPreyFromHumans;
            _minCombatPowerToDefendYoungFromHumansAndMechanoids = ModConstants.DefaultMinCombatPowerToDefendYoungFromHumans;
            PreyProtectionRange = ModConstants.DefaultPreyProtectionRange;
            ChildcareProtectionRange = ModConstants.DefaultChildcareProtectionRange;
            CorpseUnownedSizeMultiplier = ModConstants.DefaultCorpseUnownedSizeMultiplier;
            EnableScavengering = ModConstants.DefaultEnableScavengering;
            AllowSlaughterLactating = ModConstants.DefaultAllowSlaughterLactating;
            AnimalsFreeFromHumansPerAnimal.Clear();
            RoamersPerAnimal.Clear();
            RoamMtbDaysPerAnimal.Clear();
            NonRoamerTrainabilityPerAnimal.Clear();
            AnimalFeatureEnabledOverrides.Clear();
            AnimalFeatureBoolParameterOverrides.Clear();
            AnimalFeatureIntParameterOverrides.Clear();
            AnimalFeatureFloatParameterOverrides.Clear();
            EnableAnimalDamageReduction = !_cePresent;
            EnableAnimalDraftControl = ModConstants.DefaultEnableAnimalDraftControl;
            EnableOverrideCEPenetration = _cePresent ? true : false;
            DisableAllRuntimePatches = ModConstants.DefaultDisableAllRuntimePatches;
            ClampFleeAndThreatSettings();
            ApplyRuntimeDefOverrides();

            Write();
            ZoologyMod.SetRuntimePatchesEnabled(true);
            Messages.Message("Zoology_ResetMessage".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Instance = this;

            Scribe_Values.Look(ref EnableCustomFleeDanger, "EnableCustomFleeDanger", ModConstants.DefaultEnableCustomFleeDanger);
            Scribe_Values.Look(ref EnableIgnoreSmallPetsByRaiders, "EnableIgnoreSmallPetsByRaiders", ModConstants.DefaultEnableIgnoreSmallPetsByRaiders);
            Scribe_Values.Look(ref EnableSmallPetNoMeleeRetaliation, "EnableSmallPetNoMeleeRetaliation", ModConstants.DefaultEnableSmallPetNoMeleeRetaliation);
            Scribe_Values.Look(ref EnablePreyFleeFromPredators, "EnablePreyFleeFromPredators", ModConstants.DefaultEnablePreyFleeFromPredators);
            Scribe_Values.Look(ref AnimalsFleeFromNonHostlePredators, "AnimalsFleeFromNonHostlePredators", ModConstants.DefaultAnimalsFleeFromNonHostilePredators);
            Scribe_Values.Look(ref EnablePackHunt, "EnablePackHunt", ModConstants.DefaultEnablePackHunt);
            Scribe_Values.Look(ref EnableAdvancedPredationLogic, "EnableAdvancedPredationLogic", ModConstants.DefaultEnableAdvancedPredationLogic);
            bool legacyAdvancedPredation = EnableAdvancedPredationLogic;
            Scribe_Values.Look(ref legacyAdvancedPredation, "EnablePredatorOnPredatorHuntCheck", EnableAdvancedPredationLogic);
            if (!legacyAdvancedPredation)
            {
                EnableAdvancedPredationLogic = false;
            }
            Scribe_Values.Look(ref EnableHumanBionicOnAnimal, "EnableHumanBionicOnAnimal", ModConstants.DefaultEnableHumanBionicOnAnimal);
            Scribe_Values.Look(ref EnableCannotBeMutatedProtection, "EnableCannotBeMutatedProtection", ModConstants.DefaultEnableCannotBeMutatedProtection);
            Scribe_Values.Look(ref EnableCannotBeAugmentedProtection, "EnableCannotBeAugmentedProtection", ModConstants.DefaultEnableCannotBeAugmentedProtection);
            Scribe_Values.Look(ref EnableNoFleeExtension, "EnableNoFleeExtension", ModConstants.DefaultEnableNoFleeExtension);
            Scribe_Values.Look(ref EnableFleeFromCarrier, "EnableFleeFromCarrier", ModConstants.DefaultEnableFleeFromCarrier);
            Scribe_Values.Look(ref EnableFlyingFleeStart, "EnableFlyingFleeStart", ModConstants.DefaultEnableFlyingFleeStart);
            Scribe_Values.Look(ref EnableGenderRestrictedAttacks, "EnableGenderRestrictedAttacks", ModConstants.DefaultEnableGenderRestrictedAttacks);
            Scribe_Values.Look(ref EnableEctothermicPatch, "EnableEctothermicPatch", ModConstants.DefaultEnableEctothermicPatch);
            Scribe_Values.Look(ref EnableAgelessPatch, "EnableAgelessPatch", ModConstants.DefaultEnableAgelessPatch);
            Scribe_Values.Look(ref EnableDrugsImmunePatch, "EnableDrugsImmunePatch", ModConstants.DefaultEnableDrugsImmunePatch);
            Scribe_Values.Look(ref EnableAnimalRegenerationComp, "EnableAnimalRegenerationComp", ModConstants.DefaultEnableAnimalRegenerationComp);
            Scribe_Values.Look(ref EnableAnimalClottingComp, "EnableAnimalClottingComp", ModConstants.DefaultEnableAnimalClottingComp);
            Scribe_Values.Look(ref EnableNoPorcupineQuillPatch, "EnableNoPorcupineQuillPatch", ModConstants.DefaultEnableNoPorcupineQuillPatch);
            Scribe_Values.Look(ref EnableCannotChewExtension, "EnableCannotChewExtension", ModConstants.DefaultEnableCannotChewExtension);
            Scribe_Values.Look(ref _smallPetBodySizeThreshold, "SmallPetBodySizeThreshold", ModConstants.DefaultSmallPetBodySizeThreshold);
            Scribe_Values.Look(ref _safePredatorBodySizeThreshold, "SafePredatorBodySizeThreshold", ModConstants.DefaultSafePredatorBodySizeThreshold);
            Scribe_Values.Look(ref _safeNonPredatorBodySizeThreshold, "SafeNonPredatorBodySizeThreshold", ModConstants.DefaultSafeNonPredatorBodySizeThreshold);
            Scribe_Values.Look(ref AnimalsFreeFromHumans, "AnimalsFreeFromHumans", ModConstants.DefaultAnimalsFreeFromHumans);
            Scribe_Values.Look(ref EnableWildAnimalReproduction, "EnableWildAnimalReproduction", ModConstants.DefaultEnableWildAnimalReproduction);
            Scribe_Values.Look(ref EnableAgroAtSlaughter, "EnableAgroAtSlaughter", ModConstants.DefaultEnableAgroAtSlaughter);
            Scribe_Values.Look(ref EnableMammalLactation, "EnableMammalLactation", ModConstants.DefaultEnableMammalLactation);
            Scribe_Values.Look(ref EnableAnimalChildcare, "EnableAnimalChildcare", ModConstants.DefaultEnableAnimalChildcare);
            Scribe_Values.Look(ref EnableAnimalEggProtection, "EnableAnimalEggProtection", ModConstants.DefaultEnableAnimalEggProtection);
            Scribe_Values.Look(ref PreventFleeFromHumansWhileProtectingYoung, "PreventFleeFromHumansWhileProtectingYoung", ModConstants.DefaultPreventFleeFromHumansWhileProtectingYoung);
            Scribe_Values.Look(ref PreventFleeFromHumansWhileProtectingEggClutches, "PreventFleeFromHumansWhileProtectingEggClutches", ModConstants.DefaultPreventFleeFromHumansWhileProtectingEggClutches);
            Scribe_Values.Look(ref EnableAnimalWoundLicking, "EnableAnimalWoundLicking", ModConstants.DefaultEnableAnimalWoundLicking);
            Scribe_Values.Look(ref EnablePredatorDefendCorpse, "EnablePredatorDefendCorpse", ModConstants.DefaultEnablePredatorDefendCorpse);
            Scribe_Values.Look(ref EnablePredatorDefendPreyFromHumansAndMechanoids, "EnablePredatorDefendPreyFromHumansAndMechanoids", ModConstants.DefaultEnablePredatorDefendPreyFromHumansAndMechanoids);
            Scribe_Values.Look(ref PredatorSearchRadius, "PredatorSearchRadius", ModConstants.DefaultPredatorSearchRadius);
            Scribe_Values.Look(ref NonHostilePredatorSearchRadius, "NonHostilePredatorSearchRadius", ModConstants.DefaultNonHostilePredatorSearchRadius);
            Scribe_Values.Look(ref HumanSearchRadius, "HumanSearchRadius", ModConstants.DefaultHumanSearchRadius);
            Scribe_Values.Look(ref FleeDistancePredator, "FleeDistancePredator", ModConstants.DefaultFleeDistancePredator);
            Scribe_Values.Look(ref FleeDistanceTargetPredator, "FleeDistanceTargetPredator", ModConstants.DefaultFleeDistanceTargetPredator);
            Scribe_Values.Look(ref FleeDistanceHuman, "FleeDistanceHuman", ModConstants.DefaultFleeDistanceHuman);
            Scribe_Values.Look(ref PreyProtectionRange, "PreyProtectionRange", ModConstants.DefaultPreyProtectionRange);
            Scribe_Values.Look(ref ChildcareProtectionRange, "ChildcareProtectionRange", ModConstants.DefaultChildcareProtectionRange);
            Scribe_Values.Look(ref CorpseUnownedSizeMultiplier, "CorpseUnownedSizeMultiplier", ModConstants.DefaultCorpseUnownedSizeMultiplier);
            Scribe_Values.Look(ref _minCombatPowerToDefendPreyFromHumans, "MinCombatPowerToDefendPreyFromHumans", ModConstants.DefaultMinCombatPowerToDefendPreyFromHumans);
            Scribe_Values.Look(ref _minCombatPowerToDefendYoungFromHumansAndMechanoids, "MinCombatPowerToDefendYoungFromHumansAndMechanoids", ModConstants.DefaultMinCombatPowerToDefendYoungFromHumans);
            Scribe_Values.Look(ref EnableScavengering, "EnableScavengering", ModConstants.DefaultEnableScavengering);
            Scribe_Values.Look(ref AllowSlaughterLactating, "AllowSlaughterLactating", ModConstants.DefaultAllowSlaughterLactating);
            Scribe_Values.Look(ref DisableAllRuntimePatches, "DisableAllRuntimePatches", ModConstants.DefaultDisableAllRuntimePatches);

            EnsureCollectionsInitialized();
            Scribe_Collections.Look(ref AnimalsFreeFromHumansPerAnimal, "AnimalsFreeFromHumansPerAnimal", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref RoamersPerAnimal, "RoamersPerAnimal", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref RoamMtbDaysPerAnimal, "RoamMtbDaysPerAnimal", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref NonRoamerTrainabilityPerAnimal, "NonRoamerTrainabilityPerAnimal", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref AnimalFeatureEnabledOverrides, "AnimalFeatureEnabledOverrides", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref AnimalFeatureBoolParameterOverrides, "AnimalFeatureBoolParameterOverrides", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref AnimalFeatureIntParameterOverrides, "AnimalFeatureIntParameterOverrides", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref AnimalFeatureFloatParameterOverrides, "AnimalFeatureFloatParameterOverrides", LookMode.Value, LookMode.Value);

            if (!EnableMammalLactation)
            {
                AllowSlaughterLactating = ModConstants.DefaultAllowSlaughterLactating;
            }

            Scribe_Values.Look(ref EnableAnimalDamageReduction, "EnableAnimalDamageReduction", ModConstants.DefaultEnableAnimalDamageReduction);
            if (_cePresent)
            {
                EnableAnimalDamageReduction = false;
            }

            Scribe_Values.Look(ref EnableAnimalDraftControl, "EnableAnimalDraftControl", ModConstants.DefaultEnableAnimalDraftControl);
            Scribe_Values.Look(ref EnableOverrideCEPenetration, "EnableOverrideCEPenetration", ModConstants.DefaultEnableOverrideCEPenetration);
            if (!_cePresent)
            {
                EnableOverrideCEPenetration = false;
            }

            ClampFleeAndThreatSettings();
            _minCombatPowerToDefendPreyFromHumans = Mathf.Clamp(_minCombatPowerToDefendPreyFromHumans, MinCombatPowerToDefendPreyFromHumansMin, MinCombatPowerToDefendPreyFromHumansMax);
            _minCombatPowerToDefendYoungFromHumansAndMechanoids = Mathf.Clamp(_minCombatPowerToDefendYoungFromHumansAndMechanoids, MinCombatPowerToDefendYoungFromHumansMin, MinCombatPowerToDefendYoungFromHumansMax);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Cleanup invalid overrides only after all definitions are loaded
                EnsureCollectionsInitialized();
                CleanupAnimalsFreeFromHumansOverrides();
                CleanupRoamerOverrides();
                CleanupAnimalFeatureOverrides();
                CleanupAnimalFeatureParameterOverrides();
                ApplyRuntimeDefOverrides();
                ZoologyMod.SyncRuntimePatchesWithSettings(forceRebuild: true);
            }
            else
            {
                // During initial loading, just apply overrides without cleanup to preserve modded animals
                ApplyRuntimeDefOverrides();
            }
        }

        private int GetRuntimePatchToggleHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (DisableAllRuntimePatches ? 1 : 0);
                hash = hash * 31 + (EnableCustomFleeDanger ? 1 : 0);
                hash = hash * 31 + (EnableIgnoreSmallPetsByRaiders ? 1 : 0);
                hash = hash * 31 + (EnableSmallPetNoMeleeRetaliation ? 1 : 0);
                hash = hash * 31 + (EnablePreyFleeFromPredators ? 1 : 0);
                hash = hash * 31 + (AnimalsFleeFromNonHostlePredators ? 1 : 0);
                hash = hash * 31 + (AnimalsFreeFromHumans ? 1 : 0);
                hash = hash * 31 + (EnableWildAnimalReproduction ? 1 : 0);
                hash = hash * 31 + (EnablePackHunt ? 1 : 0);
                hash = hash * 31 + (EnableAdvancedPredationLogic ? 1 : 0);
                hash = hash * 31 + (EnableAgroAtSlaughter ? 1 : 0);
                hash = hash * 31 + (EnableCannotBeMutatedProtection ? 1 : 0);
                hash = hash * 31 + (EnableCannotBeAugmentedProtection ? 1 : 0);
                hash = hash * 31 + (EnableNoFleeExtension ? 1 : 0);
                hash = hash * 31 + (EnableFleeFromCarrier ? 1 : 0);
                hash = hash * 31 + (EnableFlyingFleeStart ? 1 : 0);
                hash = hash * 31 + (EnableGenderRestrictedAttacks ? 1 : 0);
                hash = hash * 31 + (EnableCannotChewExtension ? 1 : 0);
                hash = hash * 31 + (EnablePredatorDefendCorpse ? 1 : 0);
                hash = hash * 31 + (EnableScavengering ? 1 : 0);
                hash = hash * 31 + (EnableMammalLactation ? 1 : 0);
                hash = hash * 31 + (EnableAnimalChildcare ? 1 : 0);
                hash = hash * 31 + (EnableAnimalEggProtection ? 1 : 0);
                hash = hash * 31 + (PreventFleeFromHumansWhileProtectingYoung ? 1 : 0);
                hash = hash * 31 + (PreventFleeFromHumansWhileProtectingEggClutches ? 1 : 0);
                hash = hash * 31 + (EnableAnimalWoundLicking ? 1 : 0);
                hash = hash * 31 + (EnableEctothermicPatch ? 1 : 0);
                hash = hash * 31 + (EnableAgelessPatch ? 1 : 0);
                hash = hash * 31 + (EnableDrugsImmunePatch ? 1 : 0);
                hash = hash * 31 + (EnableNoPorcupineQuillPatch ? 1 : 0);
                hash = hash * 31 + (EnableAnimalDamageReduction ? 1 : 0);
                hash = hash * 31 + (EnableAnimalDraftControl ? 1 : 0);
                hash = hash * 31 + (EnableOverrideCEPenetration ? 1 : 0);
                return hash;
            }
        }

        public bool GetAnimalsFreeFromHumansFor(ThingDef animal)
        {
            if (!ZoologyCacheUtility.IsAnimalThingDef(animal))
                return false;

            if (AnimalsFreeFromHumansPerAnimal == null)
                AnimalsFreeFromHumansPerAnimal = new Dictionary<string, bool>();

            if (AnimalsFreeFromHumansPerAnimal.TryGetValue(animal.defName, out bool value))
                return value;

            return !ZoologyCacheUtility.IsExcludedFromHumanFleeByDefault(animal);
        }

        public void SetAnimalsFreeFromHumansFor(ThingDef animal, bool value)
        {
            if (!ZoologyCacheUtility.IsAnimalThingDef(animal))
                return;

            if (AnimalsFreeFromHumansPerAnimal == null)
                AnimalsFreeFromHumansPerAnimal = new Dictionary<string, bool>();

            bool defaultValue = !ZoologyCacheUtility.IsExcludedFromHumanFleeByDefault(animal);
            if (value == defaultValue)
            {
                AnimalsFreeFromHumansPerAnimal.Remove(animal.defName);
            }
            else
            {
                AnimalsFreeFromHumansPerAnimal[animal.defName] = value;
            }

            Write();
        }

        public void ResetAnimalsFreeFromHumansOverrides()
        {
            if (AnimalsFreeFromHumansPerAnimal == null)
                AnimalsFreeFromHumansPerAnimal = new Dictionary<string, bool>();

            AnimalsFreeFromHumansPerAnimal.Clear();
            Write();
        }

        private void CleanupAnimalsFreeFromHumansOverrides()
        {
            if (AnimalsFreeFromHumansPerAnimal == null || AnimalsFreeFromHumansPerAnimal.Count == 0)
            {
                return;
            }

            List<string> invalidKeys = null;
            foreach (KeyValuePair<string, bool> entry in AnimalsFreeFromHumansPerAnimal)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(entry.Key);
                if (ZoologyCacheUtility.IsAnimalThingDef(def))
                {
                    continue;
                }

                if (invalidKeys == null)
                {
                    invalidKeys = new List<string>();
                }

                invalidKeys.Add(entry.Key);
            }

            if (invalidKeys == null)
            {
                return;
            }

            for (int i = 0; i < invalidKeys.Count; i++)
            {
                AnimalsFreeFromHumansPerAnimal.Remove(invalidKeys[i]);
            }
        }

        public bool GetRoamerFor(ThingDef animal)
        {
            if (!ZoologyCacheUtility.IsAnimalThingDef(animal))
            {
                return false;
            }

            EnsureCollectionsInitialized();
            if (RoamersPerAnimal.TryGetValue(animal.defName, out bool value))
            {
                return value;
            }

            return ZoologyRuntimeAnimalOverrides.IsDefaultRoamer(animal);
        }

        public void SetRoamerFor(ThingDef animal, bool value)
        {
            if (!ZoologyCacheUtility.IsAnimalThingDef(animal))
            {
                return;
            }

            EnsureCollectionsInitialized();
            bool defaultValue = ZoologyRuntimeAnimalOverrides.IsDefaultRoamer(animal);
            if (value == defaultValue)
            {
                RoamersPerAnimal.Remove(animal.defName);
            }
            else
            {
                RoamersPerAnimal[animal.defName] = value;
            }

            if (value)
            {
                float? defaultDays = ZoologyRuntimeAnimalOverrides.GetDefaultRoamMtbDays(animal);
                if ((!defaultDays.HasValue || defaultDays.Value <= 0f) && !RoamMtbDaysPerAnimal.ContainsKey(animal.defName))
                {
                    RoamMtbDaysPerAnimal[animal.defName] = 12f;
                }
            }

            ApplyRuntimeDefOverrides();
            Write();
        }

        public float GetRoamMtbDaysFor(ThingDef animal)
        {
            if (!ZoologyCacheUtility.IsAnimalThingDef(animal))
            {
                return 12f;
            }

            EnsureCollectionsInitialized();
            if (RoamMtbDaysPerAnimal.TryGetValue(animal.defName, out float value) && value > 0f)
            {
                return value;
            }

            float? defaultDays = ZoologyRuntimeAnimalOverrides.GetDefaultRoamMtbDays(animal);
            if (defaultDays.HasValue && defaultDays.Value > 0f)
            {
                return defaultDays.Value;
            }

            return 12f;
        }

        public void SetRoamMtbDaysFor(ThingDef animal, float days)
        {
            if (!ZoologyCacheUtility.IsAnimalThingDef(animal))
            {
                return;
            }

            EnsureCollectionsInitialized();
            float clamped = Mathf.Clamp(days, 1f, 120f);
            float? defaultDays = ZoologyRuntimeAnimalOverrides.GetDefaultRoamMtbDays(animal);
            if (defaultDays.HasValue && Mathf.Abs(clamped - defaultDays.Value) < 0.001f)
            {
                RoamMtbDaysPerAnimal.Remove(animal.defName);
            }
            else
            {
                RoamMtbDaysPerAnimal[animal.defName] = clamped;
            }

            ApplyRuntimeDefOverrides();
        }

        public TrainabilityDef GetNonRoamerTrainabilityFor(ThingDef animal)
        {
            if (!ZoologyCacheUtility.IsAnimalThingDef(animal))
            {
                return ZoologyRuntimeAnimalOverrides.GetTrainabilityNone();
            }

            EnsureCollectionsInitialized();
            if (NonRoamerTrainabilityPerAnimal.TryGetValue(animal.defName, out string defName))
            {
                TrainabilityDef trainabilityFromOverride = DefDatabase<TrainabilityDef>.GetNamedSilentFail(defName);
                if (trainabilityFromOverride != null)
                {
                    return trainabilityFromOverride;
                }
            }

            TrainabilityDef defaultTrainability = ZoologyRuntimeAnimalOverrides.GetDefaultTrainability(animal);
            return defaultTrainability ?? ZoologyRuntimeAnimalOverrides.GetTrainabilityNone();
        }

        public void SetNonRoamerTrainabilityFor(ThingDef animal, TrainabilityDef trainability)
        {
            if (!ZoologyCacheUtility.IsAnimalThingDef(animal))
            {
                return;
            }

            EnsureCollectionsInitialized();
            TrainabilityDef none = ZoologyRuntimeAnimalOverrides.GetTrainabilityNone();
            TrainabilityDef target = trainability ?? none;
            TrainabilityDef defaultTrainability = ZoologyRuntimeAnimalOverrides.GetDefaultTrainability(animal) ?? none;

            if (string.Equals(target.defName, defaultTrainability.defName, System.StringComparison.Ordinal))
            {
                NonRoamerTrainabilityPerAnimal.Remove(animal.defName);
            }
            else
            {
                NonRoamerTrainabilityPerAnimal[animal.defName] = target.defName;
            }

            ApplyRuntimeDefOverrides();
        }

        public void ResetRoamerTrainabilityOverrides()
        {
            EnsureCollectionsInitialized();
            RoamersPerAnimal.Clear();
            RoamMtbDaysPerAnimal.Clear();
            NonRoamerTrainabilityPerAnimal.Clear();
            ApplyRuntimeDefOverrides();
            Write();
        }

        public bool GetAnimalFeatureEnabled(string featureId, ThingDef animal)
        {
            if (!ZoologyCacheUtility.IsAnimalThingDef(animal) || !ZoologyRuntimeAnimalOverrides.IsKnownFeatureId(featureId))
            {
                return false;
            }

            EnsureCollectionsInitialized();
            string key = MakeAnimalFeatureOverrideKey(featureId, animal.defName);
            if (AnimalFeatureEnabledOverrides.TryGetValue(key, out bool value))
            {
                return value;
            }

            return ZoologyRuntimeAnimalOverrides.GetDefaultFeaturePresence(featureId, animal);
        }

        public void SetAnimalFeatureEnabled(string featureId, ThingDef animal, bool value)
        {
            if (!ZoologyCacheUtility.IsAnimalThingDef(animal) || !ZoologyRuntimeAnimalOverrides.IsKnownFeatureId(featureId))
            {
                return;
            }

            EnsureCollectionsInitialized();
            bool defaultValue = ZoologyRuntimeAnimalOverrides.GetDefaultFeaturePresence(featureId, animal);
            string key = MakeAnimalFeatureOverrideKey(featureId, animal.defName);
            if (value == defaultValue)
            {
                AnimalFeatureEnabledOverrides.Remove(key);
            }
            else
            {
                AnimalFeatureEnabledOverrides[key] = value;
            }

            ApplyRuntimeDefOverrides();
            Write();
        }

        public bool GetScavengerAllowVeryRottenFor(ThingDef animal)
        {
            bool defaultValue = GetDefaultScavengerAllowVeryRotten(animal);
            return GetFeatureBoolParameter(FeatureScavenger, animal, ParamAllowVeryRotten, defaultValue);
        }

        public void SetScavengerAllowVeryRottenFor(ThingDef animal, bool value)
        {
            bool defaultValue = GetDefaultScavengerAllowVeryRotten(animal);
            SetFeatureBoolParameter(FeatureScavenger, animal, ParamAllowVeryRotten, value, defaultValue);
        }

        public float GetFleeFromCarrierRadiusFor(ThingDef animal)
        {
            float defaultValue = GetDefaultFleeFromCarrierRadius(animal);
            return GetFeatureFloatParameter(FeatureFleeFromCarrier, animal, ParamFleeRadius, defaultValue);
        }

        public void SetFleeFromCarrierRadiusFor(ThingDef animal, float value)
        {
            float clamped = Mathf.Clamp(value, 1f, 60f);
            float defaultValue = GetDefaultFleeFromCarrierRadius(animal);
            SetFeatureFloatParameter(FeatureFleeFromCarrier, animal, ParamFleeRadius, clamped, defaultValue);
        }

        public float GetFleeFromCarrierBodySizeLimitFor(ThingDef animal)
        {
            float defaultValue = GetDefaultFleeFromCarrierBodySizeLimit(animal);
            return GetFeatureFloatParameter(FeatureFleeFromCarrier, animal, ParamFleeBodySizeLimit, defaultValue);
        }

        public void SetFleeFromCarrierBodySizeLimitFor(ThingDef animal, float value)
        {
            float clamped = Mathf.Clamp(value, 0f, 20f);
            float defaultValue = GetDefaultFleeFromCarrierBodySizeLimit(animal);
            SetFeatureFloatParameter(FeatureFleeFromCarrier, animal, ParamFleeBodySizeLimit, clamped, defaultValue);
        }

        public int GetFleeFromCarrierDistanceFor(ThingDef animal)
        {
            int defaultValue = GetDefaultFleeFromCarrierDistance(animal);
            return GetFeatureIntParameter(FeatureFleeFromCarrier, animal, ParamFleeDistance, defaultValue);
        }

        public void SetFleeFromCarrierDistanceFor(ThingDef animal, int value)
        {
            int clamped = Mathf.Clamp(value, 1, 80);
            int defaultValue = GetDefaultFleeFromCarrierDistance(animal);
            SetFeatureIntParameter(FeatureFleeFromCarrier, animal, ParamFleeDistance, clamped, defaultValue);
        }

        public int GetAgelessCleanupIntervalTicksFor(ThingDef animal)
        {
            int defaultValue = GetDefaultAgelessCleanupIntervalTicks(animal);
            return GetFeatureIntParameter(FeatureCompAgeless, animal, ParamCleanupIntervalTicks, defaultValue);
        }

        public void SetAgelessCleanupIntervalTicksFor(ThingDef animal, int value)
        {
            int clamped = Mathf.Clamp(value, 60, 120000);
            int defaultValue = GetDefaultAgelessCleanupIntervalTicks(animal);
            SetFeatureIntParameter(FeatureCompAgeless, animal, ParamCleanupIntervalTicks, clamped, defaultValue);
        }

        public int GetDrugsImmuneCleanupIntervalTicksFor(ThingDef animal)
        {
            int defaultValue = GetDefaultDrugsImmuneCleanupIntervalTicks(animal);
            return GetFeatureIntParameter(FeatureCompDrugsImmune, animal, ParamCleanupIntervalTicks, defaultValue);
        }

        public void SetDrugsImmuneCleanupIntervalTicksFor(ThingDef animal, int value)
        {
            int clamped = Mathf.Clamp(value, 60, 120000);
            int defaultValue = GetDefaultDrugsImmuneCleanupIntervalTicks(animal);
            SetFeatureIntParameter(FeatureCompDrugsImmune, animal, ParamCleanupIntervalTicks, clamped, defaultValue);
        }

        public int GetAnimalClottingCheckIntervalFor(ThingDef animal)
        {
            int defaultValue = GetDefaultAnimalClottingCheckInterval(animal);
            return GetFeatureIntParameter(FeatureCompAnimalClotting, animal, ParamCheckInterval, defaultValue);
        }

        public void SetAnimalClottingCheckIntervalFor(ThingDef animal, int value)
        {
            int clamped = Mathf.Clamp(value, 60, 120000);
            int defaultValue = GetDefaultAnimalClottingCheckInterval(animal);
            SetFeatureIntParameter(FeatureCompAnimalClotting, animal, ParamCheckInterval, clamped, defaultValue);
        }

        public float GetAnimalClottingTendingMinFor(ThingDef animal)
        {
            float defaultValue = GetDefaultAnimalClottingTendingMin(animal);
            return GetFeatureFloatParameter(FeatureCompAnimalClotting, animal, ParamTendingQualityMin, defaultValue);
        }

        public void SetAnimalClottingTendingMinFor(ThingDef animal, float value)
        {
            float clampedMin = Mathf.Clamp(value, 0f, 2f);
            float currentMax = GetAnimalClottingTendingMaxFor(animal);
            float defaultValue = GetDefaultAnimalClottingTendingMin(animal);
            SetFeatureFloatParameter(FeatureCompAnimalClotting, animal, ParamTendingQualityMin, clampedMin, defaultValue);

            if (clampedMin > currentMax)
            {
                float maxDefaultValue = GetDefaultAnimalClottingTendingMax(animal);
                SetFeatureFloatParameter(FeatureCompAnimalClotting, animal, ParamTendingQualityMax, clampedMin, maxDefaultValue);
            }
        }

        public float GetAnimalClottingTendingMaxFor(ThingDef animal)
        {
            float defaultValue = GetDefaultAnimalClottingTendingMax(animal);
            return GetFeatureFloatParameter(FeatureCompAnimalClotting, animal, ParamTendingQualityMax, defaultValue);
        }

        public void SetAnimalClottingTendingMaxFor(ThingDef animal, float value)
        {
            float clampedMax = Mathf.Clamp(value, 0f, 2f);
            float currentMin = GetAnimalClottingTendingMinFor(animal);
            float defaultValue = GetDefaultAnimalClottingTendingMax(animal);
            SetFeatureFloatParameter(FeatureCompAnimalClotting, animal, ParamTendingQualityMax, clampedMax, defaultValue);

            if (clampedMax < currentMin)
            {
                float minDefaultValue = GetDefaultAnimalClottingTendingMin(animal);
                SetFeatureFloatParameter(FeatureCompAnimalClotting, animal, ParamTendingQualityMin, clampedMax, minDefaultValue);
            }
        }

        public void ResetAnimalFeatureOverrides(string featureId)
        {
            if (string.IsNullOrEmpty(featureId))
            {
                return;
            }

            EnsureCollectionsInitialized();
            string prefix = featureId + "|";
            List<string> keysToRemove = null;
            foreach (KeyValuePair<string, bool> entry in AnimalFeatureEnabledOverrides)
            {
                if (!entry.Key.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    continue;
                }

                keysToRemove ??= new List<string>();
                keysToRemove.Add(entry.Key);
            }

            if (keysToRemove != null)
            {
                for (int i = 0; i < keysToRemove.Count; i++)
                {
                    AnimalFeatureEnabledOverrides.Remove(keysToRemove[i]);
                }
            }

            RemoveFeatureParameterOverrides(featureId);

            ApplyRuntimeDefOverrides();
            Write();
        }

        public void ApplyRuntimeDefOverrides()
        {
            EnsureCollectionsInitialized();
            ClampFleeAndThreatSettings();
            ZoologyRuntimeAnimalOverrides.ApplyAll(this);
            Patch_SmallPetThreatDisabled.NotifySettingsChanged();
        }

        private void EnsureCollectionsInitialized()
        {
            AnimalsFreeFromHumansPerAnimal ??= new Dictionary<string, bool>();
            RoamersPerAnimal ??= new Dictionary<string, bool>();
            RoamMtbDaysPerAnimal ??= new Dictionary<string, float>();
            NonRoamerTrainabilityPerAnimal ??= new Dictionary<string, string>();
            AnimalFeatureEnabledOverrides ??= new Dictionary<string, bool>();
            AnimalFeatureBoolParameterOverrides ??= new Dictionary<string, bool>();
            AnimalFeatureIntParameterOverrides ??= new Dictionary<string, int>();
            AnimalFeatureFloatParameterOverrides ??= new Dictionary<string, float>();
        }

        private void ClampFleeAndThreatSettings()
        {
            PredatorSearchRadius = Mathf.Clamp(PredatorSearchRadius, SearchRadiusMin, SearchRadiusMax);
            NonHostilePredatorSearchRadius = Mathf.Clamp(NonHostilePredatorSearchRadius, SearchRadiusMin, SearchRadiusMax);
            HumanSearchRadius = Mathf.Clamp(HumanSearchRadius, SearchRadiusMin, SearchRadiusMax);
            FleeDistancePredator = Mathf.Clamp(FleeDistancePredator, FleeDistanceMin, FleeDistanceMax);
            FleeDistanceTargetPredator = Mathf.Clamp(FleeDistanceTargetPredator, FleeDistanceMin, FleeDistanceMax);
            FleeDistanceHuman = Mathf.Clamp(FleeDistanceHuman, FleeDistanceMin, FleeDistanceMax);
            PreyProtectionRange = Mathf.Clamp(PreyProtectionRange, PreyProtectionRangeMin, PreyProtectionRangeMax);
            ChildcareProtectionRange = Mathf.Clamp(ChildcareProtectionRange, ChildcareProtectionRangeMin, ChildcareProtectionRangeMax);
        }

        private static string MakeAnimalFeatureOverrideKey(string featureId, string defName)
        {
            return featureId + "|" + defName;
        }

        private static string MakeAnimalFeatureParameterOverrideKey(string featureId, string defName, string parameterId)
        {
            return featureId + "|" + defName + "|" + parameterId;
        }

        private bool GetFeatureBoolParameter(string featureId, ThingDef animal, string parameterId, bool defaultValue)
        {
            if (!ZoologyCacheUtility.IsAnimalThingDef(animal) || !IsSupportedBoolFeatureParameter(featureId, parameterId))
            {
                return defaultValue;
            }

            EnsureCollectionsInitialized();
            string key = MakeAnimalFeatureParameterOverrideKey(featureId, animal.defName, parameterId);
            if (AnimalFeatureBoolParameterOverrides.TryGetValue(key, out bool value))
            {
                return value;
            }

            return defaultValue;
        }

        private int GetFeatureIntParameter(string featureId, ThingDef animal, string parameterId, int defaultValue)
        {
            if (!ZoologyCacheUtility.IsAnimalThingDef(animal) || !IsSupportedIntFeatureParameter(featureId, parameterId))
            {
                return defaultValue;
            }

            EnsureCollectionsInitialized();
            string key = MakeAnimalFeatureParameterOverrideKey(featureId, animal.defName, parameterId);
            if (AnimalFeatureIntParameterOverrides.TryGetValue(key, out int value))
            {
                return value;
            }

            return defaultValue;
        }

        private float GetFeatureFloatParameter(string featureId, ThingDef animal, string parameterId, float defaultValue)
        {
            if (!ZoologyCacheUtility.IsAnimalThingDef(animal) || !IsSupportedFloatFeatureParameter(featureId, parameterId))
            {
                return defaultValue;
            }

            EnsureCollectionsInitialized();
            string key = MakeAnimalFeatureParameterOverrideKey(featureId, animal.defName, parameterId);
            if (AnimalFeatureFloatParameterOverrides.TryGetValue(key, out float value))
            {
                return value;
            }

            return defaultValue;
        }

        private void SetFeatureBoolParameter(string featureId, ThingDef animal, string parameterId, bool value, bool defaultValue)
        {
            if (!ZoologyCacheUtility.IsAnimalThingDef(animal) || !IsSupportedBoolFeatureParameter(featureId, parameterId))
            {
                return;
            }

            EnsureCollectionsInitialized();
            string key = MakeAnimalFeatureParameterOverrideKey(featureId, animal.defName, parameterId);
            if (value == defaultValue)
            {
                AnimalFeatureBoolParameterOverrides.Remove(key);
            }
            else
            {
                AnimalFeatureBoolParameterOverrides[key] = value;
            }

            ApplyRuntimeDefOverrides();
        }

        private void SetFeatureIntParameter(string featureId, ThingDef animal, string parameterId, int value, int defaultValue)
        {
            if (!ZoologyCacheUtility.IsAnimalThingDef(animal) || !IsSupportedIntFeatureParameter(featureId, parameterId))
            {
                return;
            }

            EnsureCollectionsInitialized();
            string key = MakeAnimalFeatureParameterOverrideKey(featureId, animal.defName, parameterId);
            if (value == defaultValue)
            {
                AnimalFeatureIntParameterOverrides.Remove(key);
            }
            else
            {
                AnimalFeatureIntParameterOverrides[key] = value;
            }

            ApplyRuntimeDefOverrides();
        }

        private void SetFeatureFloatParameter(string featureId, ThingDef animal, string parameterId, float value, float defaultValue)
        {
            if (!ZoologyCacheUtility.IsAnimalThingDef(animal) || !IsSupportedFloatFeatureParameter(featureId, parameterId))
            {
                return;
            }

            EnsureCollectionsInitialized();
            string key = MakeAnimalFeatureParameterOverrideKey(featureId, animal.defName, parameterId);
            if (Mathf.Abs(value - defaultValue) < 0.0001f)
            {
                AnimalFeatureFloatParameterOverrides.Remove(key);
            }
            else
            {
                AnimalFeatureFloatParameterOverrides[key] = value;
            }

            ApplyRuntimeDefOverrides();
        }

        private bool TryGetDefaultFeatureEntry<T>(string featureId, ThingDef animal, out T entry) where T : class
        {
            entry = null;
            if (!ZoologyCacheUtility.IsAnimalThingDef(animal)
                || !ZoologyRuntimeAnimalOverrides.TryGetDefaultFeatureEntry(featureId, animal, out object entryObject))
            {
                return false;
            }

            entry = entryObject as T;
            return entry != null;
        }

        private bool GetDefaultScavengerAllowVeryRotten(ThingDef animal)
        {
            if (TryGetDefaultFeatureEntry(FeatureScavenger, animal, out ModExtension_IsScavenger scavenger))
            {
                return scavenger.allowVeryRotten;
            }

            return new ModExtension_IsScavenger().allowVeryRotten;
        }

        private float GetDefaultFleeFromCarrierRadius(ThingDef animal)
        {
            if (TryGetDefaultFeatureEntry(FeatureFleeFromCarrier, animal, out ModExtension_FleeFromCarrier flee))
            {
                return flee.fleeRadius;
            }

            return new ModExtension_FleeFromCarrier().fleeRadius;
        }

        private float GetDefaultFleeFromCarrierBodySizeLimit(ThingDef animal)
        {
            if (TryGetDefaultFeatureEntry(FeatureFleeFromCarrier, animal, out ModExtension_FleeFromCarrier flee))
            {
                return flee.fleeBodySizeLimit;
            }

            return new ModExtension_FleeFromCarrier().fleeBodySizeLimit;
        }

        private int GetDefaultFleeFromCarrierDistance(ThingDef animal)
        {
            if (TryGetDefaultFeatureEntry(FeatureFleeFromCarrier, animal, out ModExtension_FleeFromCarrier flee)
                && flee.fleeDistance.HasValue
                && flee.fleeDistance.Value > 0)
            {
                return flee.fleeDistance.Value;
            }

            return new ModExtension_FleeFromCarrier().fleeDistance ?? 16;
        }

        private int GetDefaultAgelessCleanupIntervalTicks(ThingDef animal)
        {
            if (TryGetDefaultFeatureEntry(FeatureCompAgeless, animal, out CompProperties_Ageless props))
            {
                return Mathf.Max(60, props.cleanupIntervalTicks);
            }

            return Mathf.Max(60, new CompProperties_Ageless().cleanupIntervalTicks);
        }

        private int GetDefaultDrugsImmuneCleanupIntervalTicks(ThingDef animal)
        {
            if (TryGetDefaultFeatureEntry(FeatureCompDrugsImmune, animal, out CompProperties_DrugsImmune props))
            {
                return Mathf.Max(60, props.cleanupIntervalTicks);
            }

            return Mathf.Max(60, new CompProperties_DrugsImmune().cleanupIntervalTicks);
        }

        private int GetDefaultAnimalClottingCheckInterval(ThingDef animal)
        {
            if (TryGetDefaultFeatureEntry(FeatureCompAnimalClotting, animal, out CompProperties_AnimalClotting props))
            {
                return Mathf.Max(60, props.checkInterval);
            }

            return Mathf.Max(60, new CompProperties_AnimalClotting().checkInterval);
        }

        private float GetDefaultAnimalClottingTendingMin(ThingDef animal)
        {
            if (TryGetDefaultFeatureEntry(FeatureCompAnimalClotting, animal, out CompProperties_AnimalClotting props))
            {
                return Mathf.Clamp(props.tendingQuality.TrueMin, 0f, 2f);
            }

            return Mathf.Clamp(new CompProperties_AnimalClotting().tendingQuality.TrueMin, 0f, 2f);
        }

        private float GetDefaultAnimalClottingTendingMax(ThingDef animal)
        {
            if (TryGetDefaultFeatureEntry(FeatureCompAnimalClotting, animal, out CompProperties_AnimalClotting props))
            {
                return Mathf.Clamp(props.tendingQuality.TrueMax, 0f, 2f);
            }

            return Mathf.Clamp(new CompProperties_AnimalClotting().tendingQuality.TrueMax, 0f, 2f);
        }

        private static bool IsSupportedBoolFeatureParameter(string featureId, string parameterId)
        {
            return string.Equals(featureId, FeatureScavenger, System.StringComparison.Ordinal)
                && string.Equals(parameterId, ParamAllowVeryRotten, System.StringComparison.Ordinal);
        }

        private static bool IsSupportedIntFeatureParameter(string featureId, string parameterId)
        {
            if (string.Equals(featureId, FeatureFleeFromCarrier, System.StringComparison.Ordinal))
            {
                return string.Equals(parameterId, ParamFleeDistance, System.StringComparison.Ordinal);
            }

            if (string.Equals(featureId, FeatureCompAgeless, System.StringComparison.Ordinal)
                || string.Equals(featureId, FeatureCompDrugsImmune, System.StringComparison.Ordinal))
            {
                return string.Equals(parameterId, ParamCleanupIntervalTicks, System.StringComparison.Ordinal);
            }

            if (string.Equals(featureId, FeatureCompAnimalClotting, System.StringComparison.Ordinal))
            {
                return string.Equals(parameterId, ParamCheckInterval, System.StringComparison.Ordinal);
            }

            return false;
        }

        private static bool IsSupportedFloatFeatureParameter(string featureId, string parameterId)
        {
            if (string.Equals(featureId, FeatureFleeFromCarrier, System.StringComparison.Ordinal))
            {
                return string.Equals(parameterId, ParamFleeRadius, System.StringComparison.Ordinal)
                    || string.Equals(parameterId, ParamFleeBodySizeLimit, System.StringComparison.Ordinal);
            }

            if (string.Equals(featureId, FeatureCompAnimalClotting, System.StringComparison.Ordinal))
            {
                return string.Equals(parameterId, ParamTendingQualityMin, System.StringComparison.Ordinal)
                    || string.Equals(parameterId, ParamTendingQualityMax, System.StringComparison.Ordinal);
            }

            return false;
        }

        private void RemoveFeatureParameterOverrides(string featureId)
        {
            if (string.IsNullOrEmpty(featureId))
            {
                return;
            }

            string prefix = featureId + "|";
            RemoveByPrefix(AnimalFeatureBoolParameterOverrides, prefix);
            RemoveByPrefix(AnimalFeatureIntParameterOverrides, prefix);
            RemoveByPrefix(AnimalFeatureFloatParameterOverrides, prefix);
        }

        private static void RemoveByPrefix<T>(Dictionary<string, T> dict, string prefix)
        {
            if (dict == null || dict.Count == 0)
            {
                return;
            }

            List<string> keysToRemove = null;
            foreach (KeyValuePair<string, T> entry in dict)
            {
                if (!entry.Key.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    continue;
                }

                keysToRemove ??= new List<string>();
                keysToRemove.Add(entry.Key);
            }

            if (keysToRemove == null)
            {
                return;
            }

            for (int i = 0; i < keysToRemove.Count; i++)
            {
                dict.Remove(keysToRemove[i]);
            }
        }

        private void CleanupRoamerOverrides()
        {
            if (RoamersPerAnimal != null && RoamersPerAnimal.Count > 0)
            {
                CleanupPerAnimalBoolOverrides(RoamersPerAnimal);
            }

            if (RoamMtbDaysPerAnimal != null && RoamMtbDaysPerAnimal.Count > 0)
            {
                List<string> invalidRoamDaysKeys = null;
                foreach (KeyValuePair<string, float> entry in RoamMtbDaysPerAnimal)
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(entry.Key);
                    if (ZoologyCacheUtility.IsAnimalThingDef(def) && entry.Value > 0f)
                    {
                        continue;
                    }

                    invalidRoamDaysKeys ??= new List<string>();
                    invalidRoamDaysKeys.Add(entry.Key);
                }

                if (invalidRoamDaysKeys != null)
                {
                    for (int i = 0; i < invalidRoamDaysKeys.Count; i++)
                    {
                        RoamMtbDaysPerAnimal.Remove(invalidRoamDaysKeys[i]);
                    }
                }
            }

            if (NonRoamerTrainabilityPerAnimal != null && NonRoamerTrainabilityPerAnimal.Count > 0)
            {
                List<string> invalidTrainabilityKeys = null;
                foreach (KeyValuePair<string, string> entry in NonRoamerTrainabilityPerAnimal)
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(entry.Key);
                    TrainabilityDef trainability = DefDatabase<TrainabilityDef>.GetNamedSilentFail(entry.Value);
                    if (ZoologyCacheUtility.IsAnimalThingDef(def) && trainability != null)
                    {
                        continue;
                    }

                    invalidTrainabilityKeys ??= new List<string>();
                    invalidTrainabilityKeys.Add(entry.Key);
                }

                if (invalidTrainabilityKeys != null)
                {
                    for (int i = 0; i < invalidTrainabilityKeys.Count; i++)
                    {
                        NonRoamerTrainabilityPerAnimal.Remove(invalidTrainabilityKeys[i]);
                    }
                }
            }
        }

        private void CleanupAnimalFeatureOverrides()
        {
            if (AnimalFeatureEnabledOverrides == null || AnimalFeatureEnabledOverrides.Count == 0)
            {
                return;
            }

            List<string> invalidKeys = null;
            foreach (KeyValuePair<string, bool> entry in AnimalFeatureEnabledOverrides)
            {
                string key = entry.Key;
                int separatorIndex = key.IndexOf('|');
                if (separatorIndex <= 0 || separatorIndex >= key.Length - 1)
                {
                    invalidKeys ??= new List<string>();
                    invalidKeys.Add(key);
                    continue;
                }

                string featureId = key.Substring(0, separatorIndex);
                string defName = key.Substring(separatorIndex + 1);
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (ZoologyRuntimeAnimalOverrides.IsKnownFeatureId(featureId) && ZoologyCacheUtility.IsAnimalThingDef(def))
                {
                    continue;
                }

                invalidKeys ??= new List<string>();
                invalidKeys.Add(key);
            }

            if (invalidKeys == null)
            {
                return;
            }

            for (int i = 0; i < invalidKeys.Count; i++)
            {
                AnimalFeatureEnabledOverrides.Remove(invalidKeys[i]);
            }
        }

        private void CleanupAnimalFeatureParameterOverrides()
        {
            CleanupFeatureParameterOverrides(
                AnimalFeatureBoolParameterOverrides,
                IsSupportedBoolFeatureParameter);
            CleanupFeatureParameterOverrides(
                AnimalFeatureIntParameterOverrides,
                IsSupportedIntFeatureParameter);
            CleanupFeatureParameterOverrides(
                AnimalFeatureFloatParameterOverrides,
                IsSupportedFloatFeatureParameter);
        }

        private static void CleanupFeatureParameterOverrides<T>(
            Dictionary<string, T> overrides,
            System.Func<string, string, bool> isSupportedParameter)
        {
            if (overrides == null || overrides.Count == 0)
            {
                return;
            }

            List<string> invalidKeys = null;
            foreach (KeyValuePair<string, T> entry in overrides)
            {
                string key = entry.Key;
                int firstSeparator = key.IndexOf('|');
                int secondSeparator = firstSeparator >= 0
                    ? key.IndexOf('|', firstSeparator + 1)
                    : -1;
                if (firstSeparator <= 0
                    || secondSeparator <= firstSeparator + 1
                    || secondSeparator >= key.Length - 1)
                {
                    invalidKeys ??= new List<string>();
                    invalidKeys.Add(key);
                    continue;
                }

                string featureId = key.Substring(0, firstSeparator);
                string defName = key.Substring(firstSeparator + 1, secondSeparator - firstSeparator - 1);
                string parameterId = key.Substring(secondSeparator + 1);
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);

                if (!ZoologyRuntimeAnimalOverrides.IsKnownFeatureId(featureId)
                    || !ZoologyCacheUtility.IsAnimalThingDef(def)
                    || !isSupportedParameter(featureId, parameterId))
                {
                    invalidKeys ??= new List<string>();
                    invalidKeys.Add(key);
                }
            }

            if (invalidKeys == null)
            {
                return;
            }

            for (int i = 0; i < invalidKeys.Count; i++)
            {
                overrides.Remove(invalidKeys[i]);
            }
        }

        private static void CleanupPerAnimalBoolOverrides(Dictionary<string, bool> overrides)
        {
            if (overrides == null || overrides.Count == 0)
            {
                return;
            }

            List<string> invalidKeys = null;
            foreach (KeyValuePair<string, bool> entry in overrides)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(entry.Key);
                if (ZoologyCacheUtility.IsAnimalThingDef(def))
                {
                    continue;
                }

                invalidKeys ??= new List<string>();
                invalidKeys.Add(entry.Key);
            }

            if (invalidKeys == null)
            {
                return;
            }

            for (int i = 0; i < invalidKeys.Count; i++)
            {
                overrides.Remove(invalidKeys[i]);
            }
        }

        public bool AreAllRuntimeTogglesDisabled()
        {
            if (DisableAllRuntimePatches)
            {
                return true;
            }

            return !EnableCustomFleeDanger
                && !EnableIgnoreSmallPetsByRaiders
                && (!EnableIgnoreSmallPetsByRaiders || !EnableSmallPetNoMeleeRetaliation)
                && !EnablePreyFleeFromPredators
                && !AnimalsFreeFromHumans
                && !EnableWildAnimalReproduction
                && !EnablePackHunt
                && !EnableAdvancedPredationLogic
                && !EnableHumanBionicOnAnimal
                && !EnableAgroAtSlaughter
                && !EnableCannotBeMutatedProtection
                && !EnableCannotBeAugmentedProtection
                && !EnableNoFleeExtension
                && !EnableFleeFromCarrier
                && !EnableFlyingFleeStart
                && !EnableGenderRestrictedAttacks
                && !EnableCannotChewExtension
                && !EnableEctothermicPatch
                && !EnableAgelessPatch
                && !EnableDrugsImmunePatch
                && !EnableAnimalRegenerationComp
                && !EnableAnimalClottingComp
                && !EnableNoPorcupineQuillPatch
                && !EnableMammalLactation
                && !EnableAnimalChildcare
                && !EnableAnimalEggProtection
                && !EnableAnimalWoundLicking
                && !EnablePredatorDefendCorpse
                && !EnableScavengering
                && !EnableAnimalDamageReduction
                && !EnableAnimalDraftControl
                && !EnableOverrideCEPenetration;
        }
    }
}
