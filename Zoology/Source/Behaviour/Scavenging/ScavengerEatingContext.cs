using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public static class ScavengerEatingContext
    {
        private static readonly Dictionary<int, int> pawnToCorpseId = new Dictionary<int, int>(128);
        private static readonly Dictionary<int, Pawn> corpseToPawn = new Dictionary<int, Pawn>(128);

        public static void SetEating(Pawn pawn, Thing target)
        {
            if (pawn == null) return;
            if (!DefModExtensionCache<ModExtension_IsScavenger>.TryGet(pawn, out _)) return;

            int pawnId = pawn.thingIDNumber;

            if (pawnToCorpseId.TryGetValue(pawnId, out int oldCorpseId))
            {
                pawnToCorpseId.Remove(pawnId);
                corpseToPawn.Remove(oldCorpseId);
            }

            var corpse = target as Corpse;
            if (corpse == null) return;

            int corpseId = corpse.thingIDNumber;
            if (corpseToPawn.TryGetValue(corpseId, out Pawn existing) && existing != pawn)
            {
                pawnToCorpseId.Remove(existing.thingIDNumber);
            }

            pawnToCorpseId[pawnId] = corpseId;
            corpseToPawn[corpseId] = pawn;
        }

        public static void Clear(Pawn pawn)
        {
            if (pawn == null) return;
            int pawnId = pawn.thingIDNumber;
            if (pawnToCorpseId.TryGetValue(pawnId, out int corpseId))
            {
                pawnToCorpseId.Remove(pawnId);
                corpseToPawn.Remove(corpseId);
            }
        }

        public static Pawn GetEatingPawnForCorpse(Corpse corpse)
        {
            if (corpse == null) return null;

            int corpseId = corpse.thingIDNumber;
            if (!corpseToPawn.TryGetValue(corpseId, out Pawn pawn) || pawn == null)
            {
                corpseToPawn.Remove(corpseId);
                return null;
            }

            if (corpse.Destroyed || (!corpse.Spawned && corpse.ParentHolder == null))
            {
                corpseToPawn.Remove(corpseId);
                pawnToCorpseId.Remove(pawn.thingIDNumber);
                return null;
            }

            if (pawn.Dead || pawn.Destroyed)
            {
                corpseToPawn.Remove(corpseId);
                pawnToCorpseId.Remove(pawn.thingIDNumber);
                return null;
            }

            Job curJob = pawn.CurJob;
            if (curJob != null && curJob.def == JobDefOf.Ingest)
            {
                bool matches = curJob.targetA.Thing == corpse
                    || curJob.targetB.Thing == corpse
                    || curJob.targetC.Thing == corpse;

                if (!matches)
                {
                    corpseToPawn.Remove(corpseId);
                    pawnToCorpseId.Remove(pawn.thingIDNumber);
                    return null;
                }
            }

            return pawn;
        }
    }
}
