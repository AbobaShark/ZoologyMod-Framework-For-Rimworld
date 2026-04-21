using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(Pawn_TrainingTracker), nameof(Pawn_TrainingTracker.TrainingTrackerTickRare))]
    public static class Patch_Pawn_TrainingTracker_TrainingTrackerTickRare
    {
        static bool Prepare() => LactationSettingsGate.Enabled();

        public static bool Prefix(Pawn_TrainingTracker __instance)
        {
            try
            {
                if (!LactationSettingsGate.Enabled()) return true;
                Pawn pawn = __instance?.pawn;
                if (pawn == null) return true;
                if (!pawn.IsMammal()) return true;
                if (!AnimalLactationUtility.IsAnimalBabyLifeStage(pawn.ageTracker?.CurLifeStage)) return true;

                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"[Zoology] Patch_Pawn_TrainingTracker_TrainingTrackerTickRare Prefix failed: {ex}");
                return true;
            }
        }
    }
}
