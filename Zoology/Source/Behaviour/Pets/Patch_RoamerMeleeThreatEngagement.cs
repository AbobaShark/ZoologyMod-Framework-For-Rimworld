using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(Verb_MeleeAttack), "TryCastShot")]
    internal static class Patch_RoamerMeleeThreatEngagement
    {
        private static bool Prepare()
        {
            ZoologyModSettings settings = ZoologyModSettings.Instance;
            return settings == null || !settings.DisableAllRuntimePatches;
        }

        private static void Postfix(Verb_MeleeAttack __instance)
        {
            Pawn caster = __instance?.CasterPawn;
            Pawn target = __instance?.CurrentTarget.Pawn;
            if (!ShouldRestoreRoamerMeleeRetaliationState(target, caster))
            {
                return;
            }

            Pawn_MindState mindState = target.mindState;
            int currentTick = Find.TickManager?.TicksGame ?? -1;
            if (mindState == null
                || currentTick < 0
                || !ReferenceEquals(mindState.meleeThreat, caster)
                || mindState.lastMeleeThreatHarmTick != currentTick)
            {
                return;
            }

            // Vanilla roamer threat suppression is lifted by a fresh engage tick.
            // Marking the victim as engaged lets the normal close-melee threat logic
            // react without forcing custom attack jobs or global hostility overrides.
            mindState.lastEngageTargetTick = currentTick;
        }

        private static bool ShouldRestoreRoamerMeleeRetaliationState(Pawn target, Pawn caster)
        {
            return target != null
                && caster != null
                && !ReferenceEquals(target, caster)
                && target.Spawned
                && caster.Spawned
                && !target.Dead
                && !caster.Dead
                && !target.Destroyed
                && !caster.Destroyed
                && target.IsAnimal
                && target.Roamer
                && ReferenceEquals(target.Faction, Faction.OfPlayerSilentFail);
        }
    }
}
