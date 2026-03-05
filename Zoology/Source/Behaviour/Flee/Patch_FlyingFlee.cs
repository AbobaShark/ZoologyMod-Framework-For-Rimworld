// Patch_FlyingFlee.cs

using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using System.Reflection;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(Pawn_FlightTracker), "Notify_JobStarted")]
    public static class Patch_FlyingFleeStart
    {
        public static bool Prefix(Pawn_FlightTracker __instance, Job job)
        {
            try
            {
                // Защита от null: иногда job или job.def может быть null — не лезем в оригинал.
                if (job == null || job.def == null)
                    return true; // Отдаём управление оригинальному методу

                // Нас интересует только JobDefOf.Flee
                if (job.def != JobDefOf.Flee)
                    return true;

                // Получаем поле pawn приватно через Reflection
                var pawnField = AccessTools.Field(typeof(Pawn_FlightTracker), "pawn");
                if (pawnField == null)
                    return true; // Если по какой-то причине поле не найдено — не вмешиваемся

                var pawn = pawnField.GetValue(__instance) as Pawn;
                if (pawn == null)
                    return true; // Неизвестный pawn — пропускаем

                // Только для животных, которые вообще могут летать
                if (!pawn.RaceProps?.Animal == true) // защита на случай null RaceProps (очень редко)
                    return true;
                if (!__instance.CanEverFly)
                    return true;

                // Теперь — действительно Flee + животное + может летать.
                // Заставляем flee всегда использовать полёт и пытаемся мгновенно стартовать,
                // при этом пропуская оригинальный код, чтобы избежать ForceLand() в некоторых ситуациях.
                job.flying = true;

                // Попробуем сбросить cooldown (если поле присутствует)
                var cooldownField = AccessTools.Field(typeof(Pawn_FlightTracker), "flightCooldownTicks");
                if (cooldownField != null)
                {
                    cooldownField.SetValue(__instance, 0);
                }

                // Если не летим сейчас — запустить взлёт внутренним методом
                bool isFlying;
                // Доступ к свойству Flying может выбросить, но в нормальных условиях безопасен
                try
                {
                    isFlying = __instance.Flying;
                }
                catch
                {
                    // Если по какой-то причине нельзя прочитать свойство — просто не вызывать StartFlyingInternal
                    return false; // мы уже установили job.flying = true, пропускаем оригинал
                }

                if (!isFlying)
                {
                    var startFlyingInternalMethod = AccessTools.Method(typeof(Pawn_FlightTracker), "StartFlyingInternal");
                    if (startFlyingInternalMethod != null)
                    {
                        startFlyingInternalMethod.Invoke(__instance, null);
                    }
                }

                // Возвращаем false — чтобы оригинал не выполнял свою логику (включая ForceLand).
                return false;
            }
            catch (System.Exception ex)
            {
                // В случае любой ошибки — логируем и не вмешиваемся (чтобы не крашить игру).
                Log.ErrorOnce($"ZoologyMod: exception in Patch_FlyingFleeStart Prefix: {ex}\nPatch will defer to original method to avoid crashes.", 1234567);
                return true;
            }
        }
    }
}