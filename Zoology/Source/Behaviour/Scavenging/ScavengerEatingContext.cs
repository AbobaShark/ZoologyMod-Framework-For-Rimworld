// ScavengerEatingContext.cs

using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace ZoologyMod
{
    /// <summary>
    /// Простая карта контекста «какой pawn ест какую вещь (труп)».
    /// ThreadStatic чтобы данные не пересекались между потоками Unity (по возможности).
    /// Храним Pawn -> Thing (target), очищаем stale записи автоматом.
    /// </summary>
    public static class ScavengerEatingContext
    {
        [ThreadStatic] private static Dictionary<Pawn, Thing> pawnToTarget;

        private static Dictionary<Pawn, Thing> Map => pawnToTarget ?? (pawnToTarget = new Dictionary<Pawn, Thing>());

        /// <summary>
        /// Установить, что pawn начал есть конкретный target (обычно Corpse).
        /// Если pawn не является scavenger (mod extension), не делаем ничего.
        /// </summary>
        public static void SetEating(Pawn pawn, Thing target)
        {
            try
            {
                if (pawn == null) return;
                var scav = pawn.def?.GetModExtension<ModExtension_IsScavenger>();
                if (scav == null) return;

                var dict = Map;
                dict[pawn] = target;
            }
            catch (Exception e)
            {
                Debug.LogError("[Zoology] Error in ScavengerEatingContext.SetEating: " + e);
            }
        }

        /// <summary>
        /// Очистить состояние для pawn (удалить все записи для этого pawn).
        /// </summary>
        public static void Clear(Pawn pawn)
        {
            try
            {
                if (pawn == null) return;
                var dict = Map;
                if (dict.Remove(pawn))
                {
                    // intentionally left blank — verbose diagnostic logging removed
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Zoology] Error in ScavengerEatingContext.Clear: " + e);
            }
        }

        /// <summary>
        /// Возвращает pawn, который (по нашей карте) ест этот труп *и* по факту имеет CurJob Ingest и target == corpse.
        /// Если ничего не найдено — возвращает null.
        /// Удаляет устаревшие записи.
        /// </summary>
        public static Pawn GetEatingPawnForCorpse(Corpse corpse)
        {
            try
            {
                if (corpse == null) return null;
                var dict = Map;
                var toRemove = new List<Pawn>();

                foreach (var kv in dict)
                {
                    var p = kv.Key;
                    var t = kv.Value;

                    // очистка устаревших
                    if (p == null || p.Dead)
                    {
                        toRemove.Add(p);
                        continue;
                    }

                    if (t == null)
                    {
                        toRemove.Add(p);
                        continue;
                    }

                    try
                    {
                        var cj = p.CurJob;
                        if (cj == null || cj.def != JobDefOf.Ingest)
                        {
                            toRemove.Add(p);
                            continue;
                        }
                        var curTarget = cj.targetA.Thing;
                        if (curTarget == null || curTarget != t)
                        {
                            toRemove.Add(p);
                            continue;
                        }

                        if (t == corpse) return p;
                    }
                    catch
                    {
                        toRemove.Add(p);
                        continue;
                    }
                }

                // удаляем stale записи
                foreach (var rp in toRemove)
                {
                    try { dict.Remove(rp); } catch { }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Zoology] Error in ScavengerEatingContext.GetEatingPawnForCorpse: " + e);
            }
            return null;
        }

        private static string ShortPawn(Pawn p)
        {
            try { return $"{(p.LabelShort ?? p.ToString())}_id{p.thingIDNumber}"; } catch { return "pawn?"; }
        }

        private static string ShortThing(Thing t)
        {
            try { return $"{t.def?.defName ?? "thing"}_id{t.thingIDNumber}"; } catch { return "thing?"; }
        }
    }
}