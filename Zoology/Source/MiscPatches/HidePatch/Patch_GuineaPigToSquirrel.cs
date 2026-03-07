using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using RimWorld;

namespace ZoologyMod
{
    [StaticConstructorOnStartup]
    public static class Patch_GuineaPigToSquirrel
    {
        static Patch_GuineaPigToSquirrel()
        {
            try
            {
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    ReplaceGuineaPigLeather();
                    Log.Message("[Zoology] Replaced GuineaPig leather with Squirrel leather in all ThingDefs.");
                });
            }
            catch (Exception ex)
            {
                Log.Error("[Zoology] Patch_GuineaPigToSquirrel init failed: " + ex);
            }
        }

        private static void ReplaceGuineaPigLeather()
        {
            ThingDef from = DefDatabase<ThingDef>.GetNamedSilentFail("Leather_GuineaPig");
            ThingDef to = DefDatabase<ThingDef>.GetNamedSilentFail("Leather_Squirrel");

            if (from == null || to == null)
                return;

            foreach (ThingDef td in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                
                if (td.race != null && td.race.leatherDef == from)
                    td.race.leatherDef = to;

                
                FieldInfo[] fields = td.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (FieldInfo fi in fields)
                {
                    try
                    {
                        object val = fi.GetValue(td);
                        if (val == null) continue;

                        if (val is ThingDef tdField && tdField == from)
                            fi.SetValue(td, to);

                        else if (val is IList list)
                        {
                            for (int i = 0; i < list.Count; i++)
                                if (list[i] is ThingDef item && item == from)
                                    list[i] = to;
                        }
                    }
                    catch { }
                }
            }
        }
    }
}
