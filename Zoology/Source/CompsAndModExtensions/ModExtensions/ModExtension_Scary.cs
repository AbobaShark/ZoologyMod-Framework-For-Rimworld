// ModExtension_Scary.cs

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // -------------------------
    // ModExtension (на уровне def)
    // -------------------------
    public class ModExtension_FleeFromCarrier : DefModExtension
    {
        // Радиус обнаружения носителя (в клетках). Подобен оригинальному MaxThreatDist.
        public float fleeRadius = 18f;

        // Лимит размера убегающего зверя: если > 0 и pawn.BodySize > this, то этот животный НЕ будет убегать.
        // 0 = нет ограничения (по умолчанию)
        public float fleeBodySizeLimit = 0f;

        // Опционально: расстояние, на которое животное убежит (параметр FleeJob). Можно оставить null для дефолтного 24.
        public int? fleeDistance = 24;
    }

    // -------------------------
    // CompProperties + Comp (per-instance override)
    // -------------------------
    public class CompProperties_FleeFromCarrier : CompProperties
    {
        public float fleeRadius = 18f;
        public float fleeBodySizeLimit = 0f;
        public int? fleeDistance = 24;

        public CompProperties_FleeFromCarrier()
        {
            this.compClass = typeof(CompFleeFromCarrier);
        }
    }

    public class CompFleeFromCarrier : ThingComp
    {
        public CompProperties_FleeFromCarrier PropsFlee => (CompProperties_FleeFromCarrier)this.props;

        // Здесь можно добавить runtime-поля, управление (вкл/выкл) и сохранение.
        public bool enabled = true;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref enabled, "enabled", true);
            // Если захотите — можно сериализовать динамические параметры тоже.
        }
    }

    // -------------------------
    // Utility: получить параметры от носителя (comp > modextension > defaults)
    // -------------------------
    public static class FleeFromCarrierUtil
    {
        // Carrier = тот, от кого будут бежать другие животные.
        public static bool IsCarrier(Pawn pawn)
        {
            if (pawn == null) return false;
            if (pawn.GetComp<CompFleeFromCarrier>() != null) return true;
            if (pawn.def?.GetModExtension<ModExtension_FleeFromCarrier>() != null) return true;
            return false;
        }

        public static float GetFleeRadius(Pawn carrier)
        {
            if (carrier == null) return 0f;
            var comp = carrier.GetComp<CompFleeFromCarrier>();
            if (comp != null && comp.enabled && comp.PropsFlee != null) return comp.PropsFlee.fleeRadius;
            var ext = carrier.def?.GetModExtension<ModExtension_FleeFromCarrier>();
            if (ext != null) return ext.fleeRadius;
            return 18f; // безопасный дефолт (совместим с вашей реализацией)
        }

        public static float GetFleeBodySizeLimit(Pawn carrier)
        {
            if (carrier == null) return 0f;
            var comp = carrier.GetComp<CompFleeFromCarrier>();
            if (comp != null && comp.enabled && comp.PropsFlee != null) return comp.PropsFlee.fleeBodySizeLimit;
            var ext = carrier.def?.GetModExtension<ModExtension_FleeFromCarrier>();
            if (ext != null) return ext.fleeBodySizeLimit;
            return 0f; // 0 = нет ограничения
        }

        public static int GetFleeDistance(Pawn carrier)
        {
            if (carrier == null) return 24;
            var comp = carrier.GetComp<CompFleeFromCarrier>();
            if (comp != null && comp.enabled && comp.PropsFlee != null && comp.PropsFlee.fleeDistance.HasValue) return comp.PropsFlee.fleeDistance.Value;
            var ext = carrier.def?.GetModExtension<ModExtension_FleeFromCarrier>();
            if (ext != null && ext.fleeBodySizeLimit >= 0 && ext.fleeRadius >= 0 && ext.fleeBodySizeLimit != float.NaN)
            {
                if (ext.fleeRadius >= 0 && ext.fleeBodySizeLimit >= 0 && ext.fleeDistance.HasValue) return ext.fleeDistance.Value;
            }
            // Лучше дефолтное значение
            return 24;
        }
    }

    // -------------------------
    // Harmony патч: расширяем JobGiver_AnimalFlee.TryGiveJob
    // -------------------------
    [HarmonyPatch(typeof(JobGiver_AnimalFlee), "TryGiveJob")]
    public static class Patch_JobGiver_AnimalFlee_TryGiveJob_FleeFromCarrier
    {
        public static void Postfix(JobGiver_AnimalFlee __instance, Pawn pawn, ref Job __result)
        {
            try
            {
                // Если pawn помечен NoFlee — полностью игнорируем эту Postfix-логику
                if (pawn != null && NoFleeUtil.IsNoFlee(pawn, out var noFleeExt))
                {
                    if (noFleeExt?.verboseLogging == true && Prefs.DevMode)
                        Log.Message($"[Zoology] Suppressed Carrier-induced flee for {pawn.LabelShort} due to ModExtension_NoFlee.");
                    return;
                }
                // Если уже найдено другое подходящее задание для убегающего - не вмешиваемся
                if (__result != null) return;

                if (pawn == null) return;
                if (!pawn.RaceProps.Animal) return;

                // Не даём самим носителям триггерить бег от себя
                // (если кто-то пометил своего рода carrier, он не будет убегать сам от себя)
                // (а также исключаем помеченные животные-носители)
                if (FleeFromCarrierUtil.IsCarrier(pawn)) return;

                // Поиск ближайшего носителя в радиусах, которые могут различаться у разных носителей.
                // Для эффективности: ищем всех pawns в простом радиусе MaxPossibleRadius (например 50),
                // а затем фильтруем по реальному radius каждого носителя.
                const int MaxPossibleSearchRadius = 50; // safety cap

                Pawn threat = GenClosest.ClosestThingReachable(
                    pawn.Position,
                    pawn.Map,
                    ThingRequest.ForGroup(ThingRequestGroup.Pawn),
                    PathEndMode.OnCell,
                    TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn),
                    MaxPossibleSearchRadius,
                    t =>
                    {
                        var p = t as Pawn;
                        if (p == null) return false;
                        if (p == pawn) return false;
                        if (!FleeFromCarrierUtil.IsCarrier(p)) return false;
                        if (p.Downed) return false;

                        // Затем уточняем реальный fleeRadius, т.к. у каждого carrier он может быть разным
                        float realRadius = FleeFromCarrierUtil.GetFleeRadius(p);
                        if (realRadius <= 0f) return false; // выключено на этом носителе

                        // проверка дистанции в клетках (быстрая ранняя оценка)
                        if (!pawn.Position.InHorDistOf(p.Position, realRadius)) return false;

                        // Дополнительно критерии: можно исключать следование за родителем/хозяином
                        // Используем стандартный ShouldAnimalFleeDanger позже перед созданием задания

                        return true;
                    }
                ) as Pawn;

                if (threat == null) return;

                // Теперь проверяем лимит bodySize для убегающего животного (установлен на носителе)
                float bodySizeLimit = FleeFromCarrierUtil.GetFleeBodySizeLimit(threat);
                if (bodySizeLimit > 0f && pawn.BodySize > bodySizeLimit)
                {
                    // Животное слишком большое — не убегает от этого носителя
                    return;
                }

                // Уточняем ShouldAnimalFleeDanger (как в оригинальной реализации)
                if (!FleeUtility.ShouldAnimalFleeDanger(pawn)) return;

                // Наконец — создаём job для убегания, используем fleeDistance из носителя (comp/extension)
                int fleeDistance = FleeFromCarrierUtil.GetFleeDistance(threat);
                __result = FleeUtility.FleeJob(pawn, threat, fleeDistance);
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] Patch_JobGiver_AnimalFlee_TryGiveJob_FleeFromCarrier error: {e}");
            }
        }
    }
}
