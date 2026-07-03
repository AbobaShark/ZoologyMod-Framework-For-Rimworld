using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    internal static class AnimalWoundLickingUtility
    {
        public const string AnimalLickWoundsJobDefName = "Zoology_AnimalLickWounds";

        private const float NoSkillHumanMedicalTendSpeedFactor = 0.4f;
        private const float NoSkillHumanMedicalTendQualityFactor = 0.2f;
        private const int BaseTendDurationTicks = 600;

        private static readonly List<Hediff> tmpCandidateHediffs = new List<Hediff>(8);
        private static readonly List<Hediff> tmpHediffsToTend = new List<Hediff>(4);

        public static bool IsEnabled
        {
            get
            {
                ZoologyModSettings settings = ZoologyModSettings.Instance;
                if (settings == null)
                {
                    return true;
                }

                return !settings.DisableAllRuntimePatches && settings.EnableAnimalWoundLicking;
            }
        }

        public static bool CanUseWoundLicking(Pawn pawn)
        {
            if (!IsEnabled || pawn == null)
            {
                return false;
            }

            if (!pawn.IsAnimal || pawn.Dead || pawn.Destroyed || !pawn.Spawned || pawn.Downed || pawn.InAggroMentalState || pawn.InMentalState)
            {
                return false;
            }

            return true;
        }

        public static bool HasLickableWounds(Pawn pawn)
        {
            return TryCollectLickableHediffs(pawn, tmpCandidateHediffs) > 0;
        }

        public static int GetLickDurationTicks()
        {
            float tendSpeed = Mathf.Max(0.1f, NoSkillHumanMedicalTendSpeedFactor);
            return Mathf.Max(1, Mathf.RoundToInt(BaseTendDurationTicks / tendSpeed));
        }

        public static bool TryApplyWoundLicking(Pawn pawn)
        {
            if (TryCollectLickableHediffs(pawn, tmpCandidateHediffs) <= 0)
            {
                return false;
            }

            tmpHediffsToTend.Clear();
            try
            {
                TendUtility.SortByTendPriority(tmpCandidateHediffs);
                TendUtility.GetOptimalHediffsToTendWithSingleTreatment(
                    pawn,
                    usingMedicine: false,
                    tmpHediffsToTend,
                    tmpCandidateHediffs);
            }
            catch (Exception ex)
            {
                Log.WarningOnce($"[Zoology] Failed to select wound-licking hediffs for {pawn?.LabelShort ?? "unknown pawn"}; falling back to highest-priority wound. Exception: {ex}", 92481731);
                AddFallbackHediffToTend(tmpCandidateHediffs, tmpHediffsToTend);
            }

            if (tmpHediffsToTend.Count == 0)
            {
                return false;
            }

            float quality = CalculateNoSkillHumanSelfTendQuality();
            bool tendedAny = false;
            for (int i = 0; i < tmpHediffsToTend.Count; i++)
            {
                Hediff hediff = tmpHediffsToTend[i];
                if (!CanLickTendHediff(hediff))
                {
                    continue;
                }

                try
                {
                    hediff.Tended(quality, TendUtility.NoMedicineQualityMax, i);
                    tendedAny = true;
                }
                catch (Exception ex)
                {
                    Log.WarningOnce($"[Zoology] Failed to apply wound licking to hediff '{hediff?.def?.defName ?? "unknown"}' on {pawn?.LabelShort ?? "unknown pawn"}. Exception: {ex}", Gen.HashCombine(92481732, hediff?.def?.shortHash ?? 0));
                }
            }

            if (tendedAny)
            {
                pawn.records?.Increment(RecordDefOf.TimesTendedTo);
                pawn.mindState?.Notify_SelfTended();
            }

            return tendedAny;
        }

        private static int TryCollectLickableHediffs(Pawn pawn, List<Hediff> output)
        {
            output.Clear();
            if (!CanUseWoundLicking(pawn) || pawn.health?.hediffSet == null)
            {
                return 0;
            }

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            if (hediffs == null || hediffs.Count == 0)
            {
                return 0;
            }

            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (CanLickTendHediff(hediff))
                {
                    output.Add(hediff);
                }
            }

            return output.Count;
        }

        private static bool CanLickTendHediff(Hediff hediff)
        {
            if (hediff == null)
            {
                return false;
            }

            try
            {
                if (!hediff.Bleeding || !hediff.TendableNow())
                {
                    return false;
                }

                return IsSurfaceBodyPart(hediff.Part);
            }
            catch (Exception ex)
            {
                Log.WarningOnce($"[Zoology] Failed to inspect lickable hediff '{hediff.def?.defName ?? "unknown"}'. Exception: {ex}", Gen.HashCombine(92481733, hediff.def?.shortHash ?? 0));
                return false;
            }
        }

        private static void AddFallbackHediffToTend(List<Hediff> candidates, List<Hediff> output)
        {
            Hediff best = null;
            float bestPriority = float.MinValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                Hediff candidate = candidates[i];
                if (!CanLickTendHediff(candidate))
                {
                    continue;
                }

                float priority;
                try
                {
                    priority = candidate.TendPriority;
                }
                catch
                {
                    priority = 0f;
                }

                if (best == null || priority > bestPriority)
                {
                    best = candidate;
                    bestPriority = priority;
                }
            }

            if (best != null)
            {
                output.Add(best);
            }
        }

        private static bool IsSurfaceBodyPart(BodyPartRecord part)
        {
            if (part == null)
            {
                return false;
            }

            for (BodyPartRecord current = part; current != null; current = current.parent)
            {
                if (current.depth == BodyPartDepth.Inside)
                {
                    return false;
                }
            }

            return true;
        }

        private static float CalculateNoSkillHumanSelfTendQuality()
        {
            float baseQuality = NoSkillHumanMedicalTendQualityFactor;
            SimpleCurve postProcessCurve = StatDefOf.MedicalTendQuality?.postProcessCurve;
            if (postProcessCurve != null)
            {
                baseQuality = postProcessCurve.Evaluate(baseQuality);
            }

            baseQuality *= TendUtility.NoMedicinePotency;
            baseQuality *= TendUtility.SelfTendQualityFactor;
            return Mathf.Clamp(baseQuality, 0f, TendUtility.NoMedicineQualityMax);
        }
    }
}
