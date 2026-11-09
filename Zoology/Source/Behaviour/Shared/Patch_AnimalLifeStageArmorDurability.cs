using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace ZoologyMod
{
    // A CE comp stores natural-armor durability independently from LifeStageDef.
    // Patch the existing CE property rather than cloning comp props shared by
    // all age stages of the species. All scale factors are from AnimalStats.
    [HarmonyPatch]
    internal static class Patch_AnimalLifeStageArmorDurability
    {
        private sealed class Snapshot
        {
            public float maximum;
            public bool initialized;
        }

        private static readonly ConditionalWeakTable<ThingComp, Snapshot> Snapshots =
            new ConditionalWeakTable<ThingComp, Snapshot>();
        private static readonly Type CECompType =
            AccessTools.TypeByName("CombatExtended.CompArmorDurability");
        private static readonly FieldInfo CurrentField =
            CECompType == null ? null : AccessTools.Field(CECompType, "curDurability");

        private static bool Prepare()
        {
            return CEChecker.IsCEInstalled() && CECompType != null && CurrentField != null
                && AccessTools.PropertyGetter(CECompType, "maxDurability") != null
                && ZoologyAnimalGrowth.Enabled;
        }

        private static MethodBase TargetMethod()
        {
            return CECompType == null ? null
                : AccessTools.PropertyGetter(CECompType, "maxDurability");
        }

        private static void Postfix(ThingComp __instance, ref float __result)
        {
            if (__instance?.parent is not Pawn pawn || pawn.RaceProps?.Animal != true
                || !(__result > 0f) || float.IsNaN(__result) || float.IsInfinity(__result))
                return;

            float factor;
            bool scaled = ZoologyAnimalGrowth.TryGetDurability(pawn, out factor)
                || ZoologyLifeStageProfileInstaller.TryGetArmorDurabilityFactor(
                    pawn, out factor);
            Snapshot snapshot;
            if (!Snapshots.TryGetValue(__instance, out snapshot) && !scaled)
                return;

            float maximum = __result * (scaled ? factor : 1f)
                * ZoologySexSizeMechanics.DurabilityRatio(pawn);
            // A legitimate MROUND-to-25 in AnimalStats can produce zero.
            if (maximum < 0f || float.IsNaN(maximum) || float.IsInfinity(maximum))
                return;

            if (snapshot == null)
                snapshot = Snapshots.GetValue(__instance, _ => new Snapshot());

            // Preserve the remaining durability fraction during growth.
            // A comp's maximum changes with age without replacing its shared
            // CompProperties_ArmorDurability or adding a polling tick.
            if (snapshot.maximum > 0f
                && Math.Abs(snapshot.maximum - maximum) > 0.00001f)
            {
                float current = (float)CurrentField.GetValue(__instance);
                CurrentField.SetValue(__instance,
                    Math.Max(0f, Math.Min(maximum,
                        current * maximum / snapshot.maximum)));
            }
            else if (snapshot.maximum == 0f)
            {
                float current = (float)CurrentField.GetValue(__instance);
                if (snapshot.initialized && maximum > 0f)
                {
                    // Growing from a stage with no durable natural armor:
                    // restore the initially full capacity of the new stage.
                    CurrentField.SetValue(__instance, maximum);
                }
                else if (current > maximum)
                {
                    // Handles new pawns and old saves with adult durability.
                    CurrentField.SetValue(__instance, maximum);
                }
            }

            snapshot.maximum = maximum;
            snapshot.initialized = true;
            __result = maximum;
        }
    }

    [HarmonyPatch]
    internal static class Patch_AnimalLifeStageArmorDurabilityPercent
    {
        private static readonly Type CECompType =
            AccessTools.TypeByName("CombatExtended.CompArmorDurability");

        private static bool Prepare()
        {
            return CEChecker.IsCEInstalled() && CECompType != null
                && AccessTools.PropertyGetter(CECompType, "curDurabilityPercent") != null
                && ZoologyAnimalGrowth.Enabled;
        }

        private static MethodBase TargetMethod()
        {
            return CECompType == null ? null
                : AccessTools.PropertyGetter(CECompType, "curDurabilityPercent");
        }

        private static void Postfix(ThingComp __instance, ref float __result)
        {
            // CE divides curDurability by maxDurability in this getter.
            // For an AnimalStats-derived maximum of exactly zero, 0/0 is NaN.
            if (__instance?.parent is Pawn pawn && pawn.RaceProps?.Animal == true
                && float.IsNaN(__result))
            {
                __result = 0f;
            }
        }
    }
}
