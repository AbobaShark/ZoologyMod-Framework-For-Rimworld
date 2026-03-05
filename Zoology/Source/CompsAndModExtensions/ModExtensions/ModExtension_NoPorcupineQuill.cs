// ModExtension_NoPorcupineQuill.cs

using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Маркер/настройки на уровне def — можно расширить полями в будущем
    public class ModExtension_NoPorcupineQuill : DefModExtension
    {
        // Для будущих опций, например:
        // public int cleanupIntervalTicks = 6000;
    }

    [StaticConstructorOnStartup]
    public static class NoPorcupineQuill_HarmonyPatches
    {
        // интервал (в тиках) для периодической очистки (примерно 6000 тиков ~ 100 секунд)
        private const int CleanupIntervalTicks = 6000;

        static NoPorcupineQuill_HarmonyPatches()
        {
            try
            {
                var harmony = new Harmony("com.abobashark.zoology.noporcupinequill");

                // Патчим HediffSet.AddDirect чтобы предотвращать добавление хедиффа
                var addDirect = AccessTools.Method(typeof(HediffSet), "AddDirect");
                if (addDirect != null)
                {
                    var prefix = new HarmonyMethod(typeof(NoPorcupineQuill_HarmonyPatches), nameof(AddDirect_Prefix));
                    harmony.Patch(addDirect, prefix: prefix);
                }
                else
                {
                    Log.Warning("[Zoology.NoPorcupineQuill] HediffSet.AddDirect not found - can't patch add-blocking.");
                }

                // Патчим Pawn.Tick — в префиксе будем иногда проверять и удалять хедифф у конкретного pawn'а.
                // Используем IsHashIntervalTick, чтобы распределить нагрузку.
                var pawnTick = AccessTools.Method(typeof(Pawn), "Tick");
                if (pawnTick != null)
                {
                    var pawnTickPrefix = new HarmonyMethod(typeof(NoPorcupineQuill_HarmonyPatches), nameof(PawnTick_Prefix));
                    harmony.Patch(pawnTick, prefix: pawnTickPrefix);
                }
                else
                {
                    Log.Warning("[Zoology.NoPorcupineQuill] Pawn.Tick not found - can't patch periodic cleanup.");
                }
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology.NoPorcupineQuill] patch init error: {e}");
            }
        }

        // Prefix для HediffSet.AddDirect: если pawn имеет ModExtension_NoPorcupineQuill и добавляемый hediff — PorcupineQuill,
        // то блокируем добавление (return false).
        public static bool AddDirect_Prefix(HediffSet __instance, Hediff hediff)
        {
            try
            {
                if (hediff == null || __instance?.pawn == null) return true;

                var pawn = __instance.pawn;

                // Проверяем наличие нашего ModExtension на дефе pawn'а
                var ext = pawn.def?.GetModExtension<ModExtension_NoPorcupineQuill>();
                if (ext == null) return true; // нет маркера — выполняем оригинал

                // Проверяем наличие дефина порк-иглы (без выброса исключений, если DLC нет)
                var porcupineDef = DefDatabase<HediffDef>.GetNamedSilentFail("PorcupineQuill");
                if (porcupineDef == null)
                {
                    // DLC/hediff не установлен — просто ничего не делаем
                    return true;
                }

                // Блокировка по дефу (наиболее безопасно)
                if (hediff.def == porcupineDef)
                {
                    // логируем только в режиме разработчика, чтобы не засорять лог
                    if (Prefs.DevMode)
                        Log.Message($"[Zoology.NoPorcupineQuill] prevented adding {hediff.def.defName} to pawn {pawn.LabelShort} ({pawn.ThingID})");

                    return false; // отменяем AddDirect
                }

                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology.NoPorcupineQuill] error in AddDirect_Prefix: {e}");
                // В случае ошибки не ломать игру — дать выполниться оригиналу
                return true;
            }
        }

        // Prefix для Pawn.Tick: раз в CleanupIntervalTicks у этого pawn'а (распределённо) — удаляем hediff, если он есть.
        // Возвращаем true чтобы оригинальный Tick выполнялся как обычно.
        public static void PawnTick_Prefix(Pawn __instance)
        {
            try
            {
                var pawn = __instance;
                if (pawn == null) return;

                // быстрый и лёгкий спрединг проверки: IsHashIntervalTick использует thingID чтобы равномерно распределять
                if (!pawn.IsHashIntervalTick(CleanupIntervalTicks)) return;

                // Проверяем маркер мод-расширения на дефе (только тогда будем продолжать)
                var ext = pawn.def?.GetModExtension<ModExtension_NoPorcupineQuill>();
                if (ext == null) return;

                // Проверяем наличие дефина порк-иглы (без исключений, если DLC не установлен)
                var porcupineDef = DefDatabase<HediffDef>.GetNamedSilentFail("PorcupineQuill");
                if (porcupineDef == null) return;

                var hediffSet = pawn.health?.hediffSet;
                if (hediffSet == null) return;

                var existing = hediffSet.GetFirstHediffOfDef(porcupineDef, false);
                if (existing != null)
                {
                    // удаляем безопасно
                    pawn.health.RemoveHediff(existing);
                    if (Prefs.DevMode)
                        Log.Message($"[Zoology.NoPorcupineQuill] removed {porcupineDef.defName} from {pawn.LabelShort} ({pawn.ThingID})");
                }
            }
            catch (Exception e)
            {
                // не ломаем игру при ошибках
                Log.Error($"[Zoology.NoPorcupineQuill] error in PawnTick_Prefix cleanup: {e}");
            }
        }
    }
}