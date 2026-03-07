

using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    
    
    
    public class ModExtension_FleeFromCarrier : DefModExtension
    {
        
        public float fleeRadius = 18f;

        
        
        public float fleeBodySizeLimit = 0f;

        
        public int? fleeDistance = 24;
    }

    public static class FleeFromCarrierUtil
    {
        private static ModExtension_FleeFromCarrier GetExtension(Pawn pawn)
        {
            return pawn?.def?.GetModExtension<ModExtension_FleeFromCarrier>();
        }

        
        public static bool IsCarrier(Pawn pawn)
        {
            if (pawn == null) return false;
            return GetExtension(pawn) != null;
        }

        public static float GetFleeRadius(Pawn carrier)
        {
            if (carrier == null) return 0f;
            var ext = GetExtension(carrier);
            if (ext != null) return ext.fleeRadius;
            return 18f; 
        }

        public static float GetFleeBodySizeLimit(Pawn carrier)
        {
            if (carrier == null) return 0f;
            var ext = GetExtension(carrier);
            if (ext != null) return ext.fleeBodySizeLimit;
            return 0f; 
        }

        public static int GetFleeDistance(Pawn carrier)
        {
            if (carrier == null) return 24;
            var ext = GetExtension(carrier);
            if (ext != null)
            {
                if (ext.fleeDistance.HasValue) return ext.fleeDistance.Value;
            }
            
            return 24;
        }
    }

    internal static class FleeFromCarrierMapCache
    {
        private const int RebuildIntervalTicks = 240;

        private sealed class Entry
        {
            public int lastBuildTick = int.MinValue;
            public readonly List<Pawn> carriers = new List<Pawn>(8);
        }

        private static readonly Dictionary<int, Entry> entriesByMapId = new Dictionary<int, Entry>();
        private static readonly List<Pawn> empty = new List<Pawn>(0);

        public static List<Pawn> GetCarriers(Map map)
        {
            if (map == null) return empty;

            int mapId = map.uniqueID;
            if (!entriesByMapId.TryGetValue(mapId, out var entry))
            {
                entry = new Entry();
                entriesByMapId[mapId] = entry;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            if (entry.lastBuildTick == int.MinValue || now - entry.lastBuildTick >= RebuildIntervalTicks)
            {
                RebuildForMap(map, entry, now);
            }

            return entry.carriers;
        }

        private static void RebuildForMap(Map map, Entry entry, int now)
        {
            entry.carriers.Clear();

            var pawns = map?.mapPawns?.AllPawnsSpawned;
            if (pawns != null)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    var p = pawns[i];
                    if (p == null || p.Dead || p.Downed) continue;
                    if (!FleeFromCarrierUtil.IsCarrier(p)) continue;
                    entry.carriers.Add(p);
                }
            }

            entry.lastBuildTick = now;
        }
    }

    
    
    
    [HarmonyPatch(typeof(JobGiver_AnimalFlee), "TryGiveJob")]
    public static class Patch_JobGiver_AnimalFlee_TryGiveJob_FleeFromCarrier
    {
        public static bool Prepare()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnableFleeFromCarrier;
        }

        public static void Postfix(JobGiver_AnimalFlee __instance, Pawn pawn, ref Job __result)
        {
            try
            {
                var s = ZoologyModSettings.Instance;
                if (s != null && !s.EnableFleeFromCarrier) return;

                if (pawn == null || pawn.Map == null) return;
                if (__result != null) return;
                if (!pawn.RaceProps.Animal) return;
                if (pawn.Dead || pawn.Downed) return;

                if ((s == null || s.EnableNoFleeExtension) && NoFleeUtil.IsNoFlee(pawn, out var noFleeExt))
                {
                    if (noFleeExt?.verboseLogging == true && Prefs.DevMode)
                        Log.Message($"[Zoology] Suppressed Carrier-induced flee for {pawn.LabelShort} due to ModExtension_NoFlee.");
                    return;
                }

                
                
                
                if (FleeFromCarrierUtil.IsCarrier(pawn)) return;

                var carriers = FleeFromCarrierMapCache.GetCarriers(pawn.Map);
                if (carriers == null || carriers.Count == 0) return;

                Pawn threat = null;
                float bestDistSq = float.MaxValue;

                for (int i = 0; i < carriers.Count; i++)
                {
                    var carrier = carriers[i];
                    if (carrier == null || carrier == pawn) continue;
                    if (!carrier.Spawned || carrier.Map != pawn.Map) continue;
                    if (carrier.Dead || carrier.Downed) continue;

                    float realRadius = FleeFromCarrierUtil.GetFleeRadius(carrier);
                    if (realRadius <= 0f) continue;

                    float distSq = (carrier.Position - pawn.Position).LengthHorizontalSquared;
                    float radiusSq = realRadius * realRadius;
                    if (distSq > radiusSq) continue;

                    float bodySizeLimit = FleeFromCarrierUtil.GetFleeBodySizeLimit(carrier);
                    if (bodySizeLimit > 0f && pawn.BodySize > bodySizeLimit) continue;

                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        threat = carrier;
                    }
                }

                if (threat == null) return;

                
                if (!FleeUtility.ShouldAnimalFleeDanger(pawn)) return;

                
                int fleeDistance = FleeFromCarrierUtil.GetFleeDistance(threat);
                __result = FleeUtility.FleeJob(pawn, threat, fleeDistance);
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] Patch_JobGiver_AnimalFlee_TryGiveJob_FleeFromCarrier error: {e}");
            }
        }
    }
}
