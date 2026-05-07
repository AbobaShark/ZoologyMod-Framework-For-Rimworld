using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryStartCarry), new[] { typeof(Thing) })]
    internal static class Patch_Childcare_DefendEggs_OnCarry
    {
        public static bool Prepare() => ChildcareDefenseUtility.IsEggProtectionEnabled;

        public static void Postfix(Pawn_CarryTracker __instance, Thing item, bool __result)
        {
            if (!__result)
            {
                return;
            }

            TryTriggerCarryProtection(__instance, item);
        }

        private static void TryTriggerCarryProtection(Pawn_CarryTracker carryTracker, Thing item)
        {
            if (!ChildcareDefenseUtility.IsEggProtectionEnabled || item == null)
            {
                return;
            }

            try
            {
                Pawn carrier = PredationLookupUtility.TryGetCarrierPawn(carryTracker);
                if (carrier == null)
                {
                    carrier = PredationLookupUtility.FindPawnHoldingThing(item.thingIDNumber);
                }

                if (carrier != null)
                {
                    Thing protectedEgg = carrier.carryTracker?.CarriedThing ?? item;
                    ChildcareDefenseUtility.TryTriggerEggProtection(carrier, protectedEgg);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: Patch_Childcare_DefendEggs_OnCarry Postfix exception: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryStartCarry), new[] { typeof(Thing), typeof(int), typeof(bool) })]
    internal static class Patch_Childcare_DefendEggs_OnCarryCount
    {
        public static bool Prepare() => ChildcareDefenseUtility.IsEggProtectionEnabled;

        public static void Postfix(Pawn_CarryTracker __instance, Thing item, int count, bool reserve, int __result)
        {
            if (__result <= 0)
            {
                return;
            }

            Patch_Childcare_DefendEggs_OnCarry.Postfix(__instance, item, true);
        }
    }

    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.FoodOptimality))]
    internal static class Patch_Childcare_GuardedEggFoodOptimality
    {
        public static bool Prepare() => ChildcareDefenseUtility.IsEggProtectionEnabled;

        public static void Postfix(Pawn eater, Thing foodSource, ThingDef foodDef, float dist, bool takingToInventory, ref float __result)
        {
            if (!ChildcareDefenseUtility.IsEggProtectionEnabled || eater == null || foodSource == null)
            {
                return;
            }

            try
            {
                __result += ChildcareDefenseUtility.GetFoodOptimalityDeltaForEgg(eater, foodSource);
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: Patch_Childcare_GuardedEggFoodOptimality Postfix exception: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.WillEat), new[] { typeof(Pawn), typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool) })]
    internal static class Patch_Childcare_BlockOwnSpeciesEggEating
    {
        public static bool Prepare() => ChildcareDefenseUtility.IsEggProtectionEnabled;

        public static void Postfix(Pawn p, Thing food, Pawn getter, bool careIfNotAcceptableForTitle, bool allowVenerated, ref bool __result)
        {
            if (!__result || !ChildcareDefenseUtility.IsEggProtectionEnabled || p == null || food == null)
            {
                return;
            }

            try
            {
                if (ChildcareDefenseUtility.ShouldBlockEggConsumption(p, food))
                {
                    __result = false;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: Patch_Childcare_BlockOwnSpeciesEggEating Postfix exception: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(SteadyEnvironmentEffects), nameof(SteadyEnvironmentEffects.FinalDeteriorationRate), new[] { typeof(Thing), typeof(List<string>) })]
    internal static class Patch_Childcare_EggDeterioration
    {
        public static bool Prepare() => ChildcareDefenseUtility.IsEggProtectionEnabled;

        public static void Postfix(Thing t, List<string> reasons, ref float __result)
        {
            if (!ChildcareDefenseUtility.IsEggProtectionEnabled || t == null || __result <= 0f)
            {
                return;
            }

            try
            {
                if (!ChildcareDefenseUtility.ShouldNullifyEggDeterioration(t))
                {
                    return;
                }

                __result = 0f;
                reasons?.Clear();
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: Patch_Childcare_EggDeterioration Postfix exception: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(CompEggLayer), "ProduceEgg")]
    internal static class Patch_Childcare_RegisterLaidEggs
    {
        public static bool Prepare() => ChildcareDefenseUtility.IsEggProtectionEnabled;

        public static void Postfix(CompEggLayer __instance)
        {
            if (!ChildcareDefenseUtility.IsEggProtectionEnabled)
            {
                return;
            }

            try
            {
                if (!(__instance?.parent is Pawn mother))
                {
                    return;
                }

                ChildcareDefenseUtility.RegisterNearbyLaidEggsForMother(mother);
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: Patch_Childcare_RegisterLaidEggs Postfix exception: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.SplitOff))]
    internal static class Patch_Childcare_EggOwnershipOnSplit
    {
        public static bool Prepare() => ChildcareDefenseUtility.IsEggProtectionEnabled;

        public static void Postfix(ThingWithComps __instance, int count, Thing __result)
        {
            if (__result == null || ReferenceEquals(__result, __instance))
            {
                return;
            }

            try
            {
                ChildcareDefenseUtility.HandleEggSplit(__instance, __result);
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: Patch_Childcare_EggOwnershipOnSplit Postfix exception: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.TryAbsorbStack))]
    internal static class Patch_Childcare_EggOwnershipOnAbsorb
    {
        public static bool Prepare() => ChildcareDefenseUtility.IsEggProtectionEnabled;

        public static void Prefix(ThingWithComps __instance, Thing other, bool respectStackLimit)
        {
            if (__instance == null || other == null || !__instance.CanStackWith(other))
            {
                return;
            }

            try
            {
                int countToTake = ThingUtility.TryAbsorbStackNumToTake(__instance, other, respectStackLimit);
                if (countToTake > 0)
                {
                    ChildcareDefenseUtility.HandleEggAbsorb(__instance, other, countToTake);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: Patch_Childcare_EggOwnershipOnAbsorb Prefix exception: {ex}");
            }
        }
    }
}
