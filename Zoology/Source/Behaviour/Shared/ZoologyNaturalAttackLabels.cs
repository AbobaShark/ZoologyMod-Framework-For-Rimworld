using System;
using System.Collections.Generic;
using Verse;

namespace ZoologyMod
{
    internal static class ZoologyNaturalAttackLabels
    {
        // Def translations can address tools by list index. Use the generated
        // attack identities after translation, only for a fully matching list.
        internal static void Apply()
        {
            bool ce = CEChecker.IsCEInstalled();
            foreach (ThingDef race in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (race.race?.Animal != true || race.tools == null || race.tools.Count == 0)
                    continue;
                var profiles = ZoologyGeneratedLifeStageAttacks.For(race.defName, "AnimalBaby")
                    ?? ZoologyGeneratedLifeStageAttacks.For(race.defName, "AnimalJuvenile");
                if (profiles == null) continue;
                var labels = new string[race.tools.Count];
                bool valid = true;
                foreach (var profile in profiles)
                {
                    if (profile == null) { valid = false; break; }
                    int index = ce ? profile.ceToolIndex : profile.vanillaToolIndex;
                    if (index < 0) continue;
                    if (index >= labels.Length || labels[index] != null
                        || !Matches(race.tools[index], profile) || string.IsNullOrWhiteSpace(profile.label))
                    {
                        valid = false;
                        break;
                    }
                    labels[index] = profile.label;
                }
                if (!valid || Array.Exists(labels, label => label == null)) continue;
                for (int i = 0; i < labels.Length; i++)
                {
                    string key = "Zoology_NaturalAttack_" + labels[i].ToLowerInvariant().Replace(' ', '_');
                    race.tools[i].label = key.Translate();
                }
            }
        }

        private static bool Matches(Tool tool, ZoologyLifeStageToolFactor profile)
        {
            if (tool == null || tool.capacities == null || string.IsNullOrEmpty(profile.capacities)
                || !string.Equals(tool.linkedBodyPartsGroup?.defName ?? "",
                    profile.linkedBodyPartsGroup ?? "", StringComparison.OrdinalIgnoreCase))
                return false;
            string[] expected = profile.capacities.Split(',');
            if (expected.Length != tool.capacities.Count) return false;
            foreach (string capacity in expected)
            {
                bool found = false;
                foreach (var actual in tool.capacities)
                    if (string.Equals(actual?.defName, capacity.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                if (!found) return false;
            }
            return true;
        }
    }
}
