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

        internal static bool IsPregnantFemale(Pawn pawn)
        {
            if (pawn?.gender != Gender.Female
                || pawn.ageTracker?.CurLifeStage?.reproductive != true)
                return false;
            var hediffSet = pawn.health?.hediffSet;
            if (hediffSet == null) return false;
            // One list scan covers vanilla pregnancies and custom
            // Hediff_Pregnant derivatives without duplicate health lookups.
            var hediffs = hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (hediff.def == HediffDefOf.Pregnant
                    || hediff is Hediff_Pregnant)
                    return true;
            }
            return false;
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
                && pawn.ageTracker?.CurLifeStage?.reproductive == true
                && AnimalNursingFilterUtility.IsLactatingFemale(pawn);
        }
    }

    public sealed class SpecialThingFilterWorker_AllowPregnantFemales
        : SpecialThingFilterWorker
    {
        public override bool CanEverMatch(ThingDef def) =>
            def?.race?.Animal == true;

        public override bool Matches(Thing thing) =>
            thing is Pawn pawn
            && AnimalNursingFilterUtility.IsPregnantFemale(pawn);
    }

    public sealed class SpecialThingFilterWorker_AllowNonLactatingFemales
        : SpecialThingFilterWorker
    {
        public override bool CanEverMatch(ThingDef def) =>
            def?.race?.Animal == true;

        public override bool Matches(Thing thing)
        {
            // Other adult females are neither pregnant nor lactating.
            // Young females are controlled by their own age groups.
            return thing is Pawn pawn && pawn.gender == Gender.Female
                && pawn.ageTracker?.CurLifeStage?.reproductive == true
                && !AnimalNursingFilterUtility.IsLactatingFemale(pawn)
                && !AnimalNursingFilterUtility.IsPregnantFemale(pawn);
        }
    }

    public sealed class SpecialThingFilterWorker_AllowReproductiveAdults
        : SpecialThingFilterWorker
    {
        public override bool CanEverMatch(ThingDef def) =>
            def?.race?.Animal == true;

        public override bool Matches(Thing thing) =>
            thing is Pawn pawn
            && pawn.ageTracker?.CurLifeStage?.reproductive == true;
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
