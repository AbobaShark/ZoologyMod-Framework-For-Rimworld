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
        private static readonly Dictionary<ThingDef, ModExtension_FleeFromCarrier> extensionCache = new Dictionary<ThingDef, ModExtension_FleeFromCarrier>();

        public static ModExtension_FleeFromCarrier GetExtension(Pawn pawn)
        {
            ThingDef def = pawn?.def;
            if (def == null)
            {
                return null;
            }

            if (extensionCache.TryGetValue(def, out ModExtension_FleeFromCarrier cached))
            {
                return cached;
            }

            ModExtension_FleeFromCarrier extension = def.GetModExtension<ModExtension_FleeFromCarrier>();
            extensionCache[def] = extension;
            return extension;
        }

        public static bool TryGetExtension(Pawn pawn, out ModExtension_FleeFromCarrier extension)
        {
            extension = GetExtension(pawn);
            return extension != null;
        }

        public static bool SharesNonNullFaction(Pawn a, Pawn b)
        {
            if (a?.Faction == null || b?.Faction == null)
            {
                return false;
            }

            return ReferenceEquals(a.Faction, b.Faction);
        }

        public static bool HasLineOfSightOrReach(Pawn pawn, Pawn carrier)
        {
            if (pawn == null || carrier == null)
            {
                return false;
            }

            Map map = pawn.Map;
            if (map == null || map != carrier.Map)
            {
                return false;
            }

            if (pawn.Position == carrier.Position)
            {
                return true;
            }

            try
            {
                if (GenSight.LineOfSight(pawn.Position, carrier.Position, map))
                {
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                return carrier.CanReach(pawn, PathEndMode.Touch, Danger.Deadly);
            }
            catch
            {
                return false;
            }
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

        internal readonly struct CarrierEntry
        {
            public CarrierEntry(Pawn carrier, ModExtension_FleeFromCarrier extension)
            {
                Carrier = carrier;

                float radius = extension?.fleeRadius ?? 0f;
                RadiusSquared = radius > 0f ? radius * radius : 0f;
                BodySizeLimit = extension?.fleeBodySizeLimit ?? 0f;
                FleeDistance = extension?.fleeDistance ?? 24;
            }

            public Pawn Carrier { get; }
            public float RadiusSquared { get; }
            public float BodySizeLimit { get; }
            public int FleeDistance { get; }
        }

        private sealed class Entry
        {
            public int lastBuildTick = int.MinValue;
            public readonly List<CarrierEntry> carriers = new List<CarrierEntry>(8);
        }

        private static readonly Dictionary<int, Entry> entriesByMapId = new Dictionary<int, Entry>();
        private static readonly List<CarrierEntry> empty = new List<CarrierEntry>(0);

        public static List<CarrierEntry> GetCarriers(Map map)
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

                    var extension = FleeFromCarrierUtil.GetExtension(p);
                    if (extension == null || extension.fleeRadius <= 0f) continue;

                    entry.carriers.Add(new CarrierEntry(p, extension));
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
                if (!FleeUtility.ShouldAnimalFleeDanger(pawn)) return;

                var carriers = FleeFromCarrierMapCache.GetCarriers(pawn.Map);
                if (carriers == null || carriers.Count == 0) return;

                IntVec3 pawnPos = pawn.Position;
                float pawnBodySize = pawn.BodySize;
                Pawn threat = null;
                int fleeDistance = 24;
                int bestDistSq = int.MaxValue;

                for (int i = 0; i < carriers.Count; i++)
                {
                    var carrierEntry = carriers[i];
                    var carrier = carrierEntry.Carrier;
                    if (carrier == null || carrier == pawn) continue;
                    if (!carrier.Spawned || carrier.Map != pawn.Map) continue;
                    if (carrier.Dead || carrier.Downed) continue;
                    if (FleeFromCarrierUtil.SharesNonNullFaction(pawn, carrier)) continue;

                    int distSq = (carrier.Position - pawnPos).LengthHorizontalSquared;
                    if (distSq > carrierEntry.RadiusSquared) continue;

                    float bodySizeLimit = carrierEntry.BodySizeLimit;
                    if (bodySizeLimit > 0f && pawnBodySize > bodySizeLimit) continue;
                    if (!FleeFromCarrierUtil.HasLineOfSightOrReach(pawn, carrier)) continue;

                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        threat = carrier;
                        fleeDistance = carrierEntry.FleeDistance;
                    }
                }

                if (threat == null) return;
                __result = FleeUtility.FleeJob(pawn, threat, fleeDistance);
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] Patch_JobGiver_AnimalFlee_TryGiveJob_FleeFromCarrier error: {e}");
            }
        }
    }
}
