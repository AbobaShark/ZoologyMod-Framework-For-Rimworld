using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace ZoologyMod
{
    
    
    
    
    
    public static class ScavengerEatingContext
    {
        [ThreadStatic] private static Dictionary<Pawn, Thing> pawnToTarget;
        [ThreadStatic] private static List<Pawn> tempPawnsToRemove;

        private static Dictionary<Pawn, Thing> Map => pawnToTarget ?? (pawnToTarget = new Dictionary<Pawn, Thing>());
        private static List<Pawn> TempPawnsToRemove => tempPawnsToRemove ?? (tempPawnsToRemove = new List<Pawn>(16));

        
        
        
        
        public static void SetEating(Pawn pawn, Thing target)
        {
            try
            {
                if (pawn == null) return;
                if (pawn.def == null || !ZoologyCacheUtility.HasScavengerExtension(pawn.def)) return;

                var dict = Map;
                dict[pawn] = target;
            }
            catch (Exception e)
            {
                Debug.LogError("[Zoology] Error in ScavengerEatingContext.SetEating: " + e);
            }
        }

        
        
        
        public static void Clear(Pawn pawn)
        {
            try
            {
                if (pawn == null) return;
                var dict = Map;
                if (dict.Remove(pawn))
                {
                    
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Zoology] Error in ScavengerEatingContext.Clear: " + e);
            }
        }

        
        
        
        
        
        public static Pawn GetEatingPawnForCorpse(Corpse corpse)
        {
            try
            {
                if (corpse == null) return null;
                var dict = Map;
                var toRemove = TempPawnsToRemove;
                toRemove.Clear();

                foreach (var kv in dict)
                {
                    var p = kv.Key;
                    var t = kv.Value;

                    
                    if (p == null || p.Dead)
                    {
                        toRemove.Add(p);
                        continue;
                    }

                    if (t == null)
                    {
                        toRemove.Add(p);
                        continue;
                    }

                    try
                    {
                        var cj = p.CurJob;
                        if (cj == null || cj.def != JobDefOf.Ingest)
                        {
                            toRemove.Add(p);
                            continue;
                        }
                        var curTarget = cj.targetA.Thing;
                        if (curTarget == null || curTarget != t)
                        {
                            toRemove.Add(p);
                            continue;
                        }

                        if (t == corpse) return p;
                    }
                    catch
                    {
                        toRemove.Add(p);
                        continue;
                    }
                }

                
                foreach (var rp in toRemove)
                {
                    try { dict.Remove(rp); } catch { }
                }
                toRemove.Clear();
            }
            catch (Exception e)
            {
                Debug.LogError("[Zoology] Error in ScavengerEatingContext.GetEatingPawnForCorpse: " + e);
            }
            return null;
        }

        private static string ShortPawn(Pawn p)
        {
            try { return $"{(p.LabelShort ?? p.ToString())}_id{p.thingIDNumber}"; } catch { return "pawn?"; }
        }

        private static string ShortThing(Thing t)
        {
            try { return $"{t.def?.defName ?? "thing"}_id{t.thingIDNumber}"; } catch { return "thing?"; }
        }
    }
}
