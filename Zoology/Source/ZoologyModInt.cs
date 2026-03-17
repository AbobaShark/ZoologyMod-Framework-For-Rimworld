using HarmonyLib;
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

        public ZoologyMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<ZoologyModSettings>();
            bool allRuntimeTogglesDisabled = Settings != null && Settings.AreAllRuntimeTogglesDisabled();

            if (!allRuntimeTogglesDisabled)
            {
                var harmony = new Harmony("com.abobashark.zoology.bionic");
                harmony.PatchAll();
            }
            else
            {
                Log.Message("[Zoology] All runtime toggles are disabled. Skipping PatchAll for performance.");
            }
            
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                bool allRuntimeTogglesDisabledNow = Settings != null && Settings.AreAllRuntimeTogglesDisabled();
                if (allRuntimeTogglesDisabledNow)
                {
                    UnpatchAllZoologyHarmonyIds();
                    return;
                }

                ApplyDisabledFeatureUnpatches(Settings);

                if (Settings.EnableHumanBionicOnAnimal)
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
                EnableAllRuntimePatches();
            }
            else
            {
                DisableAllRuntimePatches();
            }
        }

        private static void EnableAllRuntimePatches()
        {
            try
            {
                var harmony = new Harmony("com.abobashark.zoology.bionic");
                harmony.PatchAll();
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
        }

        private static void DisableAllRuntimePatches()
        {
            UnpatchAllZoologyHarmonyIds();
        }

        private static void UnpatchAllZoologyHarmonyIds()
        {
            try
            {
                var ids = new List<string>
                {
                    "com.abobashark.zoology.bionic",
                    "com.abobashark.zoology.predatorpairs",
                    "com.abobashark.zoology.lactation",
                    "com.abobashark.zoology.ectothermic",
                    "zoology.ageless",
                    "zoology.drugsimmune",
                    "com.abobashark.zoology.noporcupinequill",
                    "com.abobashark.zoology.mod.melee"
                };

                for (int i = 0; i < ids.Count; i++)
                {
                    TryUnpatchHarmonyId(ids[i]);
                }

                PredationHarmonyPatches.ResetPatchedState();
                LactationPatcher.ResetPatchedState();
                AgelessHarmonyInit.ResetPatchedState();
                DrugsImmuneHarmonyInit.ResetPatchedState();
                Ectothermic_HarmonyPatches.ResetPatchedState();
                NoPorcupineQuill_HarmonyPatches.ResetPatchedState();
                CEPatches_Melee.ResetPatchedState();

                Log.Message("[Zoology] Unpatched all known Zoology harmony ids because all runtime toggles are disabled.");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[Zoology] Failed to unpatch Zoology harmony ids: {ex}");
            }
        }

        private static void ApplyDisabledFeatureUnpatches(ZoologyModSettings settings)
        {
            if (settings == null) return;

            if (!settings.EnablePredatorDefendCorpse)
                TryUnpatchHarmonyId("com.abobashark.zoology.predatorpairs");

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

            if (!settings.EnableOverrideCEPenetration)
                TryUnpatchHarmonyId("com.abobashark.zoology.mod.melee");
        }

        private static void TryUnpatchHarmonyId(string id)
        {
            try
            {
                var unpatcher = new Harmony("com.abobashark.zoology.unpatcher");
                unpatcher.UnpatchAll(id);
            }
            catch (System.Exception exId)
            {
                Log.Warning($"[Zoology] Failed to unpatch harmony id '{id}': {exId}");
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
