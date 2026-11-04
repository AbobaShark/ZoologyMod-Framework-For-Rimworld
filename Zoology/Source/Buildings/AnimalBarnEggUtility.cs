#if RIMWORLD17
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    internal static class AnimalBarnEggUtility
    {
        internal static bool IsAdjacentEggBox(Building_AnimalBarn barn, Thing box)
        {
            return barn?.Spawned == true
                && box?.Spawned == true
                && box.Map == barn.Map
                && !box.IsForbidden(Faction.OfPlayer)
                && box.TryGetComp<CompEggContainer>() != null
                && barn.OccupiedRect().ExpandedBy(1).Contains(box.Position)
                && !barn.OccupiedRect().Contains(box.Position);
        }

        // The vanilla egg box has its own container, stack limit, hatch ticking,
        // spoilage and hauling rules. Use it rather than creating a parallel
        // simulation of its contents.
        public static void DepositLaidEgg(Building_AnimalBarn barn, Thing egg)
        {
            if (barn?.Spawned != true || egg == null || egg.Destroyed)
            {
                return;
            }

            if (!IsExplosiveHatchingEgg(egg))
            {
                List<Thing> boxes = barn.Map.listerThings.ThingsOfDef(ThingDefOf.EggBox);
                for (int i = 0; i < boxes.Count && egg != null && !egg.Destroyed; i++)
                {
                    Thing box = boxes[i];
                    if (!IsAdjacentEggBox(barn, box))
                    {
                        continue;
                    }

                    CompEggContainer container = box.TryGetComp<CompEggContainer>();
                    if (container == null || !container.Accepts(egg.def)
                        || container.Full
                        || container.ContainedThing != null
                            && !container.ContainedThing.CanStackWith(egg))
                    {
                        continue;
                    }

                    int remaining = container.Props.stackLimit - container.TotalStackCount;
                    if (remaining <= 0)
                    {
                        continue;
                    }

                    // A litter may lay more eggs than one box can hold.
                    // Split real Thing stacks, retaining hatcher parentage
                    // and other modded Comp data through SplitOff.
                    int transfer = Math.Min(remaining, egg.stackCount);
                    bool whole = transfer == egg.stackCount;
                    Thing portion = whole ? egg : egg.SplitOff(transfer);
                    if (container.innerContainer.TryAdd(portion, true))
                    {
                        if (whole)
                        {
                            return;
                        }
                    }
                    else if (whole)
                    {
                        break;
                    }
                    else if (!GenPlace.TryPlaceThing(portion, barn.InteractionCell,
                                 barn.Map, ThingPlaceMode.Near)
                        && !barn.TryHoldProducedEgg(portion))
                    {
                        Log.Warning("[Zoology] Could not place or retain split eggs beside barn " + barn);
                    }
                }
            }

            if (egg.Destroyed || egg.stackCount <= 0)
            {
                return;
            }

            // Fertilized eggs must receive the real world temperature and
            // hatching tick. A VEF exploding egg is kept outside egg boxes
            // because its hatcher performs a map-dependent explosion.
            if (egg.TryGetComp<CompHatcher>() == null
                && !IsExplosiveHatchingEgg(egg)
                && barn.TryStoreProducedEggAsFood(egg))
            {
                return;
            }

            if (!GenPlace.TryPlaceThing(egg, barn.InteractionCell, barn.Map, ThingPlaceMode.Near))
            {
                // Never silently discard an egg if the entrance is blocked.
                // This fallback only preserves it; hatching still requires
                // the owner to provide a valid map context.
                if (!barn.TryHoldProducedEgg(egg))
                {
                    Log.Warning("[Zoology] Could not place or hold eggs from " + barn);
                }
            }
        }

        public static bool IsExplosiveHatchingEgg(Thing egg)
        {
            if (egg is ThingWithComps withComps)
            {
                List<ThingComp> comps = withComps.AllComps;
                if (comps != null)
                {
                    for (int i = 0; i < comps.Count; i++)
                    {
                        if (comps[i].GetType().FullName
                            == "VEF.AnimalBehaviours.CompExplodingHatcher")
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
#endif
