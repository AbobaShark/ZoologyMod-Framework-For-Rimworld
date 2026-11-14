using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public enum OffspringShelterMode
    {
        NONE, POUCH, DEN, GROUND_NEST_ALTRICIAL, TREE_NEST_ALTRICIAL,
        GROUND_NEST_PRECOCIAL, TREE_NEST_PRECOCIAL, AQUATIC_CLUTCH
    }

    public class ModExtension_OffspringShelter : DefModExtension
    {
        public OffspringShelterMode mode;
    }

    public static class AnimalOffspringShelter
    {
        // Read the configured type separately: the startup Comp installer must
        // include all eligible species so toggles can be changed without a reload.
        internal static OffspringShelterMode ConfiguredModeFor(ThingDef def) =>
            DefModExtensionCache<ModExtension_OffspringShelter>.Get(def)?.mode
                ?? OffspringShelterMode.NONE;

        public static OffspringShelterMode For(ThingDef def)
        {
            OffspringShelterMode mode = ConfiguredModeFor(def);
            if (mode == OffspringShelterMode.NONE || !ModConstants.AdvancedReproduction)
                return OffspringShelterMode.NONE;
            return mode == OffspringShelterMode.DEN && !ModConstants.AdvancedThreatAvoidance
                ? OffspringShelterMode.NONE : mode;
        }

        public static bool IsNest(OffspringShelterMode mode) =>
            mode == OffspringShelterMode.GROUND_NEST_ALTRICIAL
            || mode == OffspringShelterMode.TREE_NEST_ALTRICIAL
            || mode == OffspringShelterMode.GROUND_NEST_PRECOCIAL
            || mode == OffspringShelterMode.TREE_NEST_PRECOCIAL;

        public static bool KeepsBaby(OffspringShelterMode mode) =>
            mode == OffspringShelterMode.DEN
            || mode == OffspringShelterMode.GROUND_NEST_ALTRICIAL
            || mode == OffspringShelterMode.TREE_NEST_ALTRICIAL;

        public static bool IsTreeNest(OffspringShelterMode mode) =>
            mode == OffspringShelterMode.TREE_NEST_ALTRICIAL
            || mode == OffspringShelterMode.TREE_NEST_PRECOCIAL;
    }

    // Unlike a normal ThingOwner attached to a spawned holder, the pouch must
    // never invoke Pawn.DoTick: sleeping, food, health and age are advanced by
    // the same limited physiology method used by the barn and animal burrows.
    public sealed class CompAnimalPouch : ThingComp, IThingHolderTickable
    {
        private ThingOwner<Thing> young;
        private int lastUpdate = int.MinValue;

        // The owner is set when constructed; use the proper IThingHolder
        // constructor rather than setting it after deserialization.
        public CompAnimalPouch()
        {
            young = new ThingOwner<Thing>(this, false, LookMode.Deep);
        }

        public bool ShouldTickContents => false;
        public ThingOwner GetDirectlyHeldThings() => young;

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, young);
        }

        public bool Contains(Pawn baby) => baby != null && young.Contains(baby);
        public int YoungCount => young.Count;
        public Pawn YoungAt(int index) => index >= 0 && index < young.Count ? young[index] as Pawn : null;

        // Vanilla's contained-item selection control also works with a Pawn
        // as the outer holder; no duplicate baby is placed in inventory.
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;
            for (int i = 0; i < young.Count; i++)
                if (young[i] is Pawn baby)
                {
                    Gizmo gizmo = Building.SelectContainedItemGizmo(parent, baby);
                    if (gizmo != null) yield return gizmo;
                }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref young, "zoologyPouchYoung", this);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                young ??= new ThingOwner<Thing>(this, false, LookMode.Deep);
        }

        public bool TryStoreBaby(Pawn child)
        {
            Pawn mother = parent as Pawn;
            if (mother == null || mother.Dead || mother.Destroyed || child == null
                || child.Dead || child.Destroyed || Contains(child)
                || AnimalOffspringShelter.For(mother.def) != OffspringShelterMode.POUCH
                || !AnimalLifeStageUtility.IsAnimalInfantLifeStage(child.ageTracker?.CurLifeStage)
                || child.def != mother.def
                || !ChildcareUtility.TryGetKnownBiologicalMother(child, out Pawn knownMother)
                || knownMother != mother)
                return false;

            // Births can happen inside a barn or burrow. The newborn and its
            // mother may then share a holder, rather than being spawned.
            // Only transfer out of that exact shared holder.
            ThingOwner originalOwner = child.holdingOwner;
            if (originalOwner != null && originalOwner != mother.holdingOwner)
                return false;
            Map map = child.MapHeld;
            IntVec3 pos = child.PositionHeld;
            bool wasSpawned = child.Spawned;
            if (wasSpawned)
            {
                if (mother.MapHeld != map)
                    return false;
                child.DeSpawn();
            }
            else if (originalOwner != null)
                originalOwner.Remove(child);

            if (young.TryAdd(child, false))
            {
                child.Drawer?.renderer?.SetAnimation(null);
                return true;
            }

            // Preserve the child if a transfer fails.
            if (originalOwner != null && child.holdingOwner == null)
                originalOwner.TryAdd(child);
            else if (wasSpawned && !child.Spawned && child.holdingOwner == null && map != null)
                GenSpawn.Spawn(child, pos, map);
            return false;
        }

        // Invoked by CompTick on a spawned mother, and explicitly by
        // AnimalGameApi on a mother held inside a barn/burrow/den. Do not
        // invoke from the offspring's own Pawn.Tick.
        public override void CompTick() => TickContainedPhysiology(1);

        internal void TickContainedPhysiology() => TickContainedPhysiology(1);

        internal void TickContainedPhysiology(int elapsedTicks)
        {
            if (!(parent is Pawn mother) || young.Count == 0)
                return;

            // The component may already contain young when the setting changes.
            // Eject on the existing 250-tick interval; if no safe drop is
            // possible, continue their physiology instead of losing them.
            int tick = GenTicks.TicksGame;
            if ((tick + mother.thingIDNumber) % 250 == 0
                && !ModConstants.AdvancedReproduction)
            {
                ReleaseAll(mother.MapHeld);
                if (young.Count == 0) return;
            }
            if (lastUpdate == tick) return;
            // A caravan updates its needs in intervals. Advance exactly the
            // elapsed number of physiology ticks without running Pawn AI.
            int steps = Math.Max(1, elapsedTicks);
            if (lastUpdate > 0)
                steps = Math.Min(steps, Math.Max(0, tick - lastUpdate));
            if (steps == 0) return;
            lastUpdate = tick;

            for (int i = young.Count - 1; i >= 0; i--)
            {
                if (i >= young.Count || !(young[i] is Pawn baby))
                    continue;
                if (baby.Dead || baby.Destroyed)
                {
                    Eject(baby, mother.MapHeld, mother.PositionHeld);
                    continue;
                }

                if (!AnimalLifeStageUtility.IsAnimalInfantLifeStage(baby.ageTracker?.CurLifeStage))
                {
                    Eject(baby, mother.MapHeld, mother.PositionHeld);
                    continue;
                }

                for (int step = 0; step < steps && !baby.Dead && Contains(baby); step++)
                    AnimalGameApi.TickContainedPhysiology(baby);
                if (!Contains(baby) || baby.Dead)
                    continue;

                if (!AnimalLifeStageUtility.IsAnimalInfantLifeStage(baby.ageTracker?.CurLifeStage))
                {
                    Eject(baby, mother.MapHeld, mother.PositionHeld);
                    continue;
                }

                // Same maternal nutrition-transfer calculation as ordinary
                // nursing. Only the access condition differs.
                if (steps > 1 || tick % 250 == (baby.thingIDNumber % 250))
                    AnimalLactationUtility.SuckleFromLactatingPawn(
                        baby, mother, steps > 1 ? steps : 250);
            }
        }

        private void Eject(Pawn baby, Map map, IntVec3 at)
        {
            if (baby == null || !young.Contains(baby))
                return;

            if (map != null && at.IsValid
                && young.TryDrop(baby, at, map, ThingPlaceMode.Near, out _))
                return;

            // A mother may die in a world caravan without a map. Preserve
            // the live baby as a caravan pawn rather than discarding it.
            if (baby.Dead || baby.Destroyed)
                return;
            if (parent is Pawn mother && mother.GetCaravan() is Caravan caravan)
            {
                young.Remove(baby);
                caravan.AddPawn(baby, true);
                Find.WorldPawns.PassToWorld(baby, PawnDiscardDecideMode.KeepForever);
            }
        }

        internal static void ReleaseDisabledCaravanPouches()
        {
            // Called only on game load or a settings transition, never in a
            // hot path. Caravan mothers do not receive CompTick once the
            // associated Harmony hooks have been switched off.
            // Mod initialization runs at the main menu, before Game/World
            // exist. Find.WorldObjects dereferences Current.Game.World.
            // Inspect it only after an actual game world has been created.
            if (ModConstants.AdvancedReproduction || Current.Game?.World == null)
                return;
            List<Caravan> caravans = Find.WorldObjects?.Caravans;
            if (caravans == null) return;
            for (int caravanIndex = 0; caravanIndex < caravans.Count; caravanIndex++)
            {
                List<Pawn> pawns = caravans[caravanIndex]?.PawnsListForReading;
                if (pawns == null) continue;
                // Adding ejected babies to the caravan may extend its list.
                int initialCount = pawns.Count;
                for (int i = 0; i < initialCount; i++)
                    pawns[i]?.TryGetComp<CompAnimalPouch>()?.ReleaseAll(null);
            }
        }

        private void ReleaseAll(Map map)
        {
            Pawn mother = parent as Pawn;
            IntVec3 at = mother?.PositionHeld ?? IntVec3.Invalid;
            for (int i = young.Count - 1; i >= 0; i--)
            {
                if (!(young[i] is Pawn baby))
                    continue;
                Eject(baby, map, at);
            }
        }

        public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null)
        {
            ReleaseAll(prevMap);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            ReleaseAll(previousMap);
            // Even if every cell is blocked, never destroy a live baby with
            // its parent. A world pawn is the last-resort surviving owner.
            for (int i = young.Count - 1; i >= 0; i--)
                if (young[i] is Pawn baby && !baby.Dead && !baby.Destroyed)
                {
                    young.Remove(baby);
                    Find.WorldPawns.PassToWorld(baby, PawnDiscardDecideMode.KeepForever);
                }
            base.PostDestroy(mode, previousMap);
        }
    }

    // Visible on a selected pouch mother, like the familiar Cargo/Gear tab.
    // The babies remain inside their actual ThingOwner, so their health, age
    // and stats in the info card are those of the live Pawn.
    public sealed class ITab_AnimalPouch : ITab
    {
        public ITab_AnimalPouch()
        {
            size = new Vector2(420f, 300f);
            labelKey = "Zoology_PouchTab";
        }

        public override bool IsVisible =>
            SelPawn?.gender == Gender.Female
            && SelPawn.TryGetComp<CompAnimalPouch>() != null;

        protected override void FillTab()
        {
            CompAnimalPouch pouch = SelPawn?.TryGetComp<CompAnimalPouch>();
            if (pouch == null) return;

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(16f, 22f, 350f, 28f),
                "Zoology_PouchYoungCount".Translate(pouch.YoungCount).ToString());
            for (int i = 0; i < pouch.YoungCount && i < 8; i++)
            {
                Pawn child = pouch.YoungAt(i);
                if (child == null) continue;
                float y = 56f + i * 29f;
                if (Widgets.ButtonText(new Rect(16f, y, 335f, 27f), child.LabelCap))
                    Find.Selector.Select(child);
                Widgets.InfoCardButton(373f, y + 2f, child);
            }
        }
    }

    [StaticConstructorOnStartup]
    internal static class AnimalPouchBootstrap
    {
        static AnimalPouchBootstrap()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.race?.Animal != true
                    || AnimalOffspringShelter.ConfiguredModeFor(def) != OffspringShelterMode.POUCH)
                    continue;
                def.comps ??= new List<CompProperties>();
                if (!def.comps.Exists(c => c.compClass == typeof(CompAnimalPouch)))
                    def.comps.Add(new CompProperties(typeof(CompAnimalPouch)));

                // At this point Def resolution may already have created the
                // shared inspector tab instances, so update both lists.
                Type tabType = typeof(ITab_AnimalPouch);
                def.inspectorTabs ??= new List<Type>();
                if (!def.inspectorTabs.Contains(tabType))
                    def.inspectorTabs.Add(tabType);
                def.inspectorTabsResolved ??= new List<InspectTabBase>();
                InspectTabBase tab = InspectTabManager.GetSharedInstance(tabType);
                if (!def.inspectorTabsResolved.Contains(tab))
                    def.inspectorTabsResolved.Add(tab);
            }
        }
    }

    // Invoked only when a pouch mother makes an idle decision, using her
    // biological children; never a map-wide list.
    public sealed class JobGiver_ReturnPouchBaby : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!ModConstants.AdvancedReproduction) return null;
            if (pawn?.Spawned != true || pawn.Dead || pawn.Downed || pawn.InMentalState
                || pawn.gender != Gender.Female || pawn.relations == null
                || pawn.TryGetComp<CompAnimalPouch>() is not CompAnimalPouch pouch)
                return null;

            foreach (Pawn baby in pawn.relations.Children)
            {
                if (baby == null || baby.Dead || baby.Destroyed || !baby.Spawned
                    || baby.Map != pawn.Map || baby.def != pawn.def
                    || pouch.Contains(baby)
                    || !AnimalLifeStageUtility.IsAnimalInfantLifeStage(baby.ageTracker?.CurLifeStage))
                    continue;
                if (pawn.CanReserveAndReach(baby, PathEndMode.Touch, Danger.Some))
                    return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_ReturnPouchBaby"), baby);
            }
            return null;
        }
    }

    public sealed class JobDriver_ReturnPouchBaby : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() =>
                pawn.TryGetComp<CompAnimalPouch>()?.TryStoreBaby(TargetA.Pawn));
        }
    }

    // Caravan pawns are not spawned, so their comps do not receive CompTick.
    // The existing caravan needs interval is the only alternative entrypoint.
    [HarmonyLib.HarmonyPatch(typeof(Caravan_NeedsTracker),
        nameof(Caravan_NeedsTracker.TrySatisfyPawnsNeeds))]
    internal static class Patch_Caravan_PouchPhysiology
    {
        private static void Postfix(Caravan_NeedsTracker __instance, int delta)
        {
            var pawns = __instance?.caravan?.PawnsListForReading;
            if (pawns == null) return;
            for (int i = 0; i < pawns.Count; i++)
                pawns[i]?.TryGetComp<CompAnimalPouch>()?.TickContainedPhysiology(delta);
        }
    }
}
