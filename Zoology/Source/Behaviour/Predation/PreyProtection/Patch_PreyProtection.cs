// Patch_PreyProtection.cs

using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // Указываем точную сигнатуру метода, чтобы Harmony выбрал нужную перегрузку.
    [HarmonyPatch(typeof(Toils_Ingest))]
    [HarmonyPatch("ChewIngestible", new Type[] { typeof(Pawn), typeof(float), typeof(TargetIndex), typeof(TargetIndex) })]
    public static class Patch_ToilsIngest_ChewIngestible
    {
        // Postfix, который получает Toil (результат) и добавляет initAction.
        // initAction выполняется в момент, когда pawn действительно начинает этот toil,
        // поэтому это хорошее место, чтобы триггерить защиту трупа.
        public static void Postfix(Toil __result, Pawn chewer, float durationMultiplier, TargetIndex ingestibleInd, TargetIndex eatSurfaceInd)
        {
            try
            {
                if (__result == null) return;

                // Сохраняем ссылку на возможный существующий initAction, чтобы выполнять его после нашего
                Action originalInit = __result.initAction;

                // Добавляем нашу инициализацию — она будет вызываться когда pawn начнёт выполнять этот Toil.
                __result.initAction = () =>
                {
                    try
                    {
                        // Получаем pawn, который выполняет toil. Toil.actor заполняется движком при исполнении.
                        Pawn actor = __result.actor as Pawn;
                        if (actor == null) actor = chewer; // страховка — chewer обычно совпадает
                        if (actor != null)
                        {
                            Job curJob = actor.CurJob;
                            if (curJob != null)
                            {
                                LocalTargetInfo targ = curJob.GetTarget(ingestibleInd);
                                if (targ.HasThing)
                                {
                                    Thing t = targ.Thing;
                                    if (t != null)
                                    {
                                        // Если это труп — триггерим защиту. Если не труп — просто продолжаем.
                                        Corpse corp = t as Corpse;
                                        if (corp != null)
                                        {
                                            var comp = PredatorPreyPairGameComponent.Instance;
                                            if (comp != null)
                                            {
                                                comp.TryTriggerDefendFor(corp, actor);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Zoology: Patch_ToilsIngest_ChewIngestible initAction exception: {ex}");
                    }

                    // Выполняем оригинальный initAction, если он был — важно: делать это всегда,
                    // иначе vanilla-логика установки ticksLeftThisToil не выполнится и еда будет съедаться мгновенно.
                    try
                    {
                        originalInit?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Zoology: Patch_ToilsIngest_ChewIngestible: original initAction threw: {ex}");
                    }
                };
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: Patch_ToilsIngest_ChewIngestible Postfix exception: {ex}");
            }
        }
    }
}