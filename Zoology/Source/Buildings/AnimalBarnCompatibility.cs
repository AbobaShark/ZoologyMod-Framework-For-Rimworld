#if RIMWORLD17
using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Avoid a hard assembly dependency on Vanilla Expanded Framework or
    // Alpha Animals: both mods may be absent, and Alpha Animals uses many
    // animal comps supplied by the framework.
    internal static class AnimalBarnCompatibility
    {
        private const string ExplodingLayerName = "VEF.AnimalBehaviours.CompExplodingEggLayer";
        private const string AsexualReproductionName = "VEF.AnimalBehaviours.CompAsexualReproduction";
        private const string AsexualHediffName = "VEF.AnimalBehaviours.HediffComp_AsexualReproduction";

        private sealed class ExplodingEggMethods
        {
            public PropertyInfo CanLayNow;
            public MethodInfo ProduceEgg;
        }

        private static readonly Dictionary<Type, ExplodingEggMethods> explodingEggMethods
            = new Dictionary<Type, ExplodingEggMethods>();

        public static void TickAdditionalEggLayers(Building_AnimalBarn barn, Pawn animal, ThingComp comp)
        {
            if (comp?.GetType().FullName != ExplodingLayerName)
            {
                return;
            }

            // VEF's special egg layer contains its own progress and produce
            // methods, rather than inheriting RimWorld's CompEggLayer.
            comp.CompTick();
            Type type = comp.GetType();
            if (!explodingEggMethods.TryGetValue(type, out ExplodingEggMethods methods))
            {
                methods = new ExplodingEggMethods
                {
                    CanLayNow = type.GetProperty("CanLayNow"),
                    ProduceEgg = type.GetMethod("ProduceEgg", Type.EmptyTypes)
                };
                explodingEggMethods.Add(type, methods);
            }

            if (methods.CanLayNow == null || methods.ProduceEgg == null
                || !(methods.CanLayNow.GetValue(comp) is bool ready) || !ready)
            {
                return;
            }

            try
            {
                Thing egg = methods.ProduceEgg.Invoke(comp, null) as Thing;
                AnimalBarnEggUtility.DepositLaidEgg(barn, egg);
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Zoology] VEF egg laying failed in barn: " + exception,
                    animal.thingIDNumber ^ 0x45AA10);
            }
        }

        public static void TickMapDependentAsexualReproduction(Building_AnimalBarn barn)
        {
            if (barn?.Spawned != true)
            {
                return;
            }

            var due = new List<Pawn>();
            foreach (Pawn animal in barn.Animals)
            {
                bool needsMap = false;
                if (animal.AllComps != null)
                {
                    foreach (ThingComp comp in animal.AllComps)
                    {
                        if (comp.GetType().FullName == AsexualReproductionName)
                        {
                            needsMap = true;
                            break;
                        }
                    }
                }

                if (!needsMap && animal.health?.hediffSet?.hediffs != null)
                {
                    foreach (Hediff hediff in animal.health.hediffSet.hediffs)
                    {
                        if (!(hediff is HediffWithComps withComps) || withComps.comps == null)
                        {
                            continue;
                        }

                        foreach (HediffComp comp in withComps.comps)
                        {
                            if (comp.GetType().FullName == AsexualHediffName)
                            {
                                needsMap = true;
                                break;
                            }
                        }

                        if (needsMap)
                        {
                            break;
                        }
                    }
                }

                if (needsMap)
                {
                    due.Add(animal);
                }
            }

            for (int i = 0; i < due.Count; i++)
            {
                Pawn animal = due[i];
                if (!barn.ContainsAnimal(animal) || !barn.TryReleaseAnimal(animal))
                {
                    continue;
                }

                var previouslySpawned = new HashSet<Pawn>(barn.Map.mapPawns.AllPawnsSpawned);
                try
                {
                    // VEF requires Pawn.Map for both asexual reproduction
                    // implementations. Advance their original timers in a
                    // short real-map session, not by custom egg/pawn spawns.
                    if (animal.AllComps != null)
                    {
                        foreach (ThingComp comp in animal.AllComps)
                        {
                            if (comp.GetType().FullName == AsexualReproductionName)
                            {
                                comp.CompTickInterval(60000);
                            }
                        }
                    }

                    if (animal.health?.hediffSet?.hediffs != null)
                    {
                        // The original method may add/remove hediffs, so
                        // iterate a snapshot rather than the mutable list.
                        var hediffs = new List<Hediff>(animal.health.hediffSet.hediffs);
                        foreach (Hediff hediff in hediffs)
                        {
                            if (!(hediff is HediffWithComps withComps) || withComps.comps == null)
                            {
                                continue;
                            }

                            foreach (HediffComp comp in withComps.comps)
                            {
                                if (comp.GetType().FullName == AsexualHediffName)
                                {
                                    float severityAdjustment = 0f;
                                    comp.CompPostTickInterval(ref severityAdjustment, 60000);
                                }
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    Log.WarningOnce("[Zoology] VEF asexual reproduction failed for "
                        + animal + ": " + exception, animal.thingIDNumber ^ 0xA53A);
                }
                finally
                {
                    if (animal.Spawned && !animal.Dead)
                    {
                        // When VEF generates offspring, retain its parent
                        // outside with them to preserve family care.
                        bool newbornOutside = false;
                        IReadOnlyList<Pawn> spawned = barn.Map.mapPawns.AllPawnsSpawned;
                        for (int j = 0; j < spawned.Count; j++)
                        {
                            Pawn baby = spawned[j];
                            if (baby == null || previouslySpawned.Contains(baby)
                                || baby.RaceProps?.Animal != true
                                || baby.Faction != animal.Faction
                                || !baby.Position.InHorDistOf(barn.InteractionCell, 6f))
                            {
                                continue;
                            }

                            newbornOutside = true;
                            break;
                        }

                        if (!newbornOutside && barn.CanAcceptAnimal(animal))
                        {
                            barn.TryStoreAnimal(animal);
                        }
                    }
                }
            }
        }
    }
}
#endif
