using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.DynamicDrawPhaseAt))]
    internal static class Patch_TreeRenderAltitude
    {
        static void Prefix(Pawn ___pawn, ref Vector3 drawLoc)
        {
            if (AnimalDefense.IsInTree(___pawn))
                drawLoc.y = Math.Max(drawLoc.y, AltitudeLayer.MoteOverhead.AltitudeFor());
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawGUIOverlay))]
    internal static class Patch_TreeHeightArrow
    {
        static void Postfix(Pawn __instance)
        {
            if (!AnimalDefense.IsInTree(__instance) || !__instance.Spawned || __instance.Position.Fogged(__instance.Map)) return;
            Vector2 p = __instance.DrawPos.MapToUIPosition();
            p.x -= 13f;
            p.y -= 12f;
            Color yellow = new Color(1f, 0.85f, 0.1f);
            Widgets.DrawLine(p + new Vector2(0, 10), p + new Vector2(0, -4), yellow, 3f);
            Widgets.DrawLine(p + new Vector2(-5, 1), p + new Vector2(0, -4), yellow, 3f);
            Widgets.DrawLine(p + new Vector2(5, 1), p + new Vector2(0, -4), yellow, 3f);
        }
    }

    // Run after animal draft controls as well: both humans and drafted animals share this menu.
    [HarmonyPatch(typeof(FloatMenuUtility), nameof(FloatMenuUtility.GetMeleeAttackAction))]
    internal static class Patch_TreeMeleeMenu
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(Pawn pawn, LocalTargetInfo target, ref string failStr, ref Action __result)
        {
            if (!AnimalDefense.BlocksMelee(pawn, target.Pawn)) return;
            failStr = "Zoology_TreeOutOfMeleeReach".Translate();
            __result = null;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
    internal static class Patch_TreeMeleeOrder
    {
        static bool Prefix(Pawn ___pawn, Job job, ref bool __result)
        {
            if (!AnimalDefense.IsMeleeJob(job) || !AnimalDefense.BlocksMelee(___pawn, job.targetA.Pawn)) return true;
            Messages.Message("Zoology_TreeOutOfMeleeReach".Translate(), job.targetA.Thing, MessageTypeDefOf.RejectInput, false);
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    internal static class Patch_TreeMeleeJobStart
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(Pawn ___pawn, ref Job newJob)
        {
            if (!AnimalDefense.IsMeleeJob(newJob) || !AnimalDefense.BlocksMelee(___pawn, newJob.targetA.Pawn)) return;
            // Also covers queued/AI attacks whose target climbed after selection.
            newJob = JobMaker.MakeJob(JobDefOf.Wait_Combat);
            newJob.expiryInterval = 60;
        }
    }

    [HarmonyPatch]
    internal static class Patch_TreeMeleeContact
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Verb_MeleeAttack), "TryCastShot");
            // CE overrides the cast and can bypass the vanilla implementation.
            Type ce = AccessTools.TypeByName("CombatExtended.Verb_MeleeAttackCE");
            if (ce != null) yield return AccessTools.DeclaredMethod(ce, "TryCastShot");
        }
        static bool Prefix(Verb __instance, ref bool __result)
        {
            if (!AnimalDefense.BlocksMelee(__instance.CasterPawn, __instance.CurrentTarget.Pawn)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Toils_Combat), nameof(Toils_Combat.FollowAndMeleeAttack),
        new[] { typeof(TargetIndex), typeof(TargetIndex), typeof(Action) })]
    internal static class Patch_TreeMeleeFollow
    {
        static void Postfix(Toil __result, TargetIndex targetInd)
        {
            // Covers already running melee toils, including protection jobs and modded users.
            __result.AddFailCondition(() => AnimalDefense.BlocksMelee(__result.actor,
                __result.actor.CurJob?.GetTarget(targetInd).Pawn));
        }
    }
}
