#if RIMWORLD17
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Shared by ordinary pen markers and barns, using the same lactation
    // and mammal-infant definitions as the actual nursing system.
    internal static class AnimalNursingFilterUtility
    {
        internal static bool IsMilkDependent(Pawn pawn)
        {
            return pawn?.RaceProps?.Animal == true
                && LactationSettingsGate.Enabled()
                && pawn.IsMammal()
                && AnimalLactationUtility.IsAnimalBaby(pawn);
        }

        internal static bool IsLactatingFemale(Pawn pawn)
        {
            if (pawn?.gender != Gender.Female) return false;
            HediffDef lactation = AnimalLactationUtility.LactatingHediffDef;
            return lactation != null
                && pawn.health?.hediffSet?.HasHediff(lactation) == true;
        }
    }

    // These checks run when vanilla ThingFilter considers a candidate,
    // never as a separate per-animal tick or periodic population scan.
    public sealed class SpecialThingFilterWorker_AllowLactatingFemales
        : SpecialThingFilterWorker
    {
        public override bool CanEverMatch(ThingDef def) =>
            def?.race?.Animal == true;

        public override bool Matches(Thing thing)
        {
            return thing is Pawn pawn
                && AnimalNursingFilterUtility.IsLactatingFemale(pawn);
        }
    }

    public sealed class SpecialThingFilterWorker_AllowNonLactatingFemales
        : SpecialThingFilterWorker
    {
        public override bool CanEverMatch(ThingDef def) =>
            def?.race?.Animal == true;

        public override bool Matches(Thing thing)
        {
            // This optional exclusion only concerns reproductive females.
            // Female calves, piglets, chicks and independent juveniles must
            // remain governed by their respective age-group filters.
            return thing is Pawn pawn && pawn.gender == Gender.Female
                && pawn.ageTracker?.CurLifeStage?.reproductive == true
                && !AnimalNursingFilterUtility.IsLactatingFemale(pawn);
        }
    }

    public sealed class SpecialThingFilterWorker_AllowReproductiveAdults
        : SpecialThingFilterWorker
    {
        public override bool CanEverMatch(ThingDef def) =>
            def?.race?.Animal == true;

        public override bool Matches(Thing thing) =>
            thing is Pawn pawn
            && pawn.ageTracker?.CurLifeStage?.reproductive == true
            // Lactating females are admitted under their own filter even
            // if ordinary adult animals are excluded.
            && !AnimalNursingFilterUtility.IsLactatingFemale(pawn);
    }

    public sealed class SpecialThingFilterWorker_AllowJuveniles
        : SpecialThingFilterWorker
    {
        public override bool CanEverMatch(ThingDef def) =>
            def?.race?.Animal == true;

        public override bool Matches(Thing thing) =>
            thing is Pawn pawn && pawn.ageTracker?.CurLifeStage != null
            && !pawn.ageTracker.CurLifeStage.reproductive
            && !AnimalNursingFilterUtility.IsMilkDependent(pawn);
    }

    public sealed class SpecialThingFilterWorker_AllowMilkDependentYoung
        : SpecialThingFilterWorker
    {
        public override bool CanEverMatch(ThingDef def) =>
            def?.race?.Animal == true;

        public override bool Matches(Thing thing) =>
            thing is Pawn pawn
            && AnimalNursingFilterUtility.IsMilkDependent(pawn);
    }
}
#endif
