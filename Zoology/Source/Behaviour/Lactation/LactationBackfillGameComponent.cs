using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    public class LactationBackfillGameComponent : GameComponent
    {
        private const int BackfillTickInterval = ZoologyTickLimiter.Lactation.BackfillTickInterval;
        private const int BackfillBatchSize = 8;
        private const int MotherNearBabyMaxDistance = 30;
        private const int MotherNearBabyMaxDistanceSq = MotherNearBabyMaxDistance * MotherNearBabyMaxDistance;

        private bool backfillDone;
        private List<Pawn> pendingBabies;
        private int pendingIndex;
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
            TryStartBackfill();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            TryStartBackfill();
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            TryStartBackfill();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            if (!ShouldRunBackfill()) return;
            if (Find.TickManager == null) return;
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
                        if (!AnimalChildcareUtility.IsAnimalBabyLifeStage(p.ageTracker?.CurLifeStage)) continue;
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
                if (!AnimalChildcareUtility.IsAnimalBabyLifeStage(baby.ageTracker?.CurLifeStage)) return;
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

        private static bool IsValidMotherForBackfill(Pawn mother, Pawn baby)
        {
            if (mother == null || baby == null) return false;
            if (mother.Dead || !mother.Spawned) return false;
            if (mother.gender != Gender.Female) return false;
            if (!mother.IsMammal()) return false;
            if (mother.Map != baby.Map) return false;
            if (baby.Dead || !baby.Spawned) return false;
            if (!baby.IsMammal()) return false;
            if (!AnimalChildcareUtility.IsAnimalBabyLifeStage(baby.ageTracker?.CurLifeStage)) return false;
            if (!IsFirstLifeStage(baby)) return false;
            if (!AnimalChildcareUtility.IsCrossBreedCompatible(mother, baby)) return false;

            var lactDef = AnimalChildcareUtility.LactatingHediffDef;
            if (lactDef == null) return false;
            if (mother.health?.hediffSet == null) return false;
            if (mother.health.hediffSet.HasHediff(lactDef)) return false;

            return true;
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
                var lactDef = AnimalChildcareUtility.LactatingHediffDef;
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

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref backfillDone, "Zoology_LactationBackfillDone", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pendingBabies = null;
                pendingIndex = 0;
                processedMotherIds.Clear();
            }
        }
    }
}
