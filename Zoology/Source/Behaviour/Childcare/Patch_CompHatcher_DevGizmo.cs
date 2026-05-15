using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.GetGizmos))]
    internal static class Patch_CompHatcher_DevGizmo
    {
        private static readonly FieldInfo HatcherProgressField = AccessTools.Field(typeof(CompHatcher), "hatcherProgress");
        private static readonly MethodInfo HatchMethod = AccessTools.Method(typeof(CompHatcher), "Hatch");

        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, ThingWithComps __instance)
        {
            if (__result != null)
            {
                foreach (Gizmo gizmo in __result)
                {
                    yield return gizmo;
                }
            }

            if (!Prefs.DevMode || !DebugSettings.ShowDevGizmos || __instance == null || __instance.Destroyed)
            {
                yield break;
            }

            CompHatcher hatcher = __instance.TryGetComp<CompHatcher>();
            if (hatcher == null)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "Zoology_DevHatchNow_Label".Translate(),
                defaultDesc = "Zoology_DevHatchNow_Desc".Translate(),
                action = delegate
                {
                    TryForceHatch(hatcher);
                }
            };
        }

        private static void TryForceHatch(CompHatcher hatcher)
        {
            try
            {
                if (hatcher?.parent == null || hatcher.parent.Destroyed)
                {
                    return;
                }

                float? hatchTicks = TryGetFullHatchProgress(hatcher);
                if (hatchTicks.HasValue && HatcherProgressField != null)
                {
                    HatcherProgressField.SetValue(hatcher, hatchTicks.Value);
                }

                if (HatchMethod != null)
                {
                    HatchMethod.Invoke(hatcher, Array.Empty<object>());
                    return;
                }

                hatcher.CompTick();
                hatcher.CompTickRare();
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: failed to force hatch via dev gizmo: {ex}");
            }
        }

        private static float? TryGetFullHatchProgress(CompHatcher hatcher)
        {
            try
            {
                object props = hatcher?.props;
                if (props == null)
                {
                    return null;
                }

                FieldInfo daysField = AccessTools.Field(props.GetType(), "hatcherDaystoHatch");
                if (daysField != null && daysField.GetValue(props) is float days)
                {
                    return days * 60000f;
                }

                PropertyInfo daysProperty = AccessTools.Property(props.GetType(), "hatcherDaystoHatch");
                if (daysProperty != null && daysProperty.GetValue(props) is float propertyDays)
                {
                    return propertyDays * 60000f;
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
