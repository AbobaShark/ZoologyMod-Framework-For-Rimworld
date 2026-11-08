using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Optional, per-race rules for Zoology's beastmastery Draft.
    // Races without this extension retain the original training, master and range checks.
    public class ModExtension_AnimalDraftRules : DefModExtension
    {
        // If either unlock condition is configured, one of them must be met.
        // When neither is configured, Draft availability is unchanged.
        public string draftResearchDefName;
        public string draftHediffDefName;

        // An optional unlock for training Beastmastery itself. Until this
        // research is finished, keep Odyssey's ordinary Attack Target training
        // available. Draft permission is still controlled independently above.
        public string draftTrainingResearchDefName;
        private ResearchProjectDef cachedDraftTrainingResearch;

        public bool IsDraftTrainingUnlocked()
        {
            if (draftTrainingResearchDefName.NullOrEmpty())
            {
                return true;
            }

            cachedDraftTrainingResearch ??=
                DefDatabase<ResearchProjectDef>.GetNamedSilentFail(draftTrainingResearchDefName);
            return cachedDraftTrainingResearch?.IsFinished == true;
        }

        // Each privilege can be unlocked by research OR a hediff independently.
        public string unlimitedRangeResearchDefName;
        public string unlimitedRangeHediffDefName;
        public string masterlessResearchDefName;
        public string masterlessHediffDefName;

        public bool CanDraft(Pawn pawn)
        {
            if (draftResearchDefName.NullOrEmpty() && draftHediffDefName.NullOrEmpty())
            {
                return true;
            }

            return MeetsCondition(pawn, draftResearchDefName, draftHediffDefName);
        }

        public bool CanDraftWithoutMaster(Pawn pawn)
        {
            return MeetsCondition(pawn, masterlessResearchDefName, masterlessHediffDefName);
        }

        public bool HasUnlimitedRange(Pawn pawn)
        {
            // Masterless Draft is necessarily independent of a master's position.
            return CanDraftWithoutMaster(pawn)
                || MeetsCondition(pawn, unlimitedRangeResearchDefName, unlimitedRangeHediffDefName);
        }

        private static bool MeetsCondition(Pawn pawn, string researchDefName, string hediffDefName)
        {
            if (!researchDefName.NullOrEmpty()
                && DefDatabase<ResearchProjectDef>.GetNamedSilentFail(researchDefName)?.IsFinished == true)
            {
                return true;
            }

            if (!hediffDefName.NullOrEmpty()
                && pawn?.health?.hediffSet != null)
            {
                HediffDef hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(hediffDefName);
                return hediffDef != null && pawn.health.hediffSet.HasHediff(hediffDef);
            }

            return false;
        }
    }
}
