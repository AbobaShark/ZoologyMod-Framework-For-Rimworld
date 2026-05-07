using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public class JobGiver_IncubateEggClutch : ThinkNode_JobGiver
    {
        private const int ScanBudgetPerTickPerMap = 6;

        private static readonly Dictionary<int, int> scansByMapId = new Dictionary<int, int>(4);
        private static int scansTick = int.MinValue;

        protected override Job TryGiveJob(Pawn pawn)
        {
            try
            {
                if (pawn?.Map == null || !TryConsumeScanBudget(pawn.Map))
                {
                    return null;
                }

                Thing egg = ChildcareDefenseUtility.TryFindEggIncubationTarget(pawn);
                if (egg == null)
                {
                    return null;
                }

                JobDef jobDef = ChildcareDefenseUtility.GetEggIncubationJobDef();
                return jobDef == null ? null : JobMaker.MakeJob(jobDef, egg);
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: JobGiver_IncubateEggClutch exception: {ex}");
                return null;
            }
        }

        private static bool TryConsumeScanBudget(Map map)
        {
            if (map == null)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (scansTick != currentTick)
            {
                scansTick = currentTick;
                scansByMapId.Clear();
            }

            int mapId = map.uniqueID;
            scansByMapId.TryGetValue(mapId, out int used);
            if (used >= ScanBudgetPerTickPerMap)
            {
                return false;
            }

            scansByMapId[mapId] = used + 1;
            return true;
        }
    }
}
