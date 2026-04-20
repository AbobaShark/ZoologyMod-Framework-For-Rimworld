using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(Job), nameof(Job.ExposeData))]
    internal static class Patch_Job_ExposeData_SuppressMissingThinkNodeWarning
    {
        private static readonly FieldInfo JobGiverKeyField = AccessTools.Field(typeof(Job), "jobGiverKey");

        private static void Prefix(Job __instance)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit || __instance == null || JobGiverKeyField == null)
            {
                return;
            }

            try
            {
                int jobGiverKey = (int)JobGiverKeyField.GetValue(__instance);
                if (jobGiverKey == -1)
                {
                    return;
                }

                ThinkTreeDef thinkTree = __instance.jobGiverThinkTree;
                if (thinkTree == null || !thinkTree.TryGetThinkNodeWithSaveKey(jobGiverKey, out _))
                {
                    JobGiverKeyField.SetValue(__instance, -1);
                    __instance.jobGiver = null;
                }
            }
            catch (Exception)
            {
                // Keep save loading resilient; vanilla will continue safely.
            }
        }
    }
}
