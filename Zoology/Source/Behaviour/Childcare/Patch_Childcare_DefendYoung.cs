using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch]
    public static class Patch_Childcare_DefendYoung
    {
        static MethodBase TargetMethod()
        {
            var method = AccessTools.Method(typeof(Pawn), "TakeDamage", new Type[] { typeof(DamageInfo) });
            if (method != null)
            {
                return method;
            }

            method = AccessTools.Method(typeof(Thing), "TakeDamage", new Type[] { typeof(DamageInfo) });
            if (method == null)
            {
                Log.Error("[Zoology] Childcare: could not find TakeDamage method to patch.");
            }
            return method;
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
                if (!ChildcareUtility.HasChildcareExtension(child)) return;

                Pawn attacker = dinfo.Instigator as Pawn;
                if (attacker == null) return;
                if (attacker == child) return;
                if (!attacker.Spawned || attacker.Dead || attacker.Destroyed) return;

                if (!ChildcareUtility.TryGetBiologicalMother(child, out Pawn mother)) return;
                ChildcareDefenseUtility.TryOrderProtection(mother, attacker, child);
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: Patch_Childcare_DefendYoung Postfix exception: {ex}");
            }
        }
    }
}
