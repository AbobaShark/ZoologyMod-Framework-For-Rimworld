// Comp_Ageless.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Свойства для компа, чтобы можно было их прописать в XML (CompProperties_Ageless)
    public class CompProperties_Ageless : CompProperties
    {
        public int cleanupIntervalTicks = 6000;

        public CompProperties_Ageless()
        {
            this.compClass = typeof(CompAgeless);
        }
    }

    // Комп, который добавляется к Pawn (ThingComp на Pawn)
    public class CompAgeless : ThingComp
    {
        private int tickCounter = 0;
        private int cleanupInterval = 6000;

        private CompProperties_Ageless PropsAgeless => (CompProperties_Ageless)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (PropsAgeless != null)
                cleanupInterval = Math.Max(60, PropsAgeless.cleanupIntervalTicks);
        }

        public override void CompTick()
        {
            base.CompTick();

            // работает только если parent — Pawn
            Pawn pawn = parent as Pawn;
            if (pawn == null) return;

            tickCounter++;
            if (tickCounter >= cleanupInterval)
            {
                tickCounter = 0;
                try
                {
                    RemoveForbiddenAgeHediffs(pawn);
                }
                catch (Exception e)
                {
                    Log.ErrorOnce($"[Zoology] CompAgeless.RemoveForbiddenAgeHediffs exception: {e}", 21847231);
                }
            }
        }

        private void RemoveForbiddenAgeHediffs(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return;

            // получаем набор HediffDef'ов, которые нужно запретить/удалять для этого pawn
            HashSet<HediffDef> forb = AgelessUtils.GetAgeRelatedHediffDefsForPawn(pawn);
            if (forb == null || forb.Count == 0) return;

            // Собираем список текущих hediff'ов (копия), чтобы можно было безопасно удалять
            List<Hediff> hs = pawn.health.hediffSet.hediffs.ToList();
            foreach (var h in hs)
            {
                if (h == null || h.def == null) continue;
                if (forb.Contains(h.def))
                {
                    // Удаляем — безопасный метод API
                    try
                    {
                        pawn.health.RemoveHediff(h);
                        Log.Message($"[Zoology] CompAgeless removed age hediff {h.def.defName} from {pawn.LabelShortCap}");
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[Zoology] CompAgeless failed to remove hediff {h.def.defName} from {pawn}: {e}");
                    }
                }
            }
        }
    }

    // Вспомогательные утилиты: сбор hediff'ов из HediffGiverSetDef
    public static class AgelessUtils
    {
        // Кэш для ускорения: HediffGiverSetDef.defName -> HashSet<HediffDef>
        private static Dictionary<string, HashSet<HediffDef>> cache = new Dictionary<string, HashSet<HediffDef>>();

        // Имена классов givers, которые считаем "возрастными"
        private static readonly HashSet<string> ageGiverTypeNames = new HashSet<string>
        {
            "HediffGiver_Birthday",
            "HediffGiver_BrainInjury",
            "HediffGiver_RandomAgeCurved"
        };

        // Возвращает набор HediffDef, которые назначаются возрастными givers у заданного HediffGiverSetDef
        public static HashSet<HediffDef> GetAgeRelatedHediffsFromSet(HediffGiverSetDef set)
        {
            if (set == null) return new HashSet<HediffDef>();
            if (cache.TryGetValue(set.defName, out var cached)) return new HashSet<HediffDef>(cached);

            var result = new HashSet<HediffDef>();
            if (set.hediffGivers == null)
            {
                cache[set.defName] = result;
                return result;
            }

            foreach (var giver in set.hediffGivers)
            {
                if (giver == null) continue;
                var gType = giver.GetType();
                if (!ageGiverTypeNames.Contains(gType.Name)) continue;

                // Попытка получить hediff через публичное поле/свойство
                HediffDef hd = null;
                // часто поле называется "hediff"
                var f = gType.GetField("hediff", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    hd = f.GetValue(giver) as HediffDef;
                }
                else
                {
                    // или свойство
                    var p = gType.GetProperty("hediff", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null)
                        hd = p.GetValue(giver) as HediffDef;
                }

                if (hd != null)
                {
                    result.Add(hd);
                }
                else
                {
                    // В некоторых случаях giver не хранит hediff в поле, тогда пробуем вызвать HediffGiverUtility (редко).
                    // Чтобы не ломать, просто пропускаем такие случаи.
                    // Логим для отладки, но делаем это редким логом.
                    Log.Message($"[Zoology] AgelessUtils: couldn't get HediffDef from giver {gType.Name} in set {set.defName}");
                }
            }

            cache[set.defName] = new HashSet<HediffDef>(result);
            return result;
        }

        // Для данного pawn собираем запрещённые hediff'ы:
        //  - смотрим его hediffGiverSet(ы), вытаскиваем оттуда age-related hediff'ы;
        //  - если ничего не найдено, используем fallback 'OrganicStandard' (если есть).
        public static HashSet<HediffDef> GetAgeRelatedHediffDefsForPawn(Pawn pawn)
        {
            var result = new HashSet<HediffDef>();
            if (pawn == null) return result;

            // Попытка достать PawnKindDef.hediffGiverSets (рефлексивно, т.к. поле/свойство может меняться)
            try
            {
                var pk = pawn.kindDef;
                if (pk != null)
                {
                    // чаще всего имя поля - "hediffGiverSets" и тип - List<HediffGiverSetDef>
                    var t = pk.GetType();
                    var f = t.GetField("hediffGiverSets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null)
                    {
                        var list = f.GetValue(pk) as IEnumerable<HediffGiverSetDef>;
                        if (list != null)
                        {
                            foreach (var set in list)
                            {
                                var setDefs = GetAgeRelatedHediffsFromSet(set);
                                foreach (var s in setDefs) result.Add(s);
                            }
                        }
                    }
                    else
                    {
                        // пробуем свойство
                        var p = t.GetProperty("hediffGiverSets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (p != null)
                        {
                            var list = p.GetValue(pk) as IEnumerable<HediffGiverSetDef>;
                            if (list != null)
                            {
                                foreach (var set in list)
                                {
                                    var setDefs = GetAgeRelatedHediffsFromSet(set);
                                    foreach (var s in setDefs) result.Add(s);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Zoology] AgelessUtils.GetAgeRelatedHediffDefsForPawn reflection error: {ex}");
            }

            // Если ничего не нашли — fallback на OrganicStandard (как вы просили)
            if (result.Count == 0)
            {
                var fallback = DefDatabase<HediffGiverSetDef>.GetNamedSilentFail("OrganicStandard");
                if (fallback != null)
                {
                    var setDefs = GetAgeRelatedHediffsFromSet(fallback);
                    foreach (var s in setDefs) result.Add(s);
                }
            }

            return result;
        }

        // Вспомогательный тестовый метод для внешнего использования (например, в консоли)
        public static bool IsHediffForbiddenForPawn(Pawn pawn, HediffDef hediff)
        {
            if (pawn == null || hediff == null) return false;
            var forb = GetAgeRelatedHediffDefsForPawn(pawn);
            return forb.Contains(hediff);
        }
    }

    // Статический класс, который устанавливает Harmony-патч при загрузке
    [StaticConstructorOnStartup]
    public static class AgelessHarmonyInit
    {
        static AgelessHarmonyInit()
        {
            try
            {
                var harmony = new Harmony("zoology.ageless");
                // Патчим метод TryApply у HediffGiver — префикс, который может отменять применение
                var hediffGiverType = typeof(HediffGiver);
                var method = AccessTools.Method(hediffGiverType, "TryApply");
                if (method == null)
                {
                    // Если прямой метод не найден — попробуем искать перегрузки по имени
                    var ms = hediffGiverType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(m => m.Name == "TryApply").ToArray();
                    method = ms.FirstOrDefault();
                }

                if (method == null)
                {
                    Log.Error("[Zoology] AgelessHarmonyInit: couldn't find HediffGiver.TryApply to patch (method==null). Patch skipped.");
                    return;
                }

                var prefix = new HarmonyMethod(typeof(AgelessHarmonyInit).GetMethod(nameof(TryApply_Prefix), BindingFlags.Static | BindingFlags.NonPublic));
                harmony.Patch(method, prefix: prefix);
                Log.Message("[Zoology] AgelessHarmonyInit: patched HediffGiver.TryApply");
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] AgelessHarmonyInit failed to patch: {e}");
            }
        }

        // Prefix: если pawn имеет CompAgeless и hediff от данного giver входит в запрещённый набор — блокируем (return false)
        // Harmony подпараметры: берем __instance (HediffGiver) и pawn; остальное не обязательно для префикса
        private static bool TryApply_Prefix(HediffGiver __instance, Pawn pawn)
        {
            try
            {
                if (pawn == null) return true;

                // Проверим наличие компа Ageless
                var comp = pawn.TryGetComp<CompAgeless>();
                if (comp == null) return true;

                // Получить HediffDef, который пытается назначить этот HediffGiver
                HediffDef candidate = null;
                var gType = __instance.GetType();
                var f = gType.GetField("hediff", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    candidate = f.GetValue(__instance) as HediffDef;
                }
                else
                {
                    var p = gType.GetProperty("hediff", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null)
                        candidate = p.GetValue(__instance) as HediffDef;
                }

                if (candidate == null)
                {
                    // Если сюда попали — возможно giver генерирует не через поле "hediff" — ничего не делаем
                    return true;
                }

                // Получаем для pawn набор age-related hediff'ов
                var forbidden = AgelessUtils.GetAgeRelatedHediffDefsForPawn(pawn);
                if (forbidden == null || forbidden.Count == 0) return true;

                if (forbidden.Contains(candidate))
                {
                    // Заблокировать применение
                    // (возвращаем false — Harmony пропустит оригинал)
                    // Можно логировать единоразово для отладки
                    Log.Message($"[Zoology] blocked applying {candidate.defName} to {pawn.LabelShortCap} from giver {gType.Name}");
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] TryApply_Prefix exception: {e}");
                return true; // не мешаем игре в случае ошибки
            }
        }
    }
}
