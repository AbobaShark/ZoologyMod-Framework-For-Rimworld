using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Checker owns all numerical calculations. The game only installs the
    // already computed species x life-stage values during def initialization.
    public sealed class ZoologyLifeStageProfileCatalogDef : Def
    {
        public List<ZoologyLifeStageProfile> stages = new List<ZoologyLifeStageProfile>();
    }

    public sealed class ZoologyLifeStageProfile
    {
        public string animalDefName;
        public string sourceStageDefName;
        public float expectedBaseBodySize = -1f;
        public float expectedBaseHungerRate = -1f;
        public float expectedBodySizeFactor = -1f;

        // Negative means "retain the source LifeStageDef's field".
        public float hungerRateFactor = -1f;
        public float marketValueFactor = -1f;
        public float combatPowerFactor = -1f;

        public List<ZoologyLifeStageStatFactor> statFactors;
        public List<ZoologyLifeStageStatOffset> statOffsets;
        public List<ZoologyLifeStageToolFactor> tools;
    }

    public sealed class ZoologyLifeStageStatFactor
    {
        public string stat;
        public float factor = 1f;
        public bool ceOnly;
    }

    // Calculated absolute armor additions, used when an adult armor base is zero.
    // LifeStageDef supports statFactors only; these additions are applied by a
    // lightweight, CE-aware StatPart without changing ThingDef.statBases.
    public sealed class ZoologyLifeStageStatOffset
    {
        public string stat;
        public float offset;
        public bool ceOnly;
    }

    // Six independent absolute stage/adult ratios, calculated by AnimalStats.
    // Each record changes one tool, not ThingDef.tools as a whole. The mode-specific indices
    // are zero-based in the *post-patch* ThingDef.tools list. The optional label
    // and linked body-part group are consistency checks against other mods.
    public sealed class ZoologyLifeStageToolFactor
    {
        public int vanillaToolIndex = -1;
        public int ceToolIndex = -1;
        public string capacities;
        public string label;
        public string linkedBodyPartsGroup;
        public float vanillaDamageFactor = 1f;
        public float vanillaCooldownFactor = 1f;
        public float ceDamageFactor = 1f;
        public float ceCooldownFactor = 1f;
        public float ceSharpPenetrationFactor = 1f;
        public float ceBluntPenetrationFactor = 1f;

        public bool HasChanges()
        {
            return !ApproximatelyOne(vanillaDamageFactor)
                || !ApproximatelyOne(vanillaCooldownFactor)
                || !ApproximatelyOne(ceDamageFactor)
                || !ApproximatelyOne(ceCooldownFactor)
                || !ApproximatelyOne(ceSharpPenetrationFactor)
                || !ApproximatelyOne(ceBluntPenetrationFactor);
        }

        private static bool ApproximatelyOne(float value)
        {
            return Math.Abs(value - 1f) <= 0.000001f;
        }
    }

    // Runtime-only provenance: a cloned stage must retain its original identity
    // for childcare, nursing and CE penetration lookups.
    public sealed class ZoologyLifeStageOrigin : DefModExtension
    {
        public LifeStageDef sourceStage;
    }
}
