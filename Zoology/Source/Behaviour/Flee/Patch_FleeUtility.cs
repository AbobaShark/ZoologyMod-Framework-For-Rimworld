// Patch_FleeUtility.cs

using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(FleeUtility), nameof(FleeUtility.ShouldAnimalFleeDanger))]
    public static class Patch_ShouldAnimalFleeDanger_PrefixReplace
    {
        public static bool Prefix(Pawn pawn, ref bool __result)
        {
            // Если функция отключена, запускаем оригинал
            if (!ModConstants.Settings.EnableCustomFleeDanger)
                return true;

            // Защита
            if (pawn == null)
            {
                __result = false;
                return false; // пропускаем оригинал
            }

            // Начальные условия (как в оригинале)
            bool isAnimal = pawn.IsAnimal;
            bool notInMental = !pawn.InMentalState;
            bool notFighting = !pawn.IsFighting();
            bool notDowned = !pawn.Downed;
            bool notDead = !pawn.Dead;
            bool notFollowMaster = !ThinkNode_ConditionalShouldFollowMaster.ShouldFollowMaster(pawn);
            bool noLord = pawn.GetLord() == null;

            // --- заменяем оригинальную часть (pawn.Faction != Faction.OfPlayer || !pawn.Map.IsPlayerHome)
            bool factionClause;
            if (pawn.Faction != Faction.OfPlayer || pawn.Map == null || !pawn.Map.IsPlayerHome)
            {
                // как и раньше — не принадлежит игроку или не на домашней карте => может пугаться
                factionClause = true;
            }
            else
            {
                // pawn принадлежит игроку и на домашней карте -> разрешаем пугаться только если НЕ безопасен
                bool predator = pawn.RaceProps?.predator ?? false;
                float baseSize = pawn.RaceProps?.baseBodySize ?? pawn.BodySize;

                bool safeOnHome =
                    (predator && baseSize >= ModConstants.SafePredatorBodySizeThreshold)
                    || (!predator && baseSize > ModConstants.SafeNonPredatorBodySizeThreshold);

                // если safeOnHome == true => на домашней карте приручённый НЕ пугаться => factionClause = false
                // иначе factionClause = true (оно МОЖЕТ пугаться)
                factionClause = !safeOnHome;
            }

            // Остальные оригинальные проверки:
            bool factionAllows = (pawn.Faction == null || pawn.Faction.def.animalsFleeDanger);

            // Проверки по работе (jobs)
            bool jobAllows = true;
            if (pawn.CurJob != null && pawn.CurJobDef != null && pawn.CurJobDef.neverFleeFromEnemies)
            {
                jobAllows = false;
            }
            if (pawn.jobs != null && pawn.jobs.curJob != null && pawn.jobs.curJob.def == JobDefOf.Flee && pawn.jobs.curJob.startTick == Find.TickManager.TicksGame)
            {
                jobAllows = false;
            }

            bool final = isAnimal && notInMental && notFighting && notDowned && notDead && notFollowMaster && noLord && factionClause && factionAllows && jobAllows;

            __result = final;
            return false; // запретить выполнение оригинального метода (мы полностью заменили его)
        }
    }
}