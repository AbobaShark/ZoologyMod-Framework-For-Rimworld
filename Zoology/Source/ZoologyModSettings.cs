

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

        public bool EnableCustomFleeDanger = false;
        public bool EnableSmallPetFleeFromRaiders = true;
        public bool EnableIgnoreSmallPetsByRaiders = true;
        public bool EnablePreyFleeFromPredators = true;
        public bool EnablePredatorOnPredatorHuntCheck = true;
        public bool EnableHumanBionicOnAnimal = true;
        public bool EnableAgroAtSlaughter = true;
        public static bool EnableMammalLactation = true;
        public bool EnablePredatorDefendCorpse = true;
        public bool EnableScavengering = true;

        
        public int PreyProtectionRange = 20; 
        private const int PreyProtectionRangeMin = 10;
        private const int PreyProtectionRangeMax = 30;

        
        public int CorpseUnownedSizeMultiplier = 5; 
        private const int CorpseUnownedSizeMultiplierMin = 2;
        private const int CorpseUnownedSizeMultiplierMax = 10;

        public enum LactationSlaughterMode
        {
            TreatAsPregnant = 0,   
            SeparateSetting = 1,   
            Ignore = 2,            
            DisableSlaughterLactatingGlobal = 3 
        }
        public LactationSlaughterMode LactationSlaughterHandling = LactationSlaughterMode.TreatAsPregnant;

        
        
        
        public Dictionary<string, bool> AllowSlaughterLactatingPerAnimal = new Dictionary<string, bool>();

        
        
        public bool EnableAnimalDamageReduction = false;

        private float _smallPetBodySizeThreshold = ModConstants.DefaultSmallPetBodySizeThreshold;
        private float _safePredatorBodySizeThreshold = ModConstants.DefaultSafePredatorBodySizeThreshold;
        private float _safeNonPredatorBodySizeThreshold = ModConstants.DefaultSafeNonPredatorBodySizeThreshold;

        private Vector2 _scrollPosition = Vector2.zero;

        private readonly bool _cePresent;

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

        public ZoologyModSettings()
        {
            Instance = this;

            _cePresent = AccessTools.TypeByName("CombatExtended.Verb_MeleeAttackCE") != null;
            EnableOverrideCEPenetration = _cePresent ? true : false;

            EnableAnimalDamageReduction = false;
            if (_cePresent)
            {
                EnableAnimalDamageReduction = false;
            }

            
            if (AllowSlaughterLactatingPerAnimal == null)
                AllowSlaughterLactatingPerAnimal = new Dictionary<string, bool>();
        }

        public void DoWindowContents(Rect inRect)
        {
            var prevFont = Text.Font;
            var prevAnchor = Text.Anchor;

            float contentHeight = 1500f;
            contentHeight += 7 * 28f;
            contentHeight += 24f;
            if (EnableCustomFleeDanger)
            {
                contentHeight += 160f;
            }
            if (EnableIgnoreSmallPetsByRaiders)
            {
                contentHeight += 120f;
            }
            contentHeight += 120f;
            if (EnablePredatorDefendCorpse) contentHeight += 48f;

            float viewHeight = Mathf.Max(contentHeight, inRect.height);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, Mathf.Max(inRect.height, 2000f));

            Widgets.BeginScrollView(inRect, ref _scrollPosition, viewRect);

            var list = new Listing_Standard();
            list.Begin(viewRect);

            list.GapLine(12f);
            Text.Font = GameFont.Medium;
            list.Label("Zoology Mod Settings");
            Text.Font = GameFont.Small;
            list.GapLine(12f);

            if (!EnableIgnoreSmallPetsByRaiders)
            {
                EnableSmallPetFleeFromRaiders = false;
            }

            list.GapLine(8f);
            list.CheckboxLabeled("Enable custom animal flee danger logic (override ShouldAnimalFleeDanger)", ref EnableCustomFleeDanger, "Replaces the vanilla logic for when animals flee from danger on player home maps.");

            list.GapLine(12f);
            list.CheckboxLabeled("Enable raiders ignoring small pets", ref EnableIgnoreSmallPetsByRaiders, "Makes raiders treat small pets (not following the master) as non-aggressive roamers, reducing targeting.");

            list.GapLine(12f);
            if (EnableIgnoreSmallPetsByRaiders)
            {
                list.CheckboxLabeled("Enable small pets fleeing from raiders", ref EnableSmallPetFleeFromRaiders, "Allows small pets to flee from nearby hostile humanlike pawns (raiders), even when not threatened.");
            }

            list.GapLine(12f);
            list.CheckboxLabeled("Enable prey fleeing from predators", ref EnablePreyFleeFromPredators, "Makes prey animals flee from nearby hunting predators.");

            list.GapLine(12f);
            list.CheckboxLabeled("Enable predator-on-predator hunt check", ref EnablePredatorOnPredatorHuntCheck, "Predators only hunt other predators if their combat power is at least 4/3 times stronger.");

            list.GapLine(12f);
            list.CheckboxLabeled("Enable predators defending their kills", ref EnablePredatorDefendCorpse, "If disabled, predators will not defend owned corpses / kills — corpses will be treated as unowned for other predators.");

            list.GapLine(6f);
            if (EnablePredatorDefendCorpse)
            {
                Text.Font = GameFont.Small;
                list.Label($"Prey protection range: {PreyProtectionRange} tiles (min {PreyProtectionRangeMin}, max {PreyProtectionRangeMax})");
                PreyProtectionRange = (int)list.Slider(PreyProtectionRange, PreyProtectionRangeMin, PreyProtectionRangeMax);
                Text.Font = GameFont.Small;
                list.GapLine(6f);
            }

            list.GapLine(6f);
            if (EnablePredatorDefendCorpse)
            {
                Text.Font = GameFont.Small;
                list.Label($"Protection trigger size difference treshold: {CorpseUnownedSizeMultiplier} (min {CorpseUnownedSizeMultiplierMin}, max {CorpseUnownedSizeMultiplierMax})");
                CorpseUnownedSizeMultiplier = (int)list.Slider(CorpseUnownedSizeMultiplier, CorpseUnownedSizeMultiplierMin, CorpseUnownedSizeMultiplierMax);
                Text.Font = GameFont.Small;
                list.GapLine(6f);
            }

            list.GapLine(12f);
            list.CheckboxLabeled("Enable scavengering (scavenger behaviour)", ref EnableScavengering, "If disabled, all scavenger-related fallbacks are skipped and vanilla food selection/reservation logic is used for scavengers.");

            list.GapLine(12f);
            list.CheckboxLabeled("Enable human bionic on animals", ref EnableHumanBionicOnAnimal, "Allows installing human bionic parts on animals if they have the matching body part. Requires restart to apply changes.");

            list.GapLine(12f);
            list.CheckboxLabeled("Enable agro-on-slaughter", ref EnableAgroAtSlaughter, "When enabled, animals with the AgroAtSlaughter comp will react aggressively to slaughter designations (only if not downed).");

            list.GapLine(12f);
            list.CheckboxLabeled("Enable mammal lactation", ref EnableMammalLactation, "Enables lactation for female mammals, allowing them to produce milk for their offspring.");

            list.GapLine(12f);
            list.Label("Lactation auto-slaughter handling:");

            
            if (!EnableMammalLactation)
            {
                LactationSlaughterHandling = LactationSlaughterMode.Ignore;
                
            }

            bool prevGuiEnabledForLact = GUI.enabled;
            if (!EnableMammalLactation)
                GUI.enabled = false;

            
            if (list.ButtonText(LactationSlaughterHandling == LactationSlaughterMode.TreatAsPregnant ? "● Treat lactating animals as PREGNANT (default)" : "○ Treat lactating animals as PREGNANT (default)"))
            {
                LactationSlaughterHandling = LactationSlaughterMode.TreatAsPregnant;
            }
            if (list.ButtonText(LactationSlaughterHandling == LactationSlaughterMode.SeparateSetting ? "● Use separate per-animal setting (Auto Slaughter tab)" : "○ Use separate per-animal setting (Auto Slaughter tab)"))
            {
                LactationSlaughterHandling = LactationSlaughterMode.SeparateSetting;
            }
            if (list.ButtonText(LactationSlaughterHandling == LactationSlaughterMode.Ignore ? "● Ignore lactation (vanilla behavior)" : "○ Ignore lactation (vanilla behavior)"))
            {
                LactationSlaughterHandling = LactationSlaughterMode.Ignore;
            }
            if (list.ButtonText(LactationSlaughterHandling == LactationSlaughterMode.DisableSlaughterLactatingGlobal ? "● Globally DISABLE slaughter of lactating animals" : "○ Globally DISABLE slaughter of lactating animals"))
            {
                LactationSlaughterHandling = LactationSlaughterMode.DisableSlaughterLactatingGlobal;
            }

            list.GapLine(6f);
            if (LactationSlaughterHandling == LactationSlaughterMode.SeparateSetting)
            {
                
                list.Label("Per-animal toggle available in Auto Slaughter tab when SeparateSetting is enabled.");
                list.GapLine(6f);
            }

            GUI.enabled = prevGuiEnabledForLact;

            if (!EnableMammalLactation)
            {
                Text.Font = GameFont.Tiny;
                list.Label("Disabled: mammal lactation is turned off, handling forced to Ignore.");
                Text.Font = GameFont.Small;
                list.GapLine(6f);
            }

            
            list.GapLine(16f);

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
                
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                list.Label("Disabled: Combat Extended not detected — CE override unavailable.");
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                list.GapLine(6f);

                
                EnableOverrideCEPenetration = false;
            }

            
            list.GapLine(12f);

            if (_cePresent)
            {
                EnableAnimalDamageReduction = false;
            }

            bool prevGuiEnabled = GUI.enabled;
            if (_cePresent)
            {
                GUI.enabled = false;
            }

            list.CheckboxLabeled(
                "Enable animal damage reduction (halve animal-type damage for predators in defined cases)",
                ref EnableAnimalDamageReduction,
                "When enabled, animal damage types (Scratch/Bite and subclasses) will deal 50% damage to predator targets when: (1) target is predator and >=1.5x bodySize of attacker, or (2) target is predator and attacker is not predator."
            );

            GUI.enabled = prevGuiEnabled;

            if (_cePresent)
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                list.Label("Disabled: Combat Extended detected — this option is not available while CE is installed.");
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                list.GapLine(6f);
            }

            list.GapLine(18f);

            if (EnableCustomFleeDanger)
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                list.Label("Safe Body Size Thresholds (min: 0, max: 30)");
                Text.Anchor = TextAnchor.UpperLeft;
                list.GapLine(16f);

                _safePredatorBodySizeThreshold = list.Slider(_safePredatorBodySizeThreshold, 0f, 30f);
                list.Label($"Safe Predator Body Size Threshold: {SafePredatorBodySizeThreshold:F2} (predators >= this won't flee on home map)");
                list.GapLine(20f);

                _safeNonPredatorBodySizeThreshold = list.Slider(_safeNonPredatorBodySizeThreshold, 0f, 30f);
                list.Label($"Safe Non-Predator Body Size Threshold: {SafeNonPredatorBodySizeThreshold:F2} (non-predators > this won't flee on home map)");
                list.GapLine(20f);
            }

            if (EnableIgnoreSmallPetsByRaiders)
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                list.Label("Small Pet Body Size Threshold (min: 0, max: 30)");
                Text.Anchor = TextAnchor.UpperLeft;
                list.GapLine(16f);

                _smallPetBodySizeThreshold = list.Slider(_smallPetBodySizeThreshold, 0f, 30f);
                list.Label($"Small Pet Body Size Threshold: {SmallPetBodySizeThreshold:F2} (pets smaller than this will be affected by raider ignoring)");
                list.GapLine(20f);
            }

            list.GapLine(24f);
            if (list.ButtonText("Reset to defaults"))
            {
                EnableCustomFleeDanger = false;
                EnableSmallPetFleeFromRaiders = true;
                EnableIgnoreSmallPetsByRaiders = true;
                EnablePreyFleeFromPredators = true;
                EnablePredatorOnPredatorHuntCheck = true;
                EnableHumanBionicOnAnimal = true;
                EnableMammalLactation = true;
                _smallPetBodySizeThreshold = ModConstants.DefaultSmallPetBodySizeThreshold;
                _safePredatorBodySizeThreshold = ModConstants.DefaultSafePredatorBodySizeThreshold;
                _safeNonPredatorBodySizeThreshold = ModConstants.DefaultSafeNonPredatorBodySizeThreshold;
                EnablePredatorDefendCorpse = true;
                PreyProtectionRange = 20;
                CorpseUnownedSizeMultiplier  = 5;
                EnableScavengering= true;
                LactationSlaughterHandling = LactationSlaughterMode.TreatAsPregnant;
                AllowSlaughterLactatingPerAnimal.Clear();
                EnableAnimalDamageReduction = false;
                EnableOverrideCEPenetration = _cePresent ? true : false;

                Write();

                Messages.Message("Zoology Mod: settings reset to defaults.", MessageTypeDefOf.TaskCompletion, false);
            }

            list.End();
            Widgets.EndScrollView();

            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Instance = this;

            Scribe_Values.Look(ref EnableCustomFleeDanger, "EnableCustomFleeDanger", false);
            Scribe_Values.Look(ref EnableSmallPetFleeFromRaiders, "EnableSmallPetFleeFromRaiders", true);
            Scribe_Values.Look(ref EnableIgnoreSmallPetsByRaiders, "EnableIgnoreSmallPetsByRaiders", true);
            Scribe_Values.Look(ref EnablePreyFleeFromPredators, "EnablePreyFleeFromPredators", true);
            Scribe_Values.Look(ref EnablePredatorOnPredatorHuntCheck, "EnablePredatorOnPredatorHuntCheck", true);
            Scribe_Values.Look(ref EnableHumanBionicOnAnimal, "EnableHumanBionicOnAnimal", true);
            Scribe_Values.Look(ref _smallPetBodySizeThreshold, "SmallPetBodySizeThreshold", ModConstants.DefaultSmallPetBodySizeThreshold);
            Scribe_Values.Look(ref _safePredatorBodySizeThreshold, "SafePredatorBodySizeThreshold", ModConstants.DefaultSafePredatorBodySizeThreshold);
            Scribe_Values.Look(ref _safeNonPredatorBodySizeThreshold, "SafeNonPredatorBodySizeThreshold", ModConstants.DefaultSafeNonPredatorBodySizeThreshold);
            Scribe_Values.Look(ref EnableAgroAtSlaughter, "EnableAgroAtSlaughter", true);
            Scribe_Values.Look(ref EnableMammalLactation, "EnableMammalLactation", true);
            Scribe_Values.Look(ref EnablePredatorDefendCorpse, "EnablePredatorDefendCorpse", true);
            Scribe_Values.Look(ref PreyProtectionRange, "PreyProtectionRange", 20);
            Scribe_Values.Look(ref CorpseUnownedSizeMultiplier, "CorpseUnownedSizeMultiplier", 5);
            Scribe_Values.Look(ref EnableScavengering, "EnableScavengering", true);
            Scribe_Values.Look(ref LactationSlaughterHandling, "LactationSlaughterHandling", LactationSlaughterMode.TreatAsPregnant);

            
            if (AllowSlaughterLactatingPerAnimal == null)
                AllowSlaughterLactatingPerAnimal = new Dictionary<string, bool>();
            Scribe_Collections.Look(ref AllowSlaughterLactatingPerAnimal, "AllowSlaughterLactatingPerAnimal", LookMode.Value, LookMode.Value);

            
            if (!EnableMammalLactation)
            {
                LactationSlaughterHandling = LactationSlaughterMode.Ignore;
            }

            Scribe_Values.Look(ref EnableAnimalDamageReduction, "EnableAnimalDamageReduction", false);
            if (_cePresent)
            {
                EnableAnimalDamageReduction = false;
            }

            Scribe_Values.Look(ref EnableOverrideCEPenetration, "EnableOverrideCEPenetration", false);
            if (!_cePresent)
            {
                EnableOverrideCEPenetration = false;
            }
        }

        
        public bool GetAllowSlaughterLactatingFor(ThingDef animal)
        {
            if (animal == null) return false;
            if (AllowSlaughterLactatingPerAnimal == null)
                AllowSlaughterLactatingPerAnimal = new Dictionary<string, bool>();
            if (AllowSlaughterLactatingPerAnimal.TryGetValue(animal.defName, out bool v))
                return v;
            return false;
        }

        public void SetAllowSlaughterLactatingFor(ThingDef animal, bool value)
        {
            if (animal == null) return;
            if (AllowSlaughterLactatingPerAnimal == null)
                AllowSlaughterLactatingPerAnimal = new Dictionary<string, bool>();
            AllowSlaughterLactatingPerAnimal[animal.defName] = value;
        }
    }
}