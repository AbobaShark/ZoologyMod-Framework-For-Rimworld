using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch]
    public static class Patch_Childcare_DefendYoung
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            MethodBase thingMethod = AccessTools.DeclaredMethod(typeof(Thing), "TakeDamage", new[] { typeof(DamageInfo) });
            if (thingMethod != null)
            {
                yield return thingMethod;
            }

            MethodBase pawnMethod = AccessTools.DeclaredMethod(typeof(Pawn), "TakeDamage", new[] { typeof(DamageInfo) });
            if (pawnMethod != null && !ReferenceEquals(pawnMethod, thingMethod))
            {
                yield return pawnMethod;
            }

            if (thingMethod == null && pawnMethod == null)
            {
                MethodBase fallback = AccessTools.Method(typeof(Thing), "TakeDamage", new[] { typeof(DamageInfo) });
                if (fallback != null)
                {
                    yield return fallback;
                }
                else
                {
                    Log.Error("[Zoology] Childcare: could not find TakeDamage methods to patch.");
                }
            }
        }

        public static bool Prepare()
        {
            return ChildcareDefenseUtility.IsYoungProtectionEnabled;
        }

        public static void Postfix(Thing __instance, DamageInfo dinfo)
        {
            try
            {
                if (!ChildcareDefenseUtility.IsYoungProtectionEnabled) return;

                Pawn child = __instance as Pawn;
                if (child == null) return;
                if (!child.Spawned || child.Dead || child.Destroyed) return;
                if (!child.IsAnimal) return;

                if (!ChildcareUtility.IsAnimalChild(child)) return;

                Pawn attacker = dinfo.Instigator as Pawn;
                if (attacker == null) return;
                if (attacker == child) return;
                if (!attacker.Spawned || attacker.Dead || attacker.Destroyed) return;

                if (!ChildcareUtility.TryGetBiologicalMother(child, out Pawn mother)) return;
                if (!ChildcareUtility.HasChildcareExtension(mother)) return;
                ChildcareDefenseUtility.TryOrderProtection(mother, attacker, child);
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: Patch_Childcare_DefendYoung Postfix exception: {ex}");
            }
        }
    }
}
