using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ZoologyMod
{
    public class LactationBackfillGameComponent : GameComponent
    {
        private const int BackfillTickInterval = ZoologyTickLimiter.Lactation.BackfillTickInterval;
        private const int BackfillBatchSize = 8;
        private const int RecentBirthObserverTickInterval = ZoologyTickLimiter.Lactation.BackfillTickInterval;
        private const long RecentBirthAgeThresholdTicks = 10000L;
        private const int MotherNearBabyMaxDistance = 30;
        private const int MotherNearBabyMaxDistanceSq = MotherNearBabyMaxDistance * MotherNearBabyMaxDistance;

        private bool backfillDone;
        private List<Pawn> pendingBabies;
        private int pendingIndex;
        private int lastObservedRecentBirthTick = -1;
        private readonly HashSet<int> processedMotherIds = new HashSet<int>();

        public LactationBackfillGameComponent(Game game) : base()
        {
        }

        public LactationBackfillGameComponent()
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            ResetLactationRuntimeCaches();
            TryStartBackfill();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            ResetLactationRuntimeCaches();
            TryStartBackfill();
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            ResetLactationRuntimeCaches();
            TryStartBackfill();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            if (Find.TickManager == null)
            {
                return;
            }

            TryObserveRecentBirthsForRJW();

            if (!ShouldRunBackfill()) return;
            if (Find.TickManager.TicksGame % BackfillTickInterval != 0) return;

            if (pendingBabies == null)
            {
                BuildPendingList();
                if (pendingBabies == null || pendingBabies.Count == 0)
                {
                    MarkBackfillComplete();
                    return;
                }
            }

            int processed = 0;
            while (pendingIndex < pendingBabies.Count && processed < BackfillBatchSize)
            {
                Pawn baby = pendingBabies[pendingIndex++];
                processed++;
                TryBackfillForBaby(baby);
            }

            if (pendingIndex >= pendingBabies.Count)
            {
                MarkBackfillComplete();
            }
        }

        private void TryStartBackfill()
        {
            if (!ShouldRunBackfill()) return;
            if (pendingBabies != null) return;
            BuildPendingList();
        }

        private void TryObserveRecentBirthsForRJW()
        {
            if (!ZoologyModSettings.EnableMammalLactation || !RJWLactationCompatibility.IsRJWActive)
            {
                return;
            }

            int currentTick = Find.TickManager?.TicksGame ?? -1;
            if (currentTick < 0
                || currentTick == lastObservedRecentBirthTick
                || currentTick % RecentBirthObserverTickInterval != 0)
            {
                return;
            }

            lastObservedRecentBirthTick = currentTick;

            try
            {
                var newbornsByMother = new Dictionary<Pawn, List<Pawn>>();
                CollectRecentBirthsOnMaps(newbornsByMother);
                CollectRecentBirthsInCaravans(newbornsByMother);
                ApplyObservedBirths(newbornsByMother);
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] Lactation recent-birth observer failed: {ex}");
            }
        }

        private bool ShouldRunBackfill()
        {
            return ZoologyModSettings.EnableMammalLactation && !backfillDone;
        }

        private void BuildPendingList()
        {
            try
            {
                pendingBabies = new List<Pawn>(128);
                processedMotherIds.Clear();
                pendingIndex = 0;

                if (Find.Maps == null) return;
                for (int mi = 0; mi < Find.Maps.Count; mi++)
                {
                    var map = Find.Maps[mi];
                    var pawns = map?.mapPawns?.AllPawnsSpawned;
                    if (pawns == null) continue;

                    for (int i = 0; i < pawns.Count; i++)
                    {
                        Pawn p = pawns[i];
                        if (p == null || p.Dead) continue;
                        if (!p.IsMammal()) continue;
                        if (!AnimalLactationUtility.IsAnimalBabyLifeStage(p.ageTracker?.CurLifeStage)) continue;
                        if (!IsFirstLifeStage(p)) continue;
                        pendingBabies.Add(p);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] Lactation backfill: failed to build pending list: {ex}");
                pendingBabies = null;
                pendingIndex = 0;
            }
        }

        private void TryBackfillForBaby(Pawn baby)
        {
            try
            {
                if (baby == null || baby.Dead || !baby.Spawned) return;
                if (!baby.IsMammal()) return;
                if (!AnimalLactationUtility.IsAnimalBabyLifeStage(baby.ageTracker?.CurLifeStage)) return;
                if (!IsFirstLifeStage(baby)) return;

                Pawn mother = TryGetMotherFromRelations(baby);
                if (!IsValidMotherForBackfill(mother, baby)) return;
                if (!IsMotherNearBaby(mother, baby)) return;

                int mid = mother.thingIDNumber;
                if (processedMotherIds.Contains(mid)) return;

                if (EnsureLactatingHediff(mother))
                {
                    processedMotherIds.Add(mid);
                }
            }
            catch
            {
            }
        }

        private static void CollectRecentBirthsOnMaps(Dictionary<Pawn, List<Pawn>> newbornsByMother)
        {
            if (newbornsByMother == null || Find.Maps == null)
            {
                return;
            }

            for (int mapIndex = 0; mapIndex < Find.Maps.Count; mapIndex++)
            {
                Map map = Find.Maps[mapIndex];
                IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
                if (pawns == null)
                {
                    continue;
                }

                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn baby = pawns[i];
                    if (!IsRecentObservedBaby(baby))
                    {
                        continue;
                    }

                    Pawn mother = TryGetMotherFromRelations(baby);
                    if (!IsValidMotherForObservedMapBirth(mother, baby) || !IsMotherNearBaby(mother, baby))
                    {
                        continue;
                    }

                    AddObservedNewborn(newbornsByMother, mother, baby);
                }
            }
        }

        private static void CollectRecentBirthsInCaravans(Dictionary<Pawn, List<Pawn>> newbornsByMother)
        {
            if (newbornsByMother == null || Find.WorldObjects == null)
            {
                return;
            }

            List<Caravan> caravans = Find.WorldObjects.Caravans;
            if (caravans == null)
            {
                return;
            }

            for (int caravanIndex = 0; caravanIndex < caravans.Count; caravanIndex++)
            {
                Caravan caravan = caravans[caravanIndex];
                List<Pawn> pawns = caravan?.PawnsListForReading;
                if (pawns == null)
                {
                    continue;
                }

                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn baby = pawns[i];
                    if (!IsRecentObservedBaby(baby))
                    {
                        continue;
                    }

                    Pawn mother = TryGetMotherFromRelations(baby);
                    if (!IsValidMotherForObservedCaravanBirth(mother, baby, caravan))
                    {
                        continue;
                    }

                    AddObservedNewborn(newbornsByMother, mother, baby);
                }
            }
        }

        private static void ApplyObservedBirths(Dictionary<Pawn, List<Pawn>> newbornsByMother)
        {
            if (newbornsByMother == null || newbornsByMother.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<Pawn, List<Pawn>> entry in newbornsByMother)
            {
                Pawn mother = entry.Key;
                List<Pawn> newborns = entry.Value;
                if (mother == null || newborns == null || newborns.Count == 0)
                {
                    continue;
                }

                AnimalLactationUtility.OnAnimalGaveBirth(mother, newborns);
            }
        }

        private static void AddObservedNewborn(Dictionary<Pawn, List<Pawn>> newbornsByMother, Pawn mother, Pawn baby)
        {
            if (newbornsByMother == null || mother == null || baby == null)
            {
                return;
            }

            if (!newbornsByMother.TryGetValue(mother, out List<Pawn> newborns))
            {
                newborns = new List<Pawn>(4);
                newbornsByMother[mother] = newborns;
            }

            for (int i = 0; i < newborns.Count; i++)
            {
                if (ReferenceEquals(newborns[i], baby))
                {
                    return;
                }
            }

            newborns.Add(baby);
        }

        private static Pawn TryGetMotherFromRelations(Pawn baby)
        {
            try
            {
                var rels = baby?.relations?.DirectRelations;
                if (rels == null) return null;
                for (int i = 0; i < rels.Count; i++)
                {
                    var rel = rels[i];
                    if (rel == null) continue;
                    if (rel.def != PawnRelationDefOf.Parent) continue;
                    if (rel.otherPawn == null) continue;
                    if (rel.otherPawn.gender == Gender.Female) return rel.otherPawn;
                }
            }
            catch
            {
            }
            return null;
        }

        private static bool IsRecentObservedBaby(Pawn baby)
        {
            if (baby == null || baby.Dead || !baby.IsMammal())
            {
                return false;
            }

            if (!AnimalLactationUtility.IsAnimalBabyLifeStage(baby.ageTracker?.CurLifeStage) || !IsFirstLifeStage(baby))
            {
                return false;
            }

            return baby.ageTracker != null && baby.ageTracker.AgeBiologicalTicks <= RecentBirthAgeThresholdTicks;
        }

        private static bool IsValidMotherForBackfill(Pawn mother, Pawn baby)
        {
            if (mother == null || baby == null) return false;
            if (mother.Dead || !mother.Spawned) return false;
            if (mother.gender != Gender.Female) return false;
            if (!mother.IsMammal()) return false;
            if (mother.Map != baby.Map) return false;
            if (baby.Dead || !baby.Spawned) return false;
            if (!baby.IsMammal()) return false;
            if (!AnimalLactationUtility.IsAnimalBabyLifeStage(baby.ageTracker?.CurLifeStage)) return false;
            if (!IsFirstLifeStage(baby)) return false;
            if (!AnimalLactationUtility.IsCrossBreedCompatible(mother, baby)) return false;

            var lactDef = AnimalLactationUtility.LactatingHediffDef;
            if (lactDef == null) return false;
            if (mother.health?.hediffSet == null) return false;
            if (mother.health.hediffSet.HasHediff(lactDef)) return false;

            return true;
        }

        private static bool IsValidMotherForObservedMapBirth(Pawn mother, Pawn baby)
        {
            if (mother == null || baby == null)
            {
                return false;
            }

            if (mother.Dead || baby.Dead || !mother.Spawned || !baby.Spawned)
            {
                return false;
            }

            if (mother.gender != Gender.Female || !mother.IsMammal())
            {
                return false;
            }

            if (mother.Map != baby.Map || !AnimalLactationUtility.IsCrossBreedCompatible(mother, baby))
            {
                return false;
            }

            HediffDef lactDef = AnimalLactationUtility.LactatingHediffDef;
            return lactDef != null
                && mother.health?.hediffSet != null
                && !mother.health.hediffSet.HasHediff(lactDef);
        }

        private static bool IsValidMotherForObservedCaravanBirth(Pawn mother, Pawn baby, Caravan caravan)
        {
            if (mother == null || baby == null || caravan == null)
            {
                return false;
            }

            if (mother.Dead || baby.Dead || mother.gender != Gender.Female || !mother.IsMammal())
            {
                return false;
            }

            if (!AnimalLactationUtility.IsCrossBreedCompatible(mother, baby))
            {
                return false;
            }

            List<Pawn> pawns = caravan.PawnsListForReading;
            if (pawns == null || !pawns.Contains(mother) || !pawns.Contains(baby))
            {
                return false;
            }

            HediffDef lactDef = AnimalLactationUtility.LactatingHediffDef;
            return lactDef != null
                && mother.health?.hediffSet != null
                && !mother.health.hediffSet.HasHediff(lactDef);
        }

        private static bool IsFirstLifeStage(Pawn pawn)
        {
            try
            {
                return pawn?.ageTracker != null && pawn.ageTracker.CurLifeStageIndex == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsMotherNearBaby(Pawn mother, Pawn baby)
        {
            try
            {
                if (mother == null || baby == null) return false;
                if (!mother.Spawned || !baby.Spawned) return false;
                if (mother.Map != baby.Map) return false;
                int distSq = (mother.Position - baby.Position).LengthHorizontalSquared;
                return distSq <= MotherNearBabyMaxDistanceSq;
            }
            catch
            {
                return false;
            }
        }

        private static bool EnsureLactatingHediff(Pawn mother)
        {
            try
            {
                var lactDef = AnimalLactationUtility.LactatingHediffDef;
                if (lactDef == null) return false;
                if (mother.health?.hediffSet == null) return false;

                Hediff lact = mother.health.hediffSet.GetFirstHediffOfDef(lactDef);
                if (lact == null)
                {
                    lact = HediffMaker.MakeHediff(lactDef, mother);
                    mother.health.AddHediff(lact);
                }

                lact.Severity = Math.Max(0.25f, lact.Severity);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void MarkBackfillComplete()
        {
            backfillDone = true;
            pendingBabies = null;
            pendingIndex = 0;
            processedMotherIds.Clear();
        }

        private static void ResetLactationRuntimeCaches()
        {
            try
            {
                AnimalLactationUtility.ResetRuntimeCachesForLoad();
                LactationRequestUtility.ResetRuntimeCachesForLoad();
                JobGiver_AnimalAutoFeed.ResetRuntimeCachesForLoad();
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] Failed to reset lactation runtime caches: {ex}");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref backfillDone, "Zoology_LactationBackfillDone", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ResetLactationRuntimeCaches();
                pendingBabies = null;
                pendingIndex = 0;
                lastObservedRecentBirthTick = -1;
                processedMotherIds.Clear();
            }
        }
    }
}
