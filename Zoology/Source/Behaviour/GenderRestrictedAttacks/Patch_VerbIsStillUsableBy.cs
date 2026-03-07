

using System;
using System.Reflection;
using Verse;

namespace ZoologyMod
{
    
    [HarmonyLib.HarmonyPatch(typeof(Verb), "IsStillUsableBy")]
    internal static class Patch_VerbIsStillUsableBy
    {
        internal static bool Prepare()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnableGenderRestrictedAttacks;
        }

        
        internal static void Postfix(Verb __instance, ref bool __result, Pawn pawn)
        {
            try
            {
                if (!__result) return; 

                var tool = __instance.tool;
                if (tool == null) return;

                
                var asOur = tool as ToolWithGender;
                if (asOur != null)
                {
                    __result = (asOur.restrictedGender == Gender.None) || (asOur.restrictedGender == pawn.gender);
                    return;
                }

                
                
                var t = tool.GetType();
                var fld = t.GetField("restrictedGender", BindingFlags.Public | BindingFlags.Instance);
                if (fld != null && fld.FieldType == typeof(Gender))
                {
                    var val = (Gender)fld.GetValue(tool);
                    if (val != Gender.None)
                    {
                        __result = (val == pawn.gender);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Error in Patch_VerbIsStillUsableBy.Postfix: {ex}");
                
            }
        }
    }
}
