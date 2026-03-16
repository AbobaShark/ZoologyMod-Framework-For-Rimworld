using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;

namespace ZoologyMod
{
    public static class AnimalChildcareUtility
    {
        public const int FullFeedSessionTicks = ZoologyTickLimiter.Lactation.FullFeedSessionTicks;
        public const int FullLactatingSeverityTicks = ZoologyTickLimiter.Lactation.FullLactatingSeverityTicks;
        public const float feedingThreshold = 0.33f;
        private static Dictionary<int, int> lastFeedAttemptTick = new Dictionary<int, int>();
        private static readonly Dictionary<ThingDef, HashSet<string>> crossBreedDefNamesCache = new Dictionary<ThingDef, HashSet<string>>();
        private static HediffDef lactatingHediffDef;
        private static JobDef breastfeedJobDef;
        private static JobDef youngSuckleJobDef;
        private static LifeStageDef animalBabyLifeStageDef;
        private const int FeedAttemptCooldownTicks = ZoologyTickLimiter.Lactation.FeedAttemptCooldownTicks;
        public const float MotherMinFeedLevel = 0.15f;
        public static HediffDef LactatingHediffDef => lactatingHediffDef ?? (lactatingHediffDef = DefDatabase<HediffDef>.GetNamedSilentFail("Zoology_Lactating"));
        public static JobDef BreastfeedJobDef => breastfeedJobDef ?? (breastfeedJobDef = DefDatabase<JobDef>.GetNamedSilentFail("Zoology_Breastfeed"));
        public static JobDef YoungSuckleJobDef => youngSuckleJobDef ?? (youngSuckleJobDef = DefDatabase<JobDef>.GetNamedSilentFail("Zoology_YoungSuckle"));

        public static bool IsAnimalBabyLifeStage(LifeStageDef stage)
        {
            if (stage == null) return false;
            var cached = animalBabyLifeStageDef ?? (animalBabyLifeStageDef = DefDatabase<LifeStageDef>.GetNamedSilentFail("AnimalBaby"));
            return stage == cached || string.Equals(stage.defName, "AnimalBaby", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAnimalBaby(Pawn pawn)
        {
            return pawn != null && IsAnimalBabyLifeStage(pawn.ageTracker?.CurLifeStage);
        }

        public static bool MotherHasSufficientNutrition(Pawn mom)
        {
            if (mom == null) return false;
            if (mom.needs == null || mom.needs.food == null) return false;
            return mom.needs.food.CurLevelPercentage >= MotherMinFeedLevel;
        }

        public static void RecordFeedAttempt(Pawn mom)
        {
            if (mom == null) return;
            try
            {
                int id = mom.thingIDNumber;
                lastFeedAttemptTick[id] = Find.TickManager.TicksGame;
            }
            catch { }
        }

        public static bool CanAttemptFeedNow(Pawn mom)
        {
            if (mom == null) return false;
            try
            {
                int id = mom.thingIDNumber;
                int now = Find.TickManager.TicksGame;
                if (lastFeedAttemptTick.TryGetValue(id, out int last))
                {
                    if (now - last < FeedAttemptCooldownTicks) return false;
                }
                return true;
            }
            catch
            {
                return true;
            }
        }

        public static void OnAnimalGaveBirth(Pawn mother, List<Pawn> newborns)
        {
            try
            {
                if (!ZoologyModSettings.EnableMammalLactation)
                {
                    return;
                }
                if (mother == null || mother.Dead)
                {
                    return;
                }
                if (!mother.IsMammal())
                {
                    return;
                }
                if (mother.gender != Gender.Female)
                {
                    return;
                }

                HediffDef hd = LactatingHediffDef;
                if (hd == null)
                {
                    Log.Warning("ZoologyMod: HediffDef 'Zoology_Lactating' not found in DefDatabase when trying to start lactation.");
                    return;
                }

                Hediff lact = mother.health.hediffSet.GetFirstHediffOfDef(hd);
                if (lact == null)
                {
                    lact = HediffMaker.MakeHediff(hd, mother);
                    mother.health.AddHediff(lact);
                }

                int pups = (newborns != null) ? newborns.Count : 1;
                float severityGain = GetSeverityForLitter(pups);
                lact.Severity = Mathf.Min(1f, lact.Severity + severityGain);
            }
            catch (Exception ex)
            {
                Log.Error("ZoologyMod: Exception in OnAnimalGaveBirth: " + ex);
            }
        }

        private static float GetSeverityForLitter(int pups)
        {
            if (pups <= 1) return 0.25f;
            return Mathf.Min(1f, 0.25f + 0.12f * (pups - 1));
        }

        public static bool CanMotherFeed(Pawn mom, out string failReason)
        {
            failReason = null;
            if (mom == null) { failReason = "Null mom"; return false; }
            if (mom.Dead) { failReason = "Dead mom"; return false; }
            if (mom.Downed) { failReason = "Downed mom"; return false; }
            if (!mom.IsMammal()) { failReason = "Not a mammal"; return false; }
            if (!ZoologyModSettings.EnableMammalLactation) { failReason = "Mammal lactation disabled"; return false; }

            if (mom.InMentalState)
            {
                failReason = "Mom in mental state";
                return false;
            }

            var lactDef = LactatingHediffDef;
            if (lactDef == null || !mom.health.hediffSet.HasHediff(lactDef)) { failReason = "Mom not lactating"; return false; }

            if (!MotherHasSufficientNutrition(mom))
            {
                failReason = "Mom too hungry to feed";
                return false;
            }

            return true;
        }

        public static bool CanMotherFeed(Pawn mom)
        {
            if (mom == null || mom.Dead || mom.Downed) return false;
            if (!mom.IsMammal() || !ZoologyModSettings.EnableMammalLactation) return false;
            if (mom.InMentalState) return false;

            var lactDef = LactatingHediffDef;
            if (lactDef == null || !mom.health.hediffSet.HasHediff(lactDef)) return false;

            return MotherHasSufficientNutrition(mom);
        }

        private static HashSet<string> GetCrossBreedDefNames(ThingDef motherDef)
        {
            if (motherDef == null) return null;

            HashSet<string> allowedDefNames;
            if (crossBreedDefNamesCache.TryGetValue(motherDef, out allowedDefNames))
                return allowedDefNames;

            allowedDefNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = motherDef.race?.canCrossBreedWith;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var td = list[i];
                    if (td?.defName != null)
                    {
                        allowedDefNames.Add(td.defName);
                    }
                }
            }

            crossBreedDefNamesCache[motherDef] = allowedDefNames;
            return allowedDefNames;
        }

        public static bool IsCrossBreedCompatible(Pawn mother, Pawn pup)
        {
            if (mother == null || pup == null) return false;

            ThingDef motherDef = mother.def;
            ThingDef pupDef = pup.def;
            if (motherDef == null || pupDef == null) return false;
            if (pupDef == motherDef) return true;

            var allowedDefNames = GetCrossBreedDefNames(motherDef);
            return allowedDefNames != null && allowedDefNames.Contains(pupDef.defName);
        }

        public static bool ChildWantsSuckle(Pawn pup)
        {
            if (pup == null || pup.Dead) return false;
            if (pup.needs == null || pup.needs.food == null) return false;
            if (pup.InMentalState) return false;

            if (!pup.IsMammal()) return false;

            var curStage = pup.ageTracker?.CurLifeStage;
            if (!IsAnimalBabyLifeStage(curStage))
            {
                return false;
            }

            return pup.needs.food.CurLevelPercentage < feedingThreshold;
        }

        public static Pawn FindNearestAvailableMother(Pawn pup)
        {
            if (pup == null || pup.Map == null || pup.Dead) return null;
            Pawn nearest = null;
            float bestDistSqr = float.MaxValue;
            IntVec3 pupPosition = pup.Position;
            foreach (Pawn p in pup.Map.mapPawns.AllPawnsSpawned)
            {
                if (p == null || !p.Spawned || p.Dead) continue;
                if (!CanMotherFeed(p)) continue;

                if (!IsCrossBreedCompatible(p, pup)) continue;

                float d = (p.Position - pupPosition).LengthHorizontalSquared;
                if (d >= bestDistSqr) continue;

                if (!p.CanReserve(pup)) continue;

                if (!p.CanReach(pup, PathEndMode.Touch, Danger.Deadly, false, false, TraverseMode.ByPawn)) continue;

                if (d < bestDistSqr) { bestDistSqr = d; nearest = p; }
            }
            return nearest;
        }

        public static Job MakeAnimalBreastfeedJob(Pawn pup, Pawn mom)
        {
            if (mom == null || mom.Dead || mom.Downed || pup == null || pup.Dead) return null;
            var breastfeedDef = BreastfeedJobDef;
            if (breastfeedDef == null) return null;

            Job job = JobMaker.MakeJob(breastfeedDef);
            job.targetA = pup;
            job.targetB = mom;
            job.count = 1;
            job.checkOverrideOnExpire = false;
            job.expiryInterval = ZoologyTickLimiter.Lactation.FullFeedSessionTicks;
            return job;
        }

        public static bool SuckleFromLactatingPawn(Pawn pup, Pawn mom, int deltaTicks)
        {
            try
            {
                if (pup == null || pup.Dead || mom == null || mom.Dead || mom.Downed) return false;

                if (!IsCrossBreedCompatible(mom, pup)) return false;

                var lactDef = LactatingHediffDef;
                if (lactDef == null) return false;

                Hediff lact = mom.health?.hediffSet?.GetFirstHediffOfDef(lactDef);
                if (lact == null) return false;

                if ((pup.Position - mom.Position).LengthHorizontalSquared > 2f) return false;

                if (mom.needs?.food == null) return false;
                if (pup.needs?.food == null) return false;

                if (!MotherHasSufficientNutrition(mom)) return false;

                float nutritionWanted = pup.needs.food.NutritionWanted;
                if (nutritionWanted <= 0f) return false;

                float perTick = pup.needs.food.MaxLevel / (float)FullFeedSessionTicks;
                float providedRequested = perTick * deltaTicks;

                float provided = Math.Min(nutritionWanted, providedRequested);

                float momAvailable = mom.needs.food.CurLevel;
                if (momAvailable <= 0f) return false;

                float momDeduct = Math.Min(momAvailable, provided);

                if (momDeduct <= 0f) return false;

                float newMomLevel = mom.needs.food.CurLevel - momDeduct;
                mom.needs.food.CurLevel = Math.Max(0f, newMomLevel);

                pup.needs.food.CurLevel = Math.Min(pup.needs.food.MaxLevel, pup.needs.food.CurLevel + momDeduct);

                try
                {
                    lact.Severity = 1f;
                }
                catch { }

                if (pup.needs.food.CurLevel >= pup.needs.food.MaxLevel * 0.99f)
                    return true;

                return false;
            }
            catch (Exception ex)
            {
                Log.Error("ZoologyMod: Exception in SuckleFromLactatingPawn: " + ex);
                return false;
            }
        }
    }
}
