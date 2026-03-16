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

        public bool EnableCustomFleeDanger = true;
        public bool EnableSmallPetFleeFromRaiders = true;
        public bool EnableIgnoreSmallPetsByRaiders = true;
        public bool EnablePreyFleeFromPredators = true;
        public bool AnimalsFleeFromNonHostlePredators = true;
        public bool EnablePackHunt = true;
        public bool EnableAdvancedPredationLogic = true;
        public bool EnableHumanBionicOnAnimal = true;
        public bool EnableAgroAtSlaughter = true;
        public bool AnimalsFreeFromHumans = true;
        public bool EnableCannotBeMutatedProtection = true;
        public bool EnableNoFleeExtension = true;
        public bool EnableFleeFromCarrier = true;
        public bool EnableFlyingFleeStart = true;
        public bool EnableGenderRestrictedAttacks = true;
        public bool EnableEctothermicPatch = true;
        public bool EnableAgelessPatch = true;
        public bool EnableDrugsImmunePatch = true;
        public bool EnableNoPorcupineQuillPatch = true;
        public static bool EnableMammalLactation = true;
        public bool EnablePredatorDefendCorpse = true;
        public bool EnablePredatorDefendPreyFromHumansAndMechanoids = true;
        public bool EnableScavengering = true;

        
        public int PreyProtectionRange = 20; 
        private const int PreyProtectionRangeMin = 10;
        private const int PreyProtectionRangeMax = 30;

        
        public int CorpseUnownedSizeMultiplier = 5; 
        private const int CorpseUnownedSizeMultiplierMin = 2;
        private const int CorpseUnownedSizeMultiplierMax = 10;
        private const int MinCombatPowerToDefendPreyFromHumansMin = 0;
        private const int MinCombatPowerToDefendPreyFromHumansMax = 1000;

        public bool AllowSlaughterLactating = false;
        public Dictionary<string, bool> AnimalsFreeFromHumansPerAnimal = new Dictionary<string, bool>();

        
        
        public bool EnableAnimalDamageReduction = true;

        private float _smallPetBodySizeThreshold = ModConstants.DefaultSmallPetBodySizeThreshold;
        private float _safePredatorBodySizeThreshold = ModConstants.DefaultSafePredatorBodySizeThreshold;
        private float _safeNonPredatorBodySizeThreshold = ModConstants.DefaultSafeNonPredatorBodySizeThreshold;
        private int _minCombatPowerToDefendPreyFromHumans = ModConstants.DefaultMinCombatPowerToDefendPreyFromHumans;

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

        public ZoologyModSettings()
        {
            Instance = this;

            _cePresent = AccessTools.TypeByName("CombatExtended.Verb_MeleeAttackCE") != null;
            EnableOverrideCEPenetration = _cePresent ? true : false;

            EnableAnimalDamageReduction = !_cePresent;

            
            if (AnimalsFreeFromHumansPerAnimal == null)
                AnimalsFreeFromHumansPerAnimal = new Dictionary<string, bool>();
        }

        public void DoWindowContents(Rect inRect)
        {
            var prevFont = Text.Font;
            var prevAnchor = Text.Anchor;

            if (!EnableIgnoreSmallPetsByRaiders)
            {
                EnableSmallPetFleeFromRaiders = true;
            }

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "Zoology Mod Settings");
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

            DrawSettingsTabButton(predPreyTabRect, SettingsPage.PredatorPreyInteraction, "Predator-Prey");
            DrawSettingsTabButton(physiologyTabRect, SettingsPage.Physiology, "Physiology");
            DrawSettingsTabButton(combatTabRect, SettingsPage.Combat, "Combat");
            DrawSettingsTabButton(otherBehaviorTabRect, SettingsPage.OtherBehavior, "Other behavior");
            DrawSettingsTabButton(devTabRect, SettingsPage.Dev, "Dev");

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
            if (list.ButtonText("Reset to defaults"))
            {
                ResetToDefaults();
            }

            list.End();
            Widgets.EndScrollView();

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
                    return 980f
                        + (EnablePreyFleeFromPredators ? 38f : 0f)
                        + (EnablePredatorDefendCorpse ? 156f : 0f);
                case SettingsPage.Physiology:
                    return 900f + (!EnableMammalLactation ? 30f : 0f);
                case SettingsPage.Combat:
                    return 500f;
                case SettingsPage.OtherBehavior:
                    return 1120f
                        + (EnableCustomFleeDanger ? 170f : 0f)
                        + (EnableIgnoreSmallPetsByRaiders ? 150f : 0f)
                        + (AnimalsFreeFromHumans ? 56f : 0f);
                case SettingsPage.Dev:
                default:
                    return 900f;
            }
        }

        private void DrawPredatorPreySettings(Listing_Standard list)
        {
            list.GapLine(8f);
            list.CheckboxLabeled("Enable prey fleeing from predators", ref EnablePreyFleeFromPredators, "Makes prey animals flee from nearby hunting predators.");

            if (EnablePreyFleeFromPredators)
            {
                list.GapLine(6f);
                list.CheckboxLabeled("Animals flee from non-hostile predators", ref AnimalsFleeFromNonHostlePredators, "When enabled, valid prey animals will also flee from nearby predators that are not currently hunting them.");
            }

            list.GapLine(12f);
            list.CheckboxLabeled("Enable pack hunt behavior", ref EnablePackHunt, "Allows nearby herd predators to join an ongoing predator hunt.");

            list.GapLine(12f);
            list.CheckboxLabeled("Enable advanced prey selection logic", ref EnableAdvancedPredationLogic, "Enables the full Zoology override for FoodUtility.IsAcceptablePreyFor (mammal baby checks, pursuit block checks, and predator-vs-predator constraints). Disable for vanilla prey selection behavior.");

            list.GapLine(12f);
            list.CheckboxLabeled("Enable scavengering (scavenger behaviour)", ref EnableScavengering, "If disabled, all scavenger-related fallbacks are skipped and vanilla food selection/reservation logic is used for scavengers.");

            list.GapLine(12f);
            list.CheckboxLabeled("Enable predators defending their kills", ref EnablePredatorDefendCorpse, "If disabled, predators will not defend owned corpses / kills — corpses will be treated as unowned for other predators.");

            if (EnablePredatorDefendCorpse)
            {
                list.GapLine(6f);
                list.Label($"Prey protection range: {PreyProtectionRange} tiles (min {PreyProtectionRangeMin}, max {PreyProtectionRangeMax})");
                PreyProtectionRange = (int)list.Slider(PreyProtectionRange, PreyProtectionRangeMin, PreyProtectionRangeMax);

                list.GapLine(6f);
                list.Label($"Protection trigger size difference treshold: {CorpseUnownedSizeMultiplier} (min {CorpseUnownedSizeMultiplierMin}, max {CorpseUnownedSizeMultiplierMax})");
                CorpseUnownedSizeMultiplier = (int)list.Slider(CorpseUnownedSizeMultiplier, CorpseUnownedSizeMultiplierMin, CorpseUnownedSizeMultiplierMax);

                list.GapLine(10f);
                list.CheckboxLabeled(
                    "Enable predators defending prey from humans and mechanoids",
                    ref EnablePredatorDefendPreyFromHumansAndMechanoids,
                    "If disabled, predators will still defend their prey from other animals, but will ignore humanlike and mechanoid interrupters."
                );

                if (EnablePredatorDefendPreyFromHumansAndMechanoids)
                {
                    list.GapLine(6f);
                    list.Label($"Min CombatPower to defend prey from humans and mechanoids: {MinCombatPowerToDefendPreyFromHumans} (min {MinCombatPowerToDefendPreyFromHumansMin}, max {MinCombatPowerToDefendPreyFromHumansMax})");
                    MinCombatPowerToDefendPreyFromHumans = (int)list.Slider(MinCombatPowerToDefendPreyFromHumans, MinCombatPowerToDefendPreyFromHumansMin, MinCombatPowerToDefendPreyFromHumansMax);
                }
            }
        }

        private void DrawPhysiologySettings(Listing_Standard list)
        {
            list.GapLine(8f);
            list.CheckboxLabeled("Enable mammal lactation", ref EnableMammalLactation, "Enables lactation for female mammals, allowing them to produce milk for their offspring.");
            DrawLactationSettings(list);

            list.GapLine(12f);
            list.CheckboxLabeled(
                "Enable ectothermic temperature patch",
                ref EnableEctothermicPatch,
                "When disabled, ectothermic animals such as reptiles and crabs will suffer hypothermia in cold weather instead of slowing metabolism, like in vanilla."
            );
        }

        private void DrawCombatSettings(Listing_Standard list)
        {
            list.GapLine(8f);
            if (_cePresent)
            {
                list.CheckboxLabeled(
                    "Override CE Penetration for animal life stages",
                    ref EnableOverrideCEPenetration,
                    "When enabled, Zoology will override Combat Extended's life-stage-based AP modifier with custom factors from LifeStagePenetrationDef or fallback table."
                );
            }
            else
            {
                list.Label("Disabled: Combat Extended not detected - CE override unavailable.");
                list.GapLine(6f);
                EnableOverrideCEPenetration = false;
            }

            list.GapLine(12f);
            if (_cePresent)
            {
                EnableAnimalDamageReduction = false;
            }

            bool prevGuiEnabled = GUI.enabled;
            if (_cePresent) GUI.enabled = false;

            list.CheckboxLabeled(
                "Enable animal damage reduction (halve animal-type damage for predators in defined cases)",
                ref EnableAnimalDamageReduction,
                "When enabled, animal damage types (Scratch/Bite and subclasses) will deal 50% damage to predator targets when: (1) target is predator and >=1.5x bodySize of attacker, or (2) target is predator and attacker is not predator."
            );

            GUI.enabled = prevGuiEnabled;

            if (_cePresent)
            {
                list.Label("Disabled: Combat Extended detected - this option is not available while CE is installed.");
                list.GapLine(6f);
            }
        }

        private void DrawOtherBehaviorSettings(Listing_Standard list)
        {
            list.GapLine(8f);
            list.CheckboxLabeled("Enable custom animal flee danger logic (override ShouldAnimalFleeDanger)", ref EnableCustomFleeDanger, "Replaces the vanilla logic for when animals flee from danger on player home maps.");

            if (EnableCustomFleeDanger)
            {
                list.GapLine(6f);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                list.Label("Safe Body Size Thresholds (min: 0, max: 30)");
                Text.Anchor = TextAnchor.UpperLeft;
                list.GapLine(12f);

                _safePredatorBodySizeThreshold = list.Slider(_safePredatorBodySizeThreshold, 0f, 30f);
                list.Label($"Safe Predator Body Size Threshold: {SafePredatorBodySizeThreshold:F2} (predators >= this won't flee on home map)");
                list.GapLine(12f);

                _safeNonPredatorBodySizeThreshold = list.Slider(_safeNonPredatorBodySizeThreshold, 0f, 30f);
                list.Label($"Safe Non-Predator Body Size Threshold: {SafeNonPredatorBodySizeThreshold:F2} (non-predators > this won't flee on home map)");
                list.GapLine(12f);
            }

            list.GapLine(12f);
            list.CheckboxLabeled("Enable raiders ignoring small pets", ref EnableIgnoreSmallPetsByRaiders, "Makes raiders treat small pets (not following the master) as non-aggressive roamers, reducing targeting.");

            if (EnableIgnoreSmallPetsByRaiders)
            {
                list.GapLine(12f);
                list.CheckboxLabeled("Enable small pets fleeing from raiders", ref EnableSmallPetFleeFromRaiders, "Allows small pets to flee from nearby hostile humanlike pawns (raiders), even when not threatened.");

                list.GapLine(6f);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                list.Label("Small Pet Body Size Threshold (min: 0, max: 30)");
                Text.Anchor = TextAnchor.UpperLeft;
                list.GapLine(12f);

                _smallPetBodySizeThreshold = list.Slider(_smallPetBodySizeThreshold, 0f, 30f);
                list.Label($"Small Pet Body Size Threshold: {SmallPetBodySizeThreshold:F2} (pets smaller than this will be affected by raider ignoring)");
                list.GapLine(12f);
            }
            else
            {
                EnableSmallPetFleeFromRaiders = true;
            }

            list.GapLine(12f);
            list.CheckboxLabeled("Animals flee from humans", ref AnimalsFreeFromHumans, "Wild animals with no faction can flee from nearby humanlikes, unless excluded by species settings or blocked by active aggressive behavior.");

            if (AnimalsFreeFromHumans)
            {
                list.GapLine(6f);
                if (list.ButtonText("Configure animals fleeing from humans"))
                {
                    Find.WindowStack.Add(new Dialog_AnimalsFreeFromHumansSelector(this));
                }
            }

            list.GapLine(12f);
            list.CheckboxLabeled("Enable human bionic on animals", ref EnableHumanBionicOnAnimal, "Allows installing human bionic parts on animals if they have the matching body part. Requires restart to apply changes.");

            list.GapLine(12f);
            list.CheckboxLabeled("Enable agro-on-slaughter", ref EnableAgroAtSlaughter, "When enabled, animals with the AgroAtSlaughter comp will react aggressively to slaughter designations (only if not downed).");
        }

        private void DrawDevSettings(Listing_Standard list)
        {
            list.GapLine(8f);
            var oldColor = GUI.color;
            GUI.color = new Color(1f, 0.82f, 0.28f, 1f);
            list.Label("Warning: disable these patches only when troubleshooting compatibility problems.");
            GUI.color = oldColor;
            list.GapLine(4f);
            Text.Font = GameFont.Tiny;
            list.Label("Any enabled Dev patch keeps runtime patching active (global runtime-off mode requires all toggles to be false).");
            Text.Font = GameFont.Small;

            list.GapLine(12f);
            list.CheckboxLabeled("Enable cannot-be-mutated protection", ref EnableCannotBeMutatedProtection, "When disabled, Zoology will not patch mutation/biomutation target validation and related mutation protection hooks.");

            list.GapLine(12f);
            list.CheckboxLabeled("Enable no-flee extension behavior", ref EnableNoFleeExtension, "When disabled, Zoology will not patch NoFlee extension behavior in flee and panic mental-state logic.");

            list.GapLine(12f);
            list.CheckboxLabeled("Enable flee-from-carrier behavior", ref EnableFleeFromCarrier, "When disabled, Zoology will not patch carrier-based flee behavior.");

            list.GapLine(12f);
            list.CheckboxLabeled("Enable flying flee start patch", ref EnableFlyingFleeStart, "When disabled, Zoology will not patch Pawn_FlightTracker.Notify_JobStarted for forced flying flee.");

            list.GapLine(12f);
            list.CheckboxLabeled("Enable gender-restricted attacks patch", ref EnableGenderRestrictedAttacks, "When disabled, Zoology will not patch Verb.IsStillUsableBy for tool gender restrictions.");

            list.GapLine(12f);
            list.CheckboxLabeled("Enable ageless hediff patch", ref EnableAgelessPatch, "When disabled, Zoology will unpatch HediffGiver.TryApply interception for CompAgeless.");

            list.GapLine(12f);
            list.CheckboxLabeled("Enable drugs-immune ingestion patch", ref EnableDrugsImmunePatch, "When disabled, Zoology will unpatch ingestion outcome interception for CompDrugsImmune.");

            list.GapLine(12f);
            list.CheckboxLabeled("Enable no-porcupine-quill patch", ref EnableNoPorcupineQuillPatch, "When disabled, Zoology will unpatch PorcupineQuill prevention/removal hooks.");
        }

        private void DrawLactationSettings(Listing_Standard list)
        {
            list.GapLine(12f);
            list.Label("Lactation auto-slaughter:");

            bool prevGuiEnabledForLact = GUI.enabled;
            if (!EnableMammalLactation)
            {
                GUI.enabled = false;
                AllowSlaughterLactating = false;
            }

            list.CheckboxLabeled(
                "Slaughter lactating females",
                ref AllowSlaughterLactating,
                "If enabled, lactating females are treated as ordinary non-pregnant females. If disabled, lactating females are ignored and do not count toward female totals while lactating."
            );

            GUI.enabled = prevGuiEnabledForLact;

            if (!EnableMammalLactation)
            {
                Text.Font = GameFont.Tiny;
                list.Label("Disabled: mammal lactation is turned off.");
                Text.Font = GameFont.Small;
                list.GapLine(6f);
            }
        }

        private void ResetToDefaults()
        {
            EnableCustomFleeDanger = true;
            EnableSmallPetFleeFromRaiders = true;
            EnableIgnoreSmallPetsByRaiders = true;
            EnablePreyFleeFromPredators = true;
            AnimalsFleeFromNonHostlePredators = true;
            EnablePackHunt = true;
            EnableAdvancedPredationLogic = true;
            EnableHumanBionicOnAnimal = true;
            EnableAgroAtSlaughter = true;
            AnimalsFreeFromHumans = true;
            EnableCannotBeMutatedProtection = true;
            EnableNoFleeExtension = true;
            EnableFleeFromCarrier = true;
            EnableFlyingFleeStart = true;
            EnableGenderRestrictedAttacks = true;
            EnableEctothermicPatch = true;
            EnableAgelessPatch = true;
            EnableDrugsImmunePatch = true;
            EnableNoPorcupineQuillPatch = true;
            EnableMammalLactation = true;
            EnablePredatorDefendCorpse = true;
            EnablePredatorDefendPreyFromHumansAndMechanoids = true;
            _smallPetBodySizeThreshold = ModConstants.DefaultSmallPetBodySizeThreshold;
            _safePredatorBodySizeThreshold = ModConstants.DefaultSafePredatorBodySizeThreshold;
            _safeNonPredatorBodySizeThreshold = ModConstants.DefaultSafeNonPredatorBodySizeThreshold;
            _minCombatPowerToDefendPreyFromHumans = ModConstants.DefaultMinCombatPowerToDefendPreyFromHumans;
            PreyProtectionRange = 20;
            CorpseUnownedSizeMultiplier = 5;
            EnableScavengering = true;
            AllowSlaughterLactating = false;
            AnimalsFreeFromHumansPerAnimal.Clear();
            EnableAnimalDamageReduction = !_cePresent;
            EnableOverrideCEPenetration = _cePresent ? true : false;

            Write();
            Messages.Message("Zoology Mod: settings reset to defaults.", MessageTypeDefOf.TaskCompletion, false);
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Instance = this;

            Scribe_Values.Look(ref EnableCustomFleeDanger, "EnableCustomFleeDanger", true);
            Scribe_Values.Look(ref EnableSmallPetFleeFromRaiders, "EnableSmallPetFleeFromRaiders", true);
            Scribe_Values.Look(ref EnableIgnoreSmallPetsByRaiders, "EnableIgnoreSmallPetsByRaiders", true);
            if (!EnableIgnoreSmallPetsByRaiders)
            {
                EnableSmallPetFleeFromRaiders = true;
            }
            Scribe_Values.Look(ref EnablePreyFleeFromPredators, "EnablePreyFleeFromPredators", true);
            Scribe_Values.Look(ref AnimalsFleeFromNonHostlePredators, "AnimalsFleeFromNonHostlePredators", true);
            Scribe_Values.Look(ref EnablePackHunt, "EnablePackHunt", true);
            Scribe_Values.Look(ref EnableAdvancedPredationLogic, "EnableAdvancedPredationLogic", true);
            bool legacyAdvancedPredation = EnableAdvancedPredationLogic;
            Scribe_Values.Look(ref legacyAdvancedPredation, "EnablePredatorOnPredatorHuntCheck", EnableAdvancedPredationLogic);
            if (!legacyAdvancedPredation)
            {
                EnableAdvancedPredationLogic = false;
            }
            Scribe_Values.Look(ref EnableHumanBionicOnAnimal, "EnableHumanBionicOnAnimal", true);
            Scribe_Values.Look(ref EnableCannotBeMutatedProtection, "EnableCannotBeMutatedProtection", true);
            Scribe_Values.Look(ref EnableNoFleeExtension, "EnableNoFleeExtension", true);
            Scribe_Values.Look(ref EnableFleeFromCarrier, "EnableFleeFromCarrier", true);
            Scribe_Values.Look(ref EnableFlyingFleeStart, "EnableFlyingFleeStart", true);
            Scribe_Values.Look(ref EnableGenderRestrictedAttacks, "EnableGenderRestrictedAttacks", true);
            Scribe_Values.Look(ref EnableEctothermicPatch, "EnableEctothermicPatch", true);
            Scribe_Values.Look(ref EnableAgelessPatch, "EnableAgelessPatch", true);
            Scribe_Values.Look(ref EnableDrugsImmunePatch, "EnableDrugsImmunePatch", true);
            Scribe_Values.Look(ref EnableNoPorcupineQuillPatch, "EnableNoPorcupineQuillPatch", true);
            Scribe_Values.Look(ref _smallPetBodySizeThreshold, "SmallPetBodySizeThreshold", ModConstants.DefaultSmallPetBodySizeThreshold);
            Scribe_Values.Look(ref _safePredatorBodySizeThreshold, "SafePredatorBodySizeThreshold", ModConstants.DefaultSafePredatorBodySizeThreshold);
            Scribe_Values.Look(ref _safeNonPredatorBodySizeThreshold, "SafeNonPredatorBodySizeThreshold", ModConstants.DefaultSafeNonPredatorBodySizeThreshold);
            Scribe_Values.Look(ref AnimalsFreeFromHumans, "AnimalsFreeFromHumans", true);
            Scribe_Values.Look(ref EnableAgroAtSlaughter, "EnableAgroAtSlaughter", true);
            Scribe_Values.Look(ref EnableMammalLactation, "EnableMammalLactation", true);
            Scribe_Values.Look(ref EnablePredatorDefendCorpse, "EnablePredatorDefendCorpse", true);
            Scribe_Values.Look(ref EnablePredatorDefendPreyFromHumansAndMechanoids, "EnablePredatorDefendPreyFromHumansAndMechanoids", true);
            Scribe_Values.Look(ref PreyProtectionRange, "PreyProtectionRange", 20);
            Scribe_Values.Look(ref CorpseUnownedSizeMultiplier, "CorpseUnownedSizeMultiplier", 5);
            Scribe_Values.Look(ref _minCombatPowerToDefendPreyFromHumans, "MinCombatPowerToDefendPreyFromHumans", ModConstants.DefaultMinCombatPowerToDefendPreyFromHumans);
            Scribe_Values.Look(ref EnableScavengering, "EnableScavengering", true);
            Scribe_Values.Look(ref AllowSlaughterLactating, "AllowSlaughterLactating", false);

            
            if (AnimalsFreeFromHumansPerAnimal == null)
                AnimalsFreeFromHumansPerAnimal = new Dictionary<string, bool>();
            Scribe_Collections.Look(ref AnimalsFreeFromHumansPerAnimal, "AnimalsFreeFromHumansPerAnimal", LookMode.Value, LookMode.Value);
            CleanupAnimalsFreeFromHumansOverrides();

            
            if (!EnableMammalLactation)
            {
                AllowSlaughterLactating = false;
            }

            Scribe_Values.Look(ref EnableAnimalDamageReduction, "EnableAnimalDamageReduction", true);
            if (_cePresent)
            {
                EnableAnimalDamageReduction = false;
            }

            Scribe_Values.Look(ref EnableOverrideCEPenetration, "EnableOverrideCEPenetration", false);
            if (!_cePresent)
            {
                EnableOverrideCEPenetration = false;
            }

            _minCombatPowerToDefendPreyFromHumans = Mathf.Clamp(_minCombatPowerToDefendPreyFromHumans, MinCombatPowerToDefendPreyFromHumansMin, MinCombatPowerToDefendPreyFromHumansMax);
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
                return;
            }

            AnimalsFreeFromHumansPerAnimal[animal.defName] = value;
        }

        public void ResetAnimalsFreeFromHumansOverrides()
        {
            if (AnimalsFreeFromHumansPerAnimal == null)
                AnimalsFreeFromHumansPerAnimal = new Dictionary<string, bool>();

            AnimalsFreeFromHumansPerAnimal.Clear();
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

        public bool AreAllRuntimeTogglesDisabled()
        {
            return !EnableCustomFleeDanger
                && !EnableIgnoreSmallPetsByRaiders
                && (!EnableIgnoreSmallPetsByRaiders || !EnableSmallPetFleeFromRaiders)
                && !EnablePreyFleeFromPredators
                && !AnimalsFreeFromHumans
                && !EnablePackHunt
                && !EnableAdvancedPredationLogic
                && !EnableHumanBionicOnAnimal
                && !EnableAgroAtSlaughter
                && !EnableCannotBeMutatedProtection
                && !EnableNoFleeExtension
                && !EnableFleeFromCarrier
                && !EnableFlyingFleeStart
                && !EnableGenderRestrictedAttacks
                && !EnableEctothermicPatch
                && !EnableAgelessPatch
                && !EnableDrugsImmunePatch
                && !EnableNoPorcupineQuillPatch
                && !EnableMammalLactation
                && !EnablePredatorDefendCorpse
                && !EnableScavengering
                && !EnableAnimalDamageReduction
                && !EnableOverrideCEPenetration;
        }
    }
}
