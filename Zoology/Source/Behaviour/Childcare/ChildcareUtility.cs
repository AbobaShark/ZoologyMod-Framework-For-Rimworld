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
    }
}
