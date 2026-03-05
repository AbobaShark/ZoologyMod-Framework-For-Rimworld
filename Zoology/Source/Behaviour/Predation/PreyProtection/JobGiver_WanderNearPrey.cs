// JobGiver_WanderNearPrey.cs
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // Вставляется в Animal_PreMain (через XML). Работает как JobGiver_Wander, но
    // центр блужданий — позиция парного трупа/носителя, и учитывает PRESENCE_RADIUS.
    public class JobGiver_WanderNearPrey : JobGiver_Wander
    {
        public JobGiver_WanderNearPrey()
        {
            // Консервативные значения; ThinkNode/vanilla всё равно будет контролировать очередь.
            this.wanderRadius = 5f;
            this.ticksBetweenWandersRange = new IntRange(125, 200);
            this.locomotionUrgency = LocomotionUrgency.Walk;
            this.expiryInterval = -1;
        }

        protected override IntVec3 GetWanderRoot(Pawn pawn)
        {
            try
            {
                if (pawn == null || pawn.Map == null) return IntVec3.Invalid;

                var comp = PredatorPreyPairGameComponent.Instance;
                if (comp == null) return IntVec3.Invalid;

                // ищем активную пару (как в менеджере) — предпочитаем active список
                List<Corpse> corpses = null;
                try { corpses = comp.GetActivePairedCorpses(pawn); } catch { corpses = null; }

                Corpse targetCorpse = null;
                if (corpses != null && corpses.Count > 0)
                {
                    targetCorpse = corpses[0];
                }
                else
                {
                    try { targetCorpse = comp.GetPairedCorpse(pawn); } catch { targetCorpse = null; }
                }

                if (targetCorpse == null) return IntVec3.Invalid;

                // если corpse на карте — берём его позицию, иначе — позицию носителя
                Map corpseMap = targetCorpse.Map;
                IntVec3 pos;
                if (corpseMap != null)
                {
                    pos = targetCorpse.Position;
                }
                else
                {
                    var carrier = FindCarrierPawnForCorpse(targetCorpse);
                    if (carrier == null || carrier.Map == null) return IntVec3.Invalid;
                    pos = carrier.Position;
                    corpseMap = carrier.Map;
                }

                if (pawn.Map != corpseMap) return IntVec3.Invalid;

                // проверяем дистанцию: если вне PRESENCE_RADIUS — этот JobGiver не действует (менеджер должен отправить его наружу)
                float radius = GetPresenceRadius();
                if (!pawn.Position.InHorDistOf(pos, radius)) return IntVec3.Invalid;

                // если спит/лежит — не тревожим
                if (IsSleepingOrLyingDown(pawn)) return IntVec3.Invalid;

                // для стаи можно попытаться вернуть «херд-центр», но чтобы не ломать совместимость —
                // возвращаем позицию трупа: JobGiver_Wander (GetExactWanderDest) позаботится о радиусе.
                return pos;
            }
            catch { return IntVec3.Invalid; }
        }

        // Вспомогательные: как в менеджере
        private static Pawn FindCarrierPawnForCorpse(Corpse corpse)
        {
            try
            {
                if (corpse == null) return null;
                var maps = Find.Maps;
                for (int mi = 0; mi < maps.Count; mi++)
                {
                    var pawns = maps[mi].mapPawns.AllPawnsSpawned;
                    for (int pi = 0; pi < pawns.Count; pi++)
                    {
                        var p = pawns[pi];
                        if (p == null) continue;
                        try
                        {
                            if (p.carryTracker != null && p.carryTracker.CarriedThing != null && p.carryTracker.CarriedThing.thingIDNumber == corpse.thingIDNumber)
                                return p;
                            var inv = p.inventory?.innerContainer;
                            if (inv != null)
                            {
                                for (int j = 0; j < inv.Count; j++)
                                {
                                    var t = inv[j];
                                    if (t != null && t.thingIDNumber == corpse.thingIDNumber) return p;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return null;
        }

        private static float GetPresenceRadius()
        {
            return (ZoologyModSettings.Instance != null && ZoologyModSettings.Instance.EnablePredatorDefendCorpse)
                ? ZoologyModSettings.Instance.PreyProtectionRange
                : 20f;
        }

        private static bool IsSleepingOrLyingDown(Pawn pawn)
        {
            try
            {
                if (pawn == null) return false;
                if (!pawn.Awake()) return true;

                var curJob = pawn?.jobs?.curJob;
                if (curJob != null)
                {
                    var defName = curJob.def?.defName ?? "";
                    if (!string.IsNullOrEmpty(defName) && defName.IndexOf("LayDown", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;

                    var driver = pawn.jobs.curDriver;
                    if (driver != null)
                    {
                        var driverName = driver.GetType().Name ?? "";
                        if (!string.IsNullOrEmpty(driverName) && driverName.IndexOf("LayDown", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }

                return false;
            }
            catch { return false; }
        }
    }
}