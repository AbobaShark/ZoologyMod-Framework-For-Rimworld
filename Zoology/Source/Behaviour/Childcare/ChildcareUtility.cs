using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    internal static class ChildcareUtility
    {
        private const int MotherCacheDurationTicks = ZoologyTickLimiter.Childcare.MotherCacheDurationTicks;

        private readonly struct MotherCacheEntry
        {
            public MotherCacheEntry(Pawn mother, int tick, bool hasMother)
            {
                Mother = mother;
                Tick = tick;
                HasMother = hasMother;
            }

            public Pawn Mother { get; }
            public int Tick { get; }
            public bool HasMother { get; }
        }

        private static readonly Dictionary<int, MotherCacheEntry> motherCacheByChildId = new Dictionary<int, MotherCacheEntry>(128);
        private static readonly Dictionary<int, Pawn> observedMotherByChildId = new Dictionary<int, Pawn>(128);
        private static Game runtimeCacheGame;

        public static bool IsChildcareEnabled
        {
            get
            {
                var s = ZoologyModSettings.Instance;
                if (s == null) return true;
                if (s.DisableAllRuntimePatches) return false;
                return s.EnableAnimalChildcare;
            }
        }

        public static bool HasChildcareExtension(Pawn pawn)
        {
            return pawn != null && DefModExtensionCache<ModExtensiom_Chlidcare>.Has(pawn);
        }

        public static bool IsAnimalJuvenileLifeStage(LifeStageDef stage)
        {
            return AnimalLifeStageUtility.IsAnimalJuvenileLifeStage(stage);
        }

        public static bool IsAnimalChildLifeStage(LifeStageDef stage)
        {
            return AnimalLifeStageUtility.IsAnimalChildLifeStage(stage);
        }

        public static bool IsAnimalChild(Pawn pawn)
        {
            return pawn != null && IsAnimalChildLifeStage(pawn.ageTracker?.CurLifeStage);
        }

        public static bool TryGetBiologicalMother(Pawn child, out Pawn mother)
        {
            mother = null;
            if (child == null) return false;

            int now = Find.TickManager?.TicksGame ?? 0;
            EnsureRuntimeCacheState();
            int childId = child.thingIDNumber;

            if (now > 0
                && motherCacheByChildId.TryGetValue(childId, out MotherCacheEntry cached)
                && now - cached.Tick <= MotherCacheDurationTicks)
            {
                if (!cached.HasMother)
                {
                    return false;
                }

                if (IsValidMotherRef(cached.Mother))
                {
                    mother = cached.Mother;
                    return true;
                }

                motherCacheByChildId.Remove(childId);
            }

            Pawn found = TryGetMotherFromRelations(child);
            if (!IsValidMotherRef(found))
            {
                found = TryGetObservedMother(childId);
            }

            if (!IsValidMotherRef(found))
            {
                found = TryInferMotherFromNearbyAdults(child);
            }

            if (now > 0)
            {
                motherCacheByChildId[childId] = new MotherCacheEntry(found, now, found != null);
            }

            if (!IsValidMotherRef(found))
            {
                return false;
            }

            mother = found;
            return true;
        }

        public static void RegisterObservedMother(Pawn child, Pawn mother)
        {
            if (child == null || !IsValidMotherRef(mother))
            {
                return;
            }

            EnsureRuntimeCacheState();
            int childId = child.thingIDNumber;
            if (childId <= 0)
            {
                return;
            }

            observedMotherByChildId[childId] = mother;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (now > 0)
            {
                motherCacheByChildId[childId] = new MotherCacheEntry(mother, now, hasMother: true);
            }
        }

        private static bool IsValidMotherRef(Pawn mother)
        {
            return mother != null && !mother.Destroyed && !mother.Dead;
        }

        private static Pawn TryGetMotherFromRelations(Pawn child)
        {
            try
            {
                var rels = child?.relations?.DirectRelations;
                if (rels == null) return null;
                for (int i = 0; i < rels.Count; i++)
                {
                    var rel = rels[i];
                    if (rel == null) continue;
                    if (rel.def != PawnRelationDefOf.Parent) continue;
                    Pawn other = rel.otherPawn;
                    if (other == null) continue;
                    if (other.gender == Gender.Female) return other;
                }
            }
            catch
            {
            }

            return null;
        }

        private static Pawn TryGetObservedMother(int childId)
        {
            if (childId <= 0 || !observedMotherByChildId.TryGetValue(childId, out Pawn mother))
            {
                return null;
            }

            if (!IsValidMotherRef(mother))
            {
                observedMotherByChildId.Remove(childId);
                return null;
            }

            return mother;
        }

        private static Pawn TryInferMotherFromNearbyAdults(Pawn child)
        {
            if (child == null
                || child.Map?.mapPawns?.AllPawnsSpawned == null
                || !child.IsAnimal
                || !IsAnimalChild(child))
            {
                return null;
            }

            Pawn bestMother = null;
            int bestDistanceSq = int.MaxValue;
            IReadOnlyList<Pawn> pawns = child.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn candidate = pawns[i];
                if (!IsPotentialMotherCandidate(candidate, child))
                {
                    continue;
                }

                int distanceSq = (candidate.Position - child.Position).LengthHorizontalSquared;
                if (distanceSq >= bestDistanceSq)
                {
                    continue;
                }

                bestMother = candidate;
                bestDistanceSq = distanceSq;
            }

            return bestMother;
        }

        private static bool IsPotentialMotherCandidate(Pawn candidate, Pawn child)
        {
            if (!IsValidMotherRef(candidate)
                || child == null
                || ReferenceEquals(candidate, child)
                || !candidate.IsAnimal
                || candidate.gender != Gender.Female
                || !candidate.Spawned
                || candidate.Downed
                || candidate.Map != child.Map
                || !HasChildcareExtension(candidate)
                || IsAnimalChild(candidate)
                || !SharesSpeciesLineage(candidate, child)
                || !IsFactionCompatible(candidate, child))
            {
                return false;
            }

            return true;
        }

        private static bool IsFactionCompatible(Pawn potentialMother, Pawn child)
        {
            if (potentialMother == null || child == null)
            {
                return false;
            }

            Faction childFaction = child.Faction;
            Faction childHost = child.HostFaction;
            if (childFaction == null && childHost == null)
            {
                return potentialMother.Faction == null && potentialMother.HostFaction == null;
            }

            return potentialMother.Faction == childFaction
                || potentialMother.Faction == childHost
                || potentialMother.HostFaction == childFaction
                || potentialMother.HostFaction == childHost;
        }

        private static bool SharesSpeciesLineage(Pawn first, Pawn second)
        {
            if (first?.def == null || second?.def == null)
            {
                return false;
            }

            return first.def == second.def || ZoologyCacheUtility.AreCrossbreedRelated(first.def, second.def);
        }

        private static void EnsureRuntimeCacheState()
        {
            Game currentGame = Current.Game;
            if (ReferenceEquals(runtimeCacheGame, currentGame))
            {
                return;
            }

            runtimeCacheGame = currentGame;
            motherCacheByChildId.Clear();
            observedMotherByChildId.Clear();
        }
    }
}
