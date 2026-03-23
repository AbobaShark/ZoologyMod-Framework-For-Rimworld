using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    [HarmonyPatch]
    public static class Patch_Childcare_DefendYoung
    {
        private static JobDef protectYoungJobDef;

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
            var s = ZoologyModSettings.Instance;
            return s == null || (!s.DisableAllRuntimePatches && s.EnableAnimalChildcare);
        }

        public static void Postfix(Thing __instance, DamageInfo dinfo)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && (settings.DisableAllRuntimePatches || !settings.EnableAnimalChildcare)) return;

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
                if (!IsMotherEligibleForDefense(mother, child)) return;
                if (attacker == mother) return;

                if (IsSameFactionBlocked(mother, attacker)) return;
                if (IsAttackerTooStrong(attacker, mother)) return;
                if (IsMotherAcceptablePrey(attacker, mother)) return;

                if (!CanMotherEngage(mother, attacker)) return;

                TryOrderMotherDefense(mother, attacker, child);
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: Patch_Childcare_DefendYoung Postfix exception: {ex}");
            }
        }

        private static bool IsMotherEligibleForDefense(Pawn mother, Pawn child)
        {
            if (mother == null || child == null) return false;
            if (mother.Dead || mother.Destroyed || !mother.Spawned) return false;
            if (mother.Downed) return false;
            if (mother.InMentalState) return false;
            if (!PreyProtectionUtility.IsPawnAwakeForProtection(mother)) return false;
            if (mother.Map == null || child.Map == null || mother.Map != child.Map) return false;
            return true;
        }

        private static bool IsSameFactionBlocked(Pawn mother, Pawn attacker)
        {
            if (mother?.Faction == null || attacker?.Faction == null) return false;
            if (!ReferenceEquals(mother.Faction, attacker.Faction)) return false;

            var def = mother.Faction.def;
            if (def != null && def.defName != null
                && def.defName.Equals("Photonozoa", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static bool IsAttackerTooStrong(Pawn attacker, Pawn mother)
        {
            float attackerCp = attacker?.kindDef?.combatPower ?? 0f;
            float motherCp = mother?.kindDef?.combatPower ?? 0f;

            if (attackerCp <= 0f || motherCp <= 0f) return false;
            return attackerCp >= motherCp * 1.3f;
        }

        private static bool IsMotherAcceptablePrey(Pawn attacker, Pawn mother)
        {
            try
            {
                if (attacker == null || mother == null) return false;
                if (attacker.RaceProps?.predator != true) return false;
                return FoodUtility.IsAcceptablePreyFor(attacker, mother);
            }
            catch
            {
                return false;
            }
        }

        private static bool CanMotherEngage(Pawn mother, Pawn attacker)
        {
            if (mother == null || attacker == null) return false;

            var curJob = mother.CurJob;
            if (curJob != null)
            {
                if (curJob.playerForced) return false;
                if (curJob.def == JobDefOf.AttackMelee) return false;
                if (ProtectPreyState.IsProtectPreyJob(mother)) return false;
                if (ProtectYoungUtility.IsProtectYoungJob(mother)) return false;
            }

            try
            {
                if (!mother.CanReach(attacker, PathEndMode.Touch, Danger.Deadly)) return false;
            }
            catch
            {
            }

            return true;
        }

        private static void TryOrderMotherDefense(Pawn mother, Pawn attacker, Pawn child)
        {
            try
            {
                if (mother == null || attacker == null) return;
                var def = protectYoungJobDef ?? (protectYoungJobDef = DefDatabase<JobDef>.GetNamedSilentFail(ProtectYoungUtility.ProtectYoungDefName));
                Job job = def != null
                    ? JobMaker.MakeJob(def, attacker, child)
                    : JobMaker.MakeJob(JobDefOf.AttackMelee, attacker);

                mother.jobs?.TryTakeOrderedJob(job);
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: TryOrderMotherDefense failed: {ex}");
            }
        }
    }
}
