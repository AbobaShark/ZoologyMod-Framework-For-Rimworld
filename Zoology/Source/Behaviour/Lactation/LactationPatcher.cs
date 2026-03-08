using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using RimWorld;

namespace ZoologyMod
{
    [StaticConstructorOnStartup]
    public static class LactationPatcher
    {
        private static readonly HashSet<string> CandidateMethodNameSet = new HashSet<string>(new[] { "DoBirthSpawn", "GiveBirth", "PostBirth", "FinishBirth", "DoBirth" }, StringComparer.Ordinal);
        private static readonly string[] PregnancyTypeNameFragments = { "Pregnant", "Pregnancy", "BestialPregnancy", "BasePregnancy", "Birth" };
        private static readonly string[] CandidateMotherMemberNames = { "mother", "pawn", "parent", "motherPawn", "motherThing" };
        private static readonly MethodInfo PostfixVoid = typeof(LactationPatcher).GetMethod(nameof(BirthPostfix_Void), BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo PostfixWithResult = typeof(LactationPatcher).GetMethod(nameof(BirthPostfix_WithResult), BindingFlags.Static | BindingFlags.NonPublic);

        static LactationPatcher()
        {
            try
            {
                if (!ZoologyModSettings.EnableMammalLactation)
                {
                    return;
                }

                var harmony = new Harmony("com.abobashark.zoology.lactation");
                int patchedCount = 0;
                List<string> patchedNames = new List<string>();

                var assemblies = AppDomain.CurrentDomain.GetAssemblies();

                foreach (var asm in assemblies)
                {
                    Type[] types;
                    try
                    {
                        types = asm.GetTypes();
                    }
                    catch (ReflectionTypeLoadException rtle)
                    {
                        types = Array.FindAll(rtle.Types, t => t != null);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var t in types)
                    {
                        if (t == null) continue;
                        if (!TypeNameMatches(t.Name)) continue;

                        MethodInfo[] methods;
                        try
                        {
                            methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        }
                        catch
                        {
                            continue;
                        }

                        foreach (var m in methods)
                        {
                            if (m == null) continue;
                            if (!CandidateMethodNameSet.Contains(m.Name)) continue;

                            try
                            {
                                MethodInfo chosenPostfix = (m.ReturnType == typeof(void)) ? PostfixVoid : PostfixWithResult;
                                if (chosenPostfix == null)
                                {
                                    Log.Error("ZoologyMod: Could not find required postfix methods by reflection.");
                                    continue;
                                }

                                harmony.Patch(m, postfix: new HarmonyMethod(chosenPostfix));
                                patchedCount++;
                                string s = $"{t.FullName}.{m.Name} (asm {asm.GetName().Name}) {(m.IsStatic ? "[static]" : "[instance]")}";
                                patchedNames.Add(s);
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"ZoologyMod: Failed to patch {t.FullName}.{m.Name}: {ex}");
                            }
                        }
                    }
                }

                if (patchedCount == 0)
                {
                    Log.Warning("ZoologyMod: No birth-related methods found to patch (this may be fine). If you use a mod that implements birth differently, tell me its name and I'll add explicit patch.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("ZoologyMod: LactationPatcher initialization failed: " + ex);
            }
        }

        private static bool TypeNameMatches(string typeName)
        {
            for (int i = 0; i < PregnancyTypeNameFragments.Length; i++)
            {
                if (typeName.IndexOf(PregnancyTypeNameFragments[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        static void BirthPostfix_Void(object __instance, object[] __args)
        {
            try
            {
                ProcessBirth(__instance, null, __args);
            }
            catch (Exception ex)
            {
                Log.Error("ZoologyMod: BirthPostfix_Void error: " + ex);
            }
        }

        static void BirthPostfix_WithResult(object __instance, object __result, object[] __args)
        {
            try
            {
                ProcessBirth(__instance, __result, __args);
            }
            catch (Exception ex)
            {
                Log.Error("ZoologyMod: BirthPostfix_WithResult error: " + ex);
            }
        }

        private static void ProcessBirth(object __instance, object __result, object[] __args)
        {
            try
            {
                if (!ZoologyModSettings.EnableMammalLactation)
                {
                    return;
                }

                Pawn mother = null;
                if (__args != null)
                {
                    foreach (var a in __args)
                    {
                        if (a is Pawn p)
                        {
                            mother = p;
                            break;
                        }
                    }
                }

                if (mother == null)
                    mother = TryGetMotherFromInstanceOrResult(__instance, __result);

                if (mother == null)
                {
                    return;
                }

                if (!mother.IsMammal())
                {
                    return;
                }
                if (mother.gender != Gender.Female)
                {
                    return;
                }

                var lactDef = AnimalChildcareUtility.LactatingHediffDef;
                if (lactDef == null)
                {
                    Log.Warning("ZoologyMod: HediffDef 'Zoology_Lactating' not found.");
                    return;
                }
                if (mother.health.hediffSet.HasHediff(lactDef))
                {
                    return;
                }

                List<Pawn> pups = FindRecentPupsNearMother(mother);
                if (pups.Count > 0)
                {
                    AnimalChildcareUtility.OnAnimalGaveBirth(mother, pups);
                }
                else
                {
                    AnimalChildcareUtility.OnAnimalGaveBirth(mother, null);
                }
            }
            catch (Exception ex)
            {
                Log.Error("ZoologyMod: ProcessBirth error: " + ex);
            }
        }

        private static Pawn TryGetMotherFromInstanceOrResult(object instance, object result)
        {
            try
            {
                if (instance is Hediff h && h.pawn != null) return h.pawn;
                if (result is Pawn rp) return rp;
                if (result is IEnumerable<Pawn> rlist)
                {
                    foreach (var pawn in rlist)
                    {
                        if (pawn != null) return pawn;
                    }
                }

                var t = instance?.GetType();
                if (t != null)
                {
                    foreach (var name in CandidateMotherMemberNames)
                    {
                        try
                        {
                            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (f != null)
                            {
                                var val = f.GetValue(instance);
                                if (val is Pawn p) return p;
                            }
                            var pinfo = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (pinfo != null && pinfo.CanRead)
                            {
                                var val = pinfo.GetValue(instance);
                                if (val is Pawn p2) return p2;
                            }
                        }
                        catch { }
                    }

                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        try
                        {
                            var fv = f.GetValue(instance);
                            if (fv is Hediff hh && hh.pawn != null) return hh.pawn;
                            if (fv is Pawn pp) return pp;
                        }
                        catch { }
                    }
                    foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        try
                        {
                            if (!p.CanRead) continue;
                            var pv = p.GetValue(instance);
                            if (pv is Hediff hh2 && hh2.pawn != null) return hh2.pawn;
                            if (pv is Pawn pp2) return pp2;
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("ZoologyMod: Exception in TryGetMotherFromInstanceOrResult: " + ex);
            }
            return null;
        }

        private static List<Pawn> FindRecentPupsNearMother(Pawn mother)
        {
            var result = new List<Pawn>();
            try
            {
                if (mother.Map == null) return result;
                long ageThreshold = 10000L;
                foreach (var pawn in mother.Map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn == null || pawn == mother) continue;
                    if (pawn.def != mother.def) continue;
                    if (pawn.Dead) continue;
                    try
                    {
                        long age = pawn.ageTracker.AgeBiologicalTicks;
                        if (age <= ageThreshold)
                        {
                            float distF = (pawn.Position - mother.Position).LengthHorizontal;
                            int dist = (int)distF;
                            if (dist <= 30)
                            {
                                result.Add(pawn);
                                
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Log.Error("ZoologyMod: Exception in FindRecentPupsNearMother: " + ex);
            }
            return result;
        }
    }
}
