// ModExtension_CannotBeMutated.cs

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // Маркер на уровне def (ThingDef или PawnKindDef)
    public class ModExtension_CannotBeMutated : DefModExtension
    {
        // Можно добавить параметры в будущем, например сообщения, привилегии и т.д.
    }

    // Утилита/extension для удобных проверок
    public static class CannotBeMutatedUtil
    {
        // Проверяет деф/ PawnKind на наличие мод-расширения.
        // Возвращает true если pawn помечен как "нельзя мутировать".
        public static bool IsCannotBeMutated(this Pawn pawn)
        {
            if (pawn == null) return false;

            // Сначала проверяем конкретный ThingDef (раса)
            if (pawn.def?.GetModExtension<ModExtension_CannotBeMutated>() != null) return true;

            // Потом проверяем PawnKindDef (если маркер поставлен на уровне PawnKind)
            if (pawn.kindDef?.GetModExtension<ModExtension_CannotBeMutated>() != null) return true;

            return false;
        }
    }

    // 1) Отключаем цель в CompTargetable_AllAnimalsOnTheMap.ValidateTarget
    [HarmonyPatch(typeof(CompTargetable_AllAnimalsOnTheMap), "ValidateTarget")]
    public static class Patch_CompTargetable_AllAnimalsOnTheMap_ValidateTarget_CannotBeMutated
    {
        public static void Postfix(CompTargetable_AllAnimalsOnTheMap __instance, LocalTargetInfo target, bool showMessages, ref bool __result)
        {
            if (!__result) return;

            Pawn pawn = target.Thing as Pawn;
            if (pawn != null && pawn.IsCannotBeMutated())
            {
                __result = false;
                // Без сообщения — просто игнорируем как недопустимую цель
            }
        }
    }

    // 2) Verb_CastTargetEffectBiomutationLance (если в игре есть этот класс) — проверяем первыми
    [HarmonyPatch(typeof(Verb_CastTargetEffectBiomutationLance), "ValidateTarget")]
    public static class Patch_Verb_CastTargetEffectBiomutationLance_ValidateTarget_CannotBeMutated
    {
        public static bool Prefix(Verb_CastTargetEffectBiomutationLance __instance, LocalTargetInfo target, bool showMessages, ref bool __result)
        {
            Pawn pawn = target.Pawn;
            if (pawn != null && pawn.IsCannotBeMutated())
            {
                __result = false;
                if (showMessages)
                {
                    Messages.Message("MessageBiomutationLanceInvalidTargetRace".Translate(pawn), __instance.caster, MessageTypeDefOf.RejectInput, null);
                }
                return false; // блокируем выполнение оригинала
            }
            return true;
        }
    }

    // 3) Защита для FleshbeastUtility.SpawnFleshbeastFromPawn (сторонние моды)
    [HarmonyPatch(typeof(FleshbeastUtility), "SpawnFleshbeastFromPawn")]
    public static class Patch_FleshbeastUtility_SpawnFleshbeastFromPawn_CannotBeMutated
    {
        // Предположительно оригинальная сигнатура: public static Pawn SpawnFleshbeastFromPawn(Pawn pawn, ...)
        // Здесь мы делаем Prefix с тем же первым аргументом — чтобы отменить вызов при пометке.
        public static bool Prefix(Pawn pawn)
        {
            if (pawn != null && pawn.IsCannotBeMutated())
            {
                return false;  // Просто прерываем без сообщения
            }
            return true;
        }
    }

    // 4) Исключаем помеченных животных из TryMutatingRandomAnimal в CompObelisk_Mutator
    [HarmonyPatch(typeof(CompObelisk_Mutator), "TryMutatingRandomAnimal")]
    public static class Patch_CompObelisk_Mutator_TryMutatingRandomAnimal_CannotBeMutated
    {
        // Подпись соответствует примеру: Prefix(CompObelisk_Mutator __instance, ref bool __result, ref Pawn mutatedAnimal, ref Pawn resultBeast)
        public static bool Prefix(CompObelisk_Mutator __instance, ref bool __result, ref Pawn mutatedAnimal, ref Pawn resultBeast)
        {
            mutatedAnimal = null;
            resultBeast = null;

            if (__instance?.parent?.Map == null)
            {
                __result = false;
                return false; // не выполнять оригинал
            }

            // Переопределяем LINQ-запрос, исключая сущностей с ModExtension_CannotBeMutated
            IEnumerable<Pawn> candidates = from pawn in __instance.parent.Map.mapPawns.AllPawnsSpawned
                                           where pawn.Faction == null &&
                                                 pawn.IsAnimal &&
                                                 !pawn.IsCannotBeMutated() && // Исключаем помеченных
                                                 !pawn.Position.Fogged(__instance.parent.Map)
                                           select pawn;

            Pawn pawn3;
            if (candidates.TryRandomElement(out pawn3))
            {
                mutatedAnimal = pawn3;
                resultBeast = FleshbeastUtility.SpawnFleshbeastFromPawn(pawn3, false, false, Array.Empty<PawnKindDef>());
                if (resultBeast != null)
                {
                    EffecterDefOf.ObeliskSpark.Spawn(__instance.parent.Position, __instance.parent.Map, 1f).Cleanup();
                    __result = true;
                    return false;  // Не выполняем оригинал
                }
            }

            __result = false;
            return false;
        }
    }

    // 5) Делаем помеченных ModExtension_CannotBeMutated недопустимой целью для CompAbilityEffect_PsychicSlaughter
    [HarmonyPatch(typeof(CompAbilityEffect_PsychicSlaughter), "Valid")]
    public static class Patch_CompAbilityEffect_PsychicSlaughter_Valid_CannotBeMutated
    {
        public static void Postfix(CompAbilityEffect_PsychicSlaughter __instance, LocalTargetInfo target, bool throwMessages, ref bool __result)
        {
            try
            {
                if (!__result) return; // если уже невалидно — ничего не делаем

                Pawn pawn = target.Pawn;
                if (pawn == null) return;

                // Используем централизованную проверку из Zoology
                if (pawn.IsCannotBeMutated())
                {
                    // Показываем сообщение, если нужно
                    if (throwMessages)
                    {
                        const string key = "PhotonozoaCannotBeTargetedByPsychicSlaughter";
                        string text = key.Translate(pawn.Named("PAWN"));
                        if (text == key) // если перевод отсутствует — подставляем запасной текст
                        {
                            text = $"Cannot target {pawn.LabelShort} with Psychic Slaughter.";
                        }
                        Messages.Message(text, pawn, MessageTypeDefOf.RejectInput);
                    }

                    __result = false;
                }
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] Patch_CompAbilityEffect_PsychicSlaughter_Valid_CannotBeMutated error: {e}");
            }
        }
    }
}