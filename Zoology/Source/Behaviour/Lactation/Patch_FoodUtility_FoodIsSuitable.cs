// Patch_FoodUtility_FoodIsSuitable.cs

using System;
using HarmonyLib;
using Verse;
using RimWorld;
using Verse.AI;

namespace ZoologyMod
{
    // Точечный Prefix: если pawn имеет IsMammal и находится на life stage "AnimalBaby",
    // то требуем, чтобы food.ingestible.babiesCanIngest == true и раса могла есть этот food.
    // В остальных случаях — не вмешиваемся (возвращаем true -> оригинальная логика выполнится).
    [HarmonyPatch(typeof(FoodUtility), "FoodIsSuitable", new Type[] { typeof(Pawn), typeof(ThingDef) })]
    static class Patch_FoodUtility_FoodIsSuitable
    {
        static bool Prefix(Pawn p, ThingDef food, ref bool __result)
        {
            try
            {
                // Если настройки недоступны или фича выключена — не вмешиваемся
                var settings = ZoologyModSettings.Instance;
                if (settings == null || !ZoologyModSettings.EnableMammalLactation)
                {
                    return true;
                }

                if (p == null || food == null) return true; // не мешаем оригиналу

                // Если у pawn нет нашего IsMammal — не трогаем ваниль
                if (!p.IsMammal()) return true;

                // Без needs.food — не едят
                if (p.needs?.food == null)
                {
                    __result = false;
                    return false;
                }

                // Получаем текущую life stage
                var curStage = p.ageTracker?.CurLifeStage;
                if (curStage == null)
                {
                    // если нет — не вмешиваемся
                    return true;
                }

                // Если это именно стадия "AnimalBaby" (обычно defName == "AnimalBaby"),
                // то применяем поведение "только babiesCanIngest".
                // Это точечная проверка по имени стадии, как вы просили.
                if (string.Equals(curStage.defName, "AnimalBaby", StringComparison.OrdinalIgnoreCase))
                {
                    // Только разрешаем пищу с babiesCanIngest == true и которую раса вообще может употреблять.
                    bool ok = (food.ingestible != null && food.ingestible.babiesCanIngest) && p.RaceProps.CanEverEat(food);
                    __result = ok;
                    return false; // пропускаем оригинал
                }

                // Для остальных стадий (juvenile/adult) — не вмешиваемся.
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("ZoologyMod: Patch_FoodUtility_FoodIsSuitable Prefix error: " + ex);
                return true; // в случае ошибки — пусть оригинал выполнится
            }
        }
    }

    // ------------------------------------------------------------
    // Запрещаем детёнышам-млекопитающим рыбачить.
    // ------------------------------------------------------------
    [HarmonyPatch(typeof(JobGiver_GetFood), "TryFindFishJob", new Type[] { typeof(Pawn) })]
    static class Patch_JobGiver_GetFood_TryFindFishJob_BlockForMammalBabies
    {
        static bool Prefix(Pawn pawn, ref Job __result)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings == null || !ZoologyModSettings.EnableMammalLactation)
                {
                    // фича выключена — не вмешиваемся
                    return true;
                }

                if (pawn == null) return true;

                // Только для наших млекопитающих-детёнышей блокируем рыбалку
                if (!pawn.IsMammal()) return true;

                // Без needs.food — не должны заниматься добычей пищи
                if (pawn.needs?.food == null)
                {
                    __result = null;
                    return false;
                }

                var curStage = pawn.ageTracker?.CurLifeStage;
                if (curStage != null && string.Equals(curStage.defName, "AnimalBaby", StringComparison.OrdinalIgnoreCase))
                {
                    // Запрещаем возвращать задание рыбалки для детёныша
                    __result = null;
                    return false; // пропустить оригинал
                }

                // Иначе — не мешаем ванильной логике
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] Patch_TryFindFishJob Prefix failed: {ex}");
                return true;
            }
        }
    }

    // ------------------------------------------------------------
    // Запрещаем детёнышам-млекопитающим пожирать трупы (Corpse).
    // Это патчит перегрузку WillEat(this Pawn p, Thing food, Pawn getter = null, bool careIfNotAcceptableForTitle = true, bool allowVenerated = false)
    // — ранний Prefix вернёт false для животных-детёнышей, если food is Corpse.
    // ------------------------------------------------------------
    [HarmonyPatch(typeof(FoodUtility), "WillEat", new Type[] { typeof(Pawn), typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool) })]
    static class Patch_FoodUtility_WillEat_Thing_CorpseBlockForMammalBabies
    {
        static bool Prefix(Pawn p, Thing food, Pawn getter, bool careIfNotAcceptableForTitle, bool allowVenerated, ref bool __result)
        {
            try
            {
                // Включаем только когда соответствующая опция активна
                var settings = ZoologyModSettings.Instance;
                if (settings == null || !ZoologyModSettings.EnableMammalLactation)
                    return true; // не вмешиваемся

                if (p == null || food == null) return true;

                // Если у pawn нет нашего IsMammal — не трогаем ваниль
                if (!p.IsMammal()) return true;

                // Без needs.food — ведём себя как в FoodIsSuitable (ваниль)
                if (p.needs?.food == null)
                {
                    __result = false;
                    return false;
                }

                // Тот же признак детёныша, что и в вашем FoodIsSuitable (точечная проверка по имени стадии)
                var curStage = p.ageTracker?.CurLifeStage;
                if (curStage == null) return true;

                if (string.Equals(curStage.defName, "AnimalBaby", StringComparison.OrdinalIgnoreCase))
                {
                    // Если это реальный объект-корпус — запрещаем поедание
                    // (Corpse — конкретный тип в игре; это самый надёжный способ определения трупа)
                    if (food is Corpse)
                    {
                        __result = false;
                        return false; // skip original -> запретить есть этот Corpse
                    }

                    // Дополнительно: если ThingDef явно отмечен как "Corpse" (защита на случай),
                    // попытаемся проверить безопасно (в некоторых версиях может быть свойство IsCorpse).
                    try
                    {
                        var td = food.def;
                        if (td != null)
                        {
                            // Проверяем common-named property via reflection defensively:
                            var prop = td.GetType().GetProperty("IsCorpse");
                            if (prop != null)
                            {
                                var val = prop.GetValue(td, null);
                                if (val is bool b && b)
                                {
                                    __result = false;
                                    return false;
                                }
                            }
                        }
                    }
                    catch { /* не критично, продолжаем ванильную логику */ }
                }

                // Иначе не вмешиваемся
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] Patch_FoodUtility_WillEat_Thing Prefix failed: {ex}");
                return true; // в сомнительном случае — дать ваниль выполнить
            }
        }
    }
}