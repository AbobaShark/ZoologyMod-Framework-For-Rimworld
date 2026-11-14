using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using System.Linq; 
using System.Collections.Generic;

namespace ZoologyMod
{
    public class ZoologyMod : Mod
    {
        public static ZoologyMod Instance { get; private set; }
        public static ZoologyModSettings Settings { get; private set; }
        private const string RuntimeHarmonyId = "com.abobashark.zoology.bionic";
        private static bool runtimeAttributePatchesApplied;
        private static bool runtimeWasDisabled;

        public ZoologyMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<ZoologyModSettings>();
            bool allRuntimeTogglesDisabled = Settings != null && Settings.AreAllRuntimeTogglesDisabled();

            if (!allRuntimeTogglesDisabled)
            {
                EnsureRuntimeAttributePatches();
            }
            else
            {
                Log.Message("[Zoology] All runtime toggles are disabled. Skipping PatchAll for performance.");
            }
            
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                Settings?.CleanupInvalidAnimalOverrides();
                Settings?.ApplyRuntimeDefOverrides();
                ZoologyLifeStageProfileInstaller.ApplyOnce();
                ZoologySexAdultProfiles.Install();
#if RIMWORLD17
                AnimalBarnWorkPriority.MatchVanillaPens();
#endif
                AnimalDraftControlDefNormalizer.NormalizeDefs();
                SyncRuntimePatchesWithSettings(forceRebuild: true);

                if (Settings != null
                    && !Settings.DisableAllRuntimePatches
                    && Settings.EnableHumanBionicOnAnimal)
                {
                    CombatBionicPatcher.Patch();
                    SpecialBionicPatcher.Patch();
                    SimpleBionicPatcher.Patch();
                }
            });
        }

        public static void SetRuntimePatchesEnabled(bool enabled)
        {
            if (enabled)
            {
                SyncRuntimePatchesWithSettings(forceRebuild: true);
            }
            else
            {
                DisableAllRuntimePatches();
            }
        }

        public static void SyncRuntimePatchesWithSettings(bool forceRebuild = false)
        {
            ZoologyModSettings settings = Settings ?? ZoologyModSettings.Instance;
            if (settings == null)
            {
                return;
            }

            // Switch the actual LifeStageDefs as well as their Harmony hooks.
            // When disabled, vanilla and modded stage modifiers are restored;
            // enabling reuses the previously generated species profiles.
            ZoologyLifeStageProfileInstaller.SyncProfileStages(
                ZoologyAnimalGrowth.Enabled);
            ZoologySexAdultProfiles.SyncGraphics(ZoologySexAdultProfiles.Enabled);

            PetPlayUtility.SyncJoyGiverDefAvailability(settings);
            CompAnimalPouch.ReleaseDisabledCaravanPouches();

            if (forceRebuild)
            {
                Patch_SmallPetThreatDisabled.NotifySettingsChanged();
            }

            if (settings.AreAllRuntimeTogglesDisabled())
            {
                DisableAllRuntimePatches();
                return;
            }

            if (forceRebuild)
            {
                UnpatchAllZoologyHarmonyIds(logSummary: false);
            }

            bool restoreRefugeIndexes = runtimeWasDisabled;
            EnableAllRuntimePatches();
            runtimeWasDisabled = false;
            if (restoreRefugeIndexes && Find.Maps != null)
            {
                // Rebuild only after a real master-disable transition. This
                // never adds periodic scans to the simulation.
                for (int i = 0; i < Find.Maps.Count; i++)
                    Find.Maps[i].GetComponent<AnimalRefugeMapComponent>()?.RebuildIndexes();
            }
        }

        private static void EnsureRuntimeAttributePatches()
        {
            if (runtimeAttributePatchesApplied)
            {
                return;
            }

            var harmony = new Harmony(RuntimeHarmonyId);
            harmony.PatchAll(typeof(ZoologyMod).Assembly);
            runtimeAttributePatchesApplied = true;
        }

        private static void EnableAllRuntimePatches()
        {
            try
            {
                EnsureRuntimeAttributePatches();
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[Zoology] Failed to PatchAll while enabling runtime patches: {ex}");
            }

            PredationHarmonyPatches.EnsurePatched();
            LactationPatcher.EnsurePatched();
            AgelessHarmonyInit.EnsurePatched();
            DrugsImmuneHarmonyInit.EnsurePatched();
            Ectothermic_HarmonyPatches.EnsurePatched();
            NoPorcupineQuill_HarmonyPatches.EnsurePatched();
            CEPatches_Melee.EnsurePatched();

            ApplyDisabledFeatureUnpatches(Settings);
            EnsureEggFoodHooksMatchSettings(Settings);
            global::ZoologyMod.Patches.DamageReduction_AnimalTypes_PawnTakeDamage.SyncPatchState();
        }

        private static void DisableAllRuntimePatches()
        {
            runtimeWasDisabled = true;
            ZoologyLifeStageProfileInstaller.SyncProfileStages(false);
            ZoologyAnimalGraphics.Sync(false, false);
            UnpatchAllZoologyHarmonyIds();
        }

        private static void UnpatchAllZoologyHarmonyIds(bool logSummary = true)
        {
            try
            {
                var ids = new List<string>
                {
                    RuntimeHarmonyId,
                    "com.abobashark.zoology.predatorpairs",
                    "com.abobashark.zoology.naturalfood",
                    "com.abobashark.zoology.lactation",
                    "com.abobashark.zoology.ectothermic",
                    "zoology.ageless",
                    "zoology.drugsimmune",
                    "com.abobashark.zoology.noporcupinequill",
                    "com.abobashark.zoology.mod.melee",
                    "com.abobashark.zoology.damage_reduction"
                };

                for (int i = 0; i < ids.Count; i++)
                {
                    TryUnpatchHarmonyId(ids[i]);
                }

                // Verify the assembly itself, not only a maintained list of
                // Harmony IDs. This runs on master disable, never per tick.
                if (logSummary) UnpatchUnexpectedZoologyPatches();

                PredationHarmonyPatches.ResetPatchedState();
                LactationPatcher.ResetPatchedState();
                AgelessHarmonyInit.ResetPatchedState();
                DrugsImmuneHarmonyInit.ResetPatchedState();
                Ectothermic_HarmonyPatches.ResetPatchedState();
                NoPorcupineQuill_HarmonyPatches.ResetPatchedState();
                CEPatches_Melee.ResetPatchedState();
                global::ZoologyMod.Patches.DamageReduction_AnimalTypes_PawnTakeDamage.ResetPatchedState();

                if (logSummary)
                {
                    Log.Message("[Zoology] Unpatched all known Zoology harmony ids because all runtime toggles are disabled.");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[Zoology] Failed to unpatch Zoology harmony ids: {ex}");
            }
        }

        private static void UnpatchUnexpectedZoologyPatches()
        {
            try
            {
                var assembly = typeof(ZoologyMod).Assembly;
                var unpatcher = new Harmony("com.abobashark.zoology.unpatcher");
                int unexpected = 0;
                // Snapshot: unpatching mutates Harmony's global registry.
                foreach (var target in Harmony.GetAllPatchedMethods().ToList())
                {
                    var info = Harmony.GetPatchInfo(target);
                    if (info == null) continue;
                    var patches = info.Prefixes.Concat(info.Postfixes)
                        .Concat(info.Transpilers).Concat(info.Finalizers).ToList();
                    for (int i = 0; i < patches.Count; i++)
                    {
                        var method = patches[i].PatchMethod;
                        if (method?.DeclaringType?.Assembly != assembly) continue;
                        unpatcher.Unpatch(target, method);
                        unexpected++;
                    }
                }
                if (unexpected > 0)
                    Log.Warning($"[Zoology] Removed {unexpected} additional Zoology Harmony patches not covered by the known IDs.");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[Zoology] Could not verify all installed Harmony methods: {ex}");
            }
        }

        private static void ApplyDisabledFeatureUnpatches(ZoologyModSettings settings)
        {
            if (settings == null) return;

            if (!settings.EnablePredatorDefendCorpse)
                TryUnpatchHarmonyId("com.abobashark.zoology.predatorpairs");

            // The FoodOptimality postfix also implements guarded-corpse
            // preferences; retain it if either feature still requires it.
            if (!settings.EnablePredatorDefendCorpse && !settings.EnableAnimalFoodChoice)
                TryUnpatchHarmonyId("com.abobashark.zoology.naturalfood");

            // The new food-choice postfixes share the general runtime Harmony
            // owner. Unpatch only these two methods, preserving scavenging,
            // childcare and all unrelated food hooks.
            if (!settings.EnableAnimalFoodChoice)
            {
                TryUnpatchSpecificMethod(
                    AccessTools.Method(typeof(FoodUtility), nameof(FoodUtility.TryFindBestFoodSourceFor)),
                    AccessTools.Method(typeof(Patch_UnifiedPredatorFoodSource), "Postfix"));
                TryUnpatchSpecificMethod(
                    AccessTools.Method(typeof(JobGiver_GetFood), "TryGiveJob"),
                    AccessTools.Method(typeof(Patch_UnifiedPredatorFoodJobs), "Postfix"));
            }

            if (!ZoologyModSettings.EnableMammalLactation)
                TryUnpatchHarmonyId("com.abobashark.zoology.lactation");

            if (!settings.EnableEctothermicPatch)
                TryUnpatchHarmonyId("com.abobashark.zoology.ectothermic");

            if (!settings.EnableAgelessPatch)
                TryUnpatchHarmonyId("zoology.ageless");

            if (!settings.EnableDrugsImmunePatch)
                TryUnpatchHarmonyId("zoology.drugsimmune");

            if (!settings.EnableNoPorcupineQuillPatch)
                TryUnpatchHarmonyId("com.abobashark.zoology.noporcupinequill");

            if (!settings.EnableInsectCocoonSpawnFix)
            {
                TryUnpatchSpecificMethod(
                    AccessTools.Method(typeof(CompPawnSpawnOnWakeup), "GeneratePawns"),
                    AccessTools.Method(
                        typeof(Patch_CompPawnSpawnOnWakeup_InsectCocoonBudget),
                        nameof(Patch_CompPawnSpawnOnWakeup_InsectCocoonBudget.Prefix)));
            }

            if (!settings.EnableAnimalDamageReduction || settings.DisableAllRuntimePatches)
                global::ZoologyMod.Patches.DamageReduction_AnimalTypes_PawnTakeDamage.EnsureUnpatched();
        }

        private static void EnsureEggFoodHooksMatchSettings(ZoologyModSettings settings)
        {
            if (settings != null
                && !settings.DisableAllRuntimePatches
                && settings.EnableAnimalChildcare
                && settings.EnableAnimalEggProtection)
            {
                return;
            }

            TryUnpatchSpecificMethod(
                AccessTools.Method(typeof(FoodUtility), nameof(FoodUtility.FoodOptimality)),
                AccessTools.Method(typeof(Patch_Childcare_GuardedEggFoodOptimality), nameof(Patch_Childcare_GuardedEggFoodOptimality.Postfix)));

            TryUnpatchSpecificMethod(
                AccessTools.Method(typeof(FoodUtility), nameof(FoodUtility.WillEat), new[] { typeof(Pawn), typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool) }),
                AccessTools.Method(typeof(Patch_Childcare_BlockOwnSpeciesEggEating), nameof(Patch_Childcare_BlockOwnSpeciesEggEating.Postfix)));
        }

        private static void TryUnpatchHarmonyId(string id)
        {
            try
            {
                var unpatcher = new Harmony("com.abobashark.zoology.unpatcher");
                unpatcher.UnpatchAll(id);
                if (id == RuntimeHarmonyId)
                {
                    runtimeAttributePatchesApplied = false;
                }
            }
            catch (System.Exception exId)
            {
                Log.Warning($"[Zoology] Failed to unpatch harmony id '{id}': {exId}");
            }
        }

        private static void TryUnpatchSpecificMethod(System.Reflection.MethodBase targetMethod, System.Reflection.MethodInfo patchMethod)
        {
            if (targetMethod == null || patchMethod == null)
            {
                return;
            }

            try
            {
                var unpatcher = new Harmony("com.abobashark.zoology.unpatcher");
                unpatcher.Unpatch(targetMethod, patchMethod);
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[Zoology] Failed to unpatch specific method '{patchMethod.Name}' from '{targetMethod.Name}': {ex}");
            }
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var settings = GetSettings<ZoologyModSettings>();
            settings.DoWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "Zoology Mod";
        }
    }
}
