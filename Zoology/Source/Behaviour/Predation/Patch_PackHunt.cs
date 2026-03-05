// PackPredatorHuntPatch.cs

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Verse;
using RimWorld;
using Verse.AI;

namespace ZoologyMod
{
    // Патч на JobGiver_GetFood.TryGiveJob: если pawn получает JobDefOf.PredatorHunt,
    // пытаемся рекрутировать nearby herd-товарищей той же расы / canCrossBreedWith.
    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    public static class HerdPredatorHuntPatch
    {
        // радиус поиска стаи в тайлах
        private const float HerdRadius = 35f;

        public static void Postfix(Pawn pawn, ref Job __result)
        {
            try
            {
                if (pawn == null || __result == null) return;
                if (__result.def != JobDefOf.PredatorHunt) return;

                // цель охоты (предполагаем, что TargetIndex.A - цель)
                Thing targetThing = null;
                try { targetThing = __result.targetA.Thing; } catch { targetThing = null; }
                if (targetThing == null) return;
                Pawn preyPawn = targetThing as Pawn;
                if (preyPawn == null) return;

                // только хищники-стайные инициируют массовую реакцию (чтобы не затрагивать одиночных)
                if (!pawn.RaceProps.herdAnimal) return;

                var map = pawn.Map;
                if (map == null) return;

                // Собираем кандидатов в радиусе HerdRadius
                var all = map.mapPawns.AllPawnsSpawned; // IReadOnlyList<Pawn> в современных версиях -> var удобно
                int herdRadiusSq = (int)(HerdRadius * HerdRadius);

                for (int i = 0; i < all.Count; i++)
                {
                    Pawn candidate = all[i];
                    if (candidate == null) continue;
                    if (candidate == pawn) continue; // сам инициатор
                    if (candidate.Downed) continue;
                    if (candidate.InMentalState) continue;
                    if (!candidate.RaceProps.herdAnimal) continue;
                    if (candidate.Faction != null) continue; // только 'безфракционные' (faction == null)
                    // дистанция фильтр (манхэттен/евклид - используем LengthHorizontalSquared)
                    if ((candidate.Position - pawn.Position).LengthHorizontalSquared > herdRadiusSq) continue;

                    // допускаем либо ту же def, либо cross-breed relation (как вы просили)
                    bool sameDef = candidate.def == pawn.def;
                    bool crossbreed = false;
                    try
                    {
                        if (candidate.def?.race?.canCrossBreedWith != null)
                        {
                            for (int k = 0; k < candidate.def.race.canCrossBreedWith.Count; k++)
                            {
                                ThingDef td = candidate.def.race.canCrossBreedWith[k];
                                if (td == null) continue;
                                if (td == pawn.def || string.Equals(td.defName, pawn.def?.defName, StringComparison.OrdinalIgnoreCase))
                                {
                                    crossbreed = true;
                                    break;
                                }
                            }
                        }
                    }
                    catch { crossbreed = false; }

                    if (!sameDef && !crossbreed) continue;

                    // Не трогаем, если кандидат уже атакует ту же цель / уже в PredatorHunt против этой цели
                    try
                    {
                        var curJob = candidate.CurJob;
                        if (curJob != null && curJob.def == JobDefOf.PredatorHunt)
                        {
                            Thing curTarget = null;
                            try { curTarget = curJob.targetA.Thing; } catch { curTarget = null; }
                            if (curTarget == targetThing) continue; // уже охотится на ту же цель
                        }
                    }
                    catch { /*ignore*/ }

                    // Сформировать job и попытаться отдать кандидату
                    try
                    {
                        Job recruitJob = JobMaker.MakeJob(JobDefOf.PredatorHunt, preyPawn);
                        recruitJob.killIncappedTarget = true;

                        // Некоторое окружение: если candidate уже занят - TryTakeOrderedJob попытается поставить данную задачу в приоритет
                        bool given = false;
                        try
                        {
                            // TryTakeOrderedJob обычно доступен в API
                            given = candidate.jobs.TryTakeOrderedJob(recruitJob);
                        }
                        catch
                        {
                            // Если TryTakeOrderedJob отсутствует или бросает, пробуем StartJob (в try/catch)
                            try
                            {
                                candidate.jobs.StartJob(recruitJob, JobCondition.None, null, false, true, null);
                                given = true;
                            }
                            catch
                            {
                                given = false;
                            }
                        }

                        // если дали задачу — пометим карту (обновление кэша атак)
                        if (given)
                        {
                            try { candidate.Map.attackTargetsCache.UpdateTarget(candidate); } catch { }
                        }
                    }
                    catch (Exception inner)
                    {
                        Log.Warning($"[Zoology] HerdPredatorHuntPatch: error giving job to candidate {candidate}: {inner}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] HerdPredatorHuntPatch.Postfix error: {ex}");
            }
        }
    }
}