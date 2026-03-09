using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    public class ModExtension_NoPorcupineQuill : DefModExtension
    {
    }

    [StaticConstructorOnStartup]
    public static class NoPorcupineQuill_HarmonyPatches
    {
        private const string PorcupineQuillDefName = "PorcupineQuill";

        private static HediffDef porcupineQuillDef;
        private static bool porcupineQuillResolved;

        static NoPorcupineQuill_HarmonyPatches()
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && !settings.EnableNoPorcupineQuillPatch)
                {
                    return;
                }

                var harmony = new Harmony("com.abobashark.zoology.noporcupinequill");

                var addDirect = AccessTools.Method(typeof(HediffSet), "AddDirect");
                if (addDirect != null)
                {
                    var prefix = new HarmonyMethod(typeof(NoPorcupineQuill_HarmonyPatches), nameof(AddDirect_Prefix));
                    harmony.Patch(addDirect, prefix: prefix);
                }
                else
                {
                    Log.Warning("[Zoology.NoPorcupineQuill] HediffSet.AddDirect not found - can't patch add-blocking.");
                }

                var spawnSetup = AccessTools.Method(typeof(Pawn), "SpawnSetup", new Type[] { typeof(Map), typeof(bool) });
                if (spawnSetup != null)
                {
                    var spawnSetupPostfix = new HarmonyMethod(typeof(NoPorcupineQuill_HarmonyPatches), nameof(PawnSpawnSetup_Postfix));
                    harmony.Patch(spawnSetup, postfix: spawnSetupPostfix);
                }
                else
                {
                    Log.Warning("[Zoology.NoPorcupineQuill] Pawn.SpawnSetup not found - can't patch one-shot cleanup.");
                }
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology.NoPorcupineQuill] patch init error: {e}");
            }
        }

        private static bool IsNoPorcupineQuillEnabled()
        {
            var settings = ZoologyModSettings.Instance;
            return settings == null || settings.EnableNoPorcupineQuillPatch;
        }

        private static HediffDef GetPorcupineQuillDef()
        {
            if (!porcupineQuillResolved)
            {
                porcupineQuillDef = DefDatabase<HediffDef>.GetNamedSilentFail(PorcupineQuillDefName);
                porcupineQuillResolved = true;
            }

            return porcupineQuillDef;
        }

        private static bool IsTargetPorcupineQuill(Hediff hediff)
        {
            if (hediff?.def == null) return false;

            HediffDef targetDef = GetPorcupineQuillDef();
            return targetDef != null
                ? hediff.def == targetDef
                : string.Equals(hediff.def.defName, PorcupineQuillDefName, StringComparison.Ordinal);
        }

        private static bool HasNoPorcupineQuill(Pawn pawn)
        {
            return pawn?.def != null && ZoologyCacheUtility.HasNoPorcupineQuillExtension(pawn.def);
        }

        private static void TryRemoveExistingPorcupineQuill(Pawn pawn)
        {
            if (pawn == null) return;
            if (!HasNoPorcupineQuill(pawn)) return;

            var hediffSet = pawn.health?.hediffSet;
            if (hediffSet == null) return;

            var targetDef = GetPorcupineQuillDef();
            if (targetDef == null) return;

            var existing = hediffSet.GetFirstHediffOfDef(targetDef, false);
            if (existing != null)
            {
                pawn.health.RemoveHediff(existing);
                if (Prefs.DevMode)
                {
                    Log.Message($"[Zoology.NoPorcupineQuill] removed {targetDef.defName} from {pawn.LabelShort} ({pawn.ThingID}) on spawn.");
                }
            }
        }

        public static bool AddDirect_Prefix(HediffSet __instance, Hediff hediff)
        {
            try
            {
                if (!IsNoPorcupineQuillEnabled())
                {
                    return true;
                }

                if (!IsTargetPorcupineQuill(hediff)) return true;
                if (__instance?.pawn == null) return true;

                var pawn = __instance.pawn;
                if (!HasNoPorcupineQuill(pawn)) return true;

                if (Prefs.DevMode)
                {
                    Log.Message($"[Zoology.NoPorcupineQuill] prevented adding {hediff.def.defName} to pawn {pawn.LabelShort} ({pawn.ThingID})");
                }

                return false;
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology.NoPorcupineQuill] error in AddDirect_Prefix: {e}");
                return true;
            }
        }

        public static void PawnSpawnSetup_Postfix(Pawn __instance)
        {
            try
            {
                if (!IsNoPorcupineQuillEnabled())
                {
                    return;
                }

                TryRemoveExistingPorcupineQuill(__instance);
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology.NoPorcupineQuill] error in PawnSpawnSetup_Postfix cleanup: {e}");
            }
        }
    }
}
