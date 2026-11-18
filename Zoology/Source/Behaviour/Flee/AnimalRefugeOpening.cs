using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    [DefOf]
    public static class AnimalRefugeOpeningDefOf
    {
        public static DesignationDef Zoology_OpenRefuge;
        public static JobDef Zoology_OpenAnimalRefuge;

        static AnimalRefugeOpeningDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AnimalRefugeOpeningDefOf));
        }
    }

    [StaticConstructorOnStartup]
    internal static class AnimalRefugeOpening
    {
        private static readonly Texture2D OpenIcon = ContentFinder<Texture2D>.Get("UI/Designators/Open");

        internal static bool IsRefuge(Thing thing) =>
            thing is Building_AnimalBurrow || thing is Building_AnimalNursery;

        internal static bool CanDesignate(Thing thing) =>
            thing?.Spawned == true && (thing is Building_AnimalBurrow
                || (thing is Building_AnimalNursery nest && !nest.TreeNest));

        internal static Designation DesignationFor(Thing thing) =>
            thing?.Map?.designationManager.DesignationOn(thing, AnimalRefugeOpeningDefOf.Zoology_OpenRefuge);

        internal static Command_Toggle Command(Thing thing)
        {
            return new Command_Toggle
            {
                defaultLabel = "Zoology_OpenBurrow_Label".Translate(),
                defaultDesc = "Zoology_OpenBurrow_Desc".Translate(),
                icon = OpenIcon,
                isActive = () => DesignationFor(thing) != null,
                toggleAction = () =>
                {
                    if (!CanDesignate(thing)) return;
                    var manager = thing.Map.designationManager;
                    var designation = DesignationFor(thing);
                    if (designation != null) manager.RemoveDesignation(designation);
                    else manager.AddDesignation(new Designation(thing, AnimalRefugeOpeningDefOf.Zoology_OpenRefuge));
                }
            };
        }
    }

    [HarmonyPatch(typeof(Designator_Open), nameof(Designator_Open.CanDesignateThing))]
    internal static class Patch_RefugeVanillaOpen
    {
        private static bool Prefix(Thing __0, ref AcceptanceReport __result)
        {
            if (!AnimalRefugeOpening.IsRefuge(__0)) return true;
            __result = false;
            return false;
        }
    }

    public sealed class WorkGiver_OpenAnimalRefuge : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            foreach (Designation designation in pawn.Map.designationManager.SpawnedDesignationsOfDef(
                AnimalRefugeOpeningDefOf.Zoology_OpenRefuge))
                if (designation.target.Thing is Thing thing && AnimalRefugeOpening.CanDesignate(thing))
                    yield return thing;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return pawn.Faction == Faction.OfPlayer && pawn.RaceProps.Humanlike
                && AnimalRefugeOpening.CanDesignate(t)
                && AnimalRefugeOpening.DesignationFor(t) != null && !t.IsForbidden(pawn)
                && pawn.CanReserve(t, 1, -1, null, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            HasJobOnThing(pawn, t, forced)
                ? JobMaker.MakeJob(AnimalRefugeOpeningDefOf.Zoology_OpenAnimalRefuge, t) : null;
    }

    public sealed class JobDriver_OpenAnimalRefuge : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !AnimalRefugeOpening.CanDesignate(TargetA.Thing)
                || AnimalRefugeOpening.DesignationFor(TargetA.Thing) == null);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() => AnimalBurrowHunt.BeginOpening(pawn, TargetA.Thing));
            Toil opening = Toils_General.Wait(AnimalBurrowHunt.OpeningTicks(TargetA.Thing), TargetIndex.A)
                .WithProgressBarToilDelay(TargetIndex.A)
                .FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            opening.AddPreTickAction(() =>
            {
                if (pawn.IsHashIntervalTick(60))
                    AnimalBurrowHunt.BeginOpening(pawn, TargetA.Thing);
            });
            yield return opening;
            yield return Toils_General.Do(() =>
            {
                Thing target = TargetA.Thing;
                if (!AnimalRefugeOpening.CanDesignate(target)) return;
                var designation = AnimalRefugeOpening.DesignationFor(target);
                if (designation == null) return;
                target.Map.designationManager.RemoveDesignation(designation);
                AnimalBurrowHunt.Open(pawn, target);
            });
        }
    }
}
