// Patch_VerbIsStillUsableBy.cs

using System;
using System.Reflection;
using Verse;

namespace ZoologyMod
{
    // Используем полностью квалифицированный атрибут, чтобы не зависеть от using HarmonyLib;
    [HarmonyLib.HarmonyPatch(typeof(Verb), "IsStillUsableBy")]
    internal static class Patch_VerbIsStillUsableBy
    {
        // Postfix чтобы не ломать ванильную логику, а лишь дополнять её
        internal static void Postfix(Verb __instance, ref bool __result, Pawn pawn)
        {
            try
            {
                if (!__result) return; // если уже недоступно — ничего не делаем

                var tool = __instance.tool;
                if (tool == null) return;

                // 1) Если это наш класс ToolWithGender — быстрый путь
                var asOur = tool as ToolWithGender;
                if (asOur != null)
                {
                    __result = (asOur.restrictedGender == Gender.None) || (asOur.restrictedGender == pawn.gender);
                    return;
                }

                // 2) Универсальный путь: ищем поле "restrictedGender" через reflection.
                //    Это позволяет также работать с CombatExtended.ToolCE (если он присутствует)
                var t = tool.GetType();
                var fld = t.GetField("restrictedGender", BindingFlags.Public | BindingFlags.Instance);
                if (fld != null && fld.FieldType == typeof(Gender))
                {
                    var val = (Gender)fld.GetValue(tool);
                    if (val != Gender.None)
                    {
                        __result = (val == pawn.gender);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Error in Patch_VerbIsStillUsableBy.Postfix: {ex}");
                // не мешаем работе игры, просто логируем ошибку
            }
        }
    }
}