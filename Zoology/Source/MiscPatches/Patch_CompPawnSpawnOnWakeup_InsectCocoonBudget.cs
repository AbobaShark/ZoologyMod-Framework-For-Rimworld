using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(CompPawnSpawnOnWakeup), "GeneratePawns")]
    internal static class Patch_CompPawnSpawnOnWakeup_InsectCocoonBudget
    {
        private static bool Prepare()
        {
            ZoologyModSettings settings = ZoologyMod.Settings ?? ZoologyModSettings.Instance;
            return ModsConfig.BiotechActive
                && (settings?.EnableInsectCocoonSpawnFix ?? ModConstants.DefaultEnableInsectCocoonSpawnFix)
                && !(settings?.DisableAllRuntimePatches ?? ModConstants.DefaultDisableAllRuntimePatches);
        }

        internal static void Prefix(CompPawnSpawnOnWakeup __instance)
        {
            if (__instance == null
                || __instance.points <= 0f
                || __instance.parent?.def?.building?.isInsectCocoon != true)
            {
                return;
            }

            CompProperties_PawnSpawnOnWakeup props =
                __instance.props as CompProperties_PawnSpawnOnWakeup;
            if (props?.spawnablePawnKinds == null || props.spawnablePawnKinds.Count == 0)
            {
                return;
            }

            float cheapestCombatPower = float.MaxValue;
            for (int i = 0; i < props.spawnablePawnKinds.Count; i++)
            {
                PawnKindDef pawnKind = props.spawnablePawnKinds[i];
                if (pawnKind == null
                    || pawnKind.combatPower <= 0f
                    || float.IsNaN(pawnKind.combatPower)
                    || float.IsInfinity(pawnKind.combatPower))
                {
                    continue;
                }

                if (pawnKind.combatPower <= __instance.points)
                {
                    return;
                }

                if (pawnKind.combatPower < cheapestCombatPower)
                {
                    cheapestCombatPower = pawnKind.combatPower;
                }
            }

            if (cheapestCombatPower < float.MaxValue)
            {
                // Vanilla otherwise generates an empty list, clears points, and destroys the
                // cocoon. Raising this instance's budget lets vanilla generate exactly one of
                // the configured pawn kinds without mutating shared defs or replacing Spawn().
                __instance.points = cheapestCombatPower;
            }
        }
    }
}
