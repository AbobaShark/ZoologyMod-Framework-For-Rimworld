using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using Verse;

namespace ZoologyMod
{
    public sealed class AnimalProductRecord
    {
        public string animal;
        public float eggInterval;
        public string eggCount;
        public float hatchDays;
        public string hatchling;
        public float eggNutrition;
        public float milkPerDay;
        public float woolPerDay;
    }

    // One load-time pass over the game's actual XML, before Def inheritance.
    public sealed class PatchOperation_AnimalProducts : PatchOperation
    {
        public List<AnimalProductRecord> products;
        private sealed class EggBinding
        {
            internal AnimalProductRecord record;
            internal XmlElement race;
            internal string egg;
        }

        protected override bool ApplyWorker(XmlDocument xml)
        {
            if (products == null || products.Count == 0) return false;
            var bindings = new List<EggBinding>();
            bool success = true;
            foreach (var product in products)
            {
                XmlElement race = Def(xml, "ThingDef", product.animal);
                if (race == null) continue; // Optional content is not loaded.
                try
                {
                    if (product.eggInterval > 0f)
                    {
                        XmlElement layer = Component(xml, race, "CompProperties_EggLayer", "CompProperties_ExplodingEggLayer");
                        if (layer == null) throw new InvalidOperationException("No supported egg layer");
                        string fertilized = Read(layer, "eggFertilizedDef");
                        XmlElement egg = Def(xml, "ThingDef", fertilized);
                        if (egg == null) throw new InvalidOperationException("Missing fertilized egg " + fertilized);
                        XmlElement hatcher = Component(xml, egg, "CompProperties_Hatcher", "CompProperties_ExplodingHatcher");
                        if (hatcher == null) throw new InvalidOperationException("No supported hatcher in " + fertilized);
                        string kindName = Read(hatcher, "hatcherPawn");
                        XmlElement kind = Def(xml, "PawnKindDef", kindName);
                        string actual = kind == null ? kindName : InheritedText(xml, kind, "race");
                        if (actual != product.hatchling)
                            throw new InvalidOperationException("Hatchling " + actual + " differs from AnimalStats " + product.hatchling);
                        Set(layer, "eggLayIntervalDays", Num(product.eggInterval));
                        Set(layer, "eggCountRange", product.eggCount);
                        Set(hatcher, "hatcherDaystoHatch", Num(product.hatchDays));
                        string unfertilized = Read(layer, "eggUnfertilizedDef");
                        if (!string.IsNullOrEmpty(unfertilized) && unfertilized != fertilized)
                            bindings.Add(new EggBinding { record = product, race = race, egg = unfertilized });
                    }
                    if (product.milkPerDay > 0f)
                    {
                        XmlElement comp = Component(xml, race, "CompProperties_Milkable");
                        if (comp == null && product.animal == "Horse")
                        {
                            comp = AddComponent(xml, race, "CompProperties_Milkable");
                            Set(comp, "milkDef", "Milk");
                        }
                        if (comp == null || Read(comp, "milkDef") == "Chemfuel")
                            throw new InvalidOperationException("No supported food milk component");
                        float days = product.animal.StartsWith("AA_", StringComparison.Ordinal)
                            ? Interval(comp, "milkIntervalDays", 1f) : 1f;
                        Set(comp, "milkIntervalDays", Num(days));
                        Set(comp, "milkAmount", Count(product.milkPerDay * days));
                    }
                    if (product.woolPerDay > 0f)
                    {
                        XmlElement comp = Component(xml, race, "CompProperties_Shearable");
                        if (comp == null && product.animal == "Dromedary")
                        {
                            // Camel wool is supplied by the compatible animal mod.
                            if (Def(xml, "ThingDef", "WoolCamel") == null) continue;
                            comp = AddComponent(xml, race, "CompProperties_Shearable");
                            Set(comp, "woolDef", "WoolCamel");
                            Set(comp, "shearIntervalDays", "15");
                        }
                        string amount = "woolAmount", interval = "shearIntervalDays";
                        if (comp == null)
                        {
                            comp = Component(xml, race, "CompProperties_AnimalProduct");
                            amount = "resourceAmount";
                            interval = "gatheringIntervalDays";
                        }
                        if (comp == null) throw new InvalidOperationException("No supported textile component");
                        float days = product.animal.StartsWith("AA_", StringComparison.Ordinal)
                            || product.animal == "Dromedary" ? Interval(comp, interval, 10f) : 10f;
                        Set(comp, interval, Num(days));
                        Set(comp, amount, Count(product.woolPerDay * days));
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("[Zoology] Animal products " + product.animal + ": " + ex.Message);
                    success = false;
                }
            }

            // Unfertilized eggs can be shared by species with different budgets.
            // Keep the first definition and split only unequal nutritional groups.
            foreach (var group in bindings.GroupBy(b => b.egg))
            {
                EggBinding first = group.First();
                foreach (EggBinding binding in group)
                {
                    if (Math.Abs(first.record.eggNutrition - binding.record.eggNutrition) <= 0.00001f)
                        continue;
                    XmlElement original = Def(xml, "ThingDef", binding.egg);
                    if (original == null)
                    {
                        Log.Error("[Zoology] Missing shared egg " + binding.egg);
                        success = false;
                        continue;
                    }
                    string name = "Zoology_" + binding.record.animal + "_UnfertilizedEgg";
                    XmlElement copy = Def(xml, "ThingDef", name);
                    if (copy == null)
                    {
                        copy = (XmlElement)original.CloneNode(true);
                        copy.RemoveAttribute("Name");
                        Set(copy, "defName", name);
                        xml.DocumentElement.AppendChild(copy);
                    }
                    XmlElement layer = Component(xml, binding.race, "CompProperties_EggLayer", "CompProperties_ExplodingEggLayer");
                    if (layer == null)
                    {
                        Log.Error("[Zoology] Missing egg layer for " + binding.record.animal);
                        success = false;
                        continue;
                    }
                    Set(layer, "eggUnfertilizedDef", name);
                }
            }
            return success;
        }

        private static string Num(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string Count(float value) =>
            Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

        private static float Interval(XmlElement comp, string field, float fallback)
        {
            return float.TryParse(Read(comp, field), NumberStyles.Float, CultureInfo.InvariantCulture, out float n)
                && n > 0f ? n : fallback;
        }

        private static XmlElement Def(XmlDocument xml, string type, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (XmlElement node in xml.DocumentElement.SelectNodes(type))
                if (Read(node, "defName") == name) return node;
            return null;
        }

        private static string Read(XmlElement node, string field) =>
            node?[field]?.InnerText?.Trim();

        private static void Set(XmlElement node, string field, string value)
        {
            XmlElement child = node[field];
            if (child == null)
            {
                child = node.OwnerDocument.CreateElement(field);
                node.AppendChild(child);
            }
            child.InnerText = value;
        }

        private static XmlElement Parent(XmlDocument xml, XmlElement node)
        {
            string name = node.GetAttribute("ParentName");
            if (string.IsNullOrEmpty(name)) return null;
            foreach (XmlElement candidate in xml.DocumentElement.SelectNodes(node.Name))
                if (candidate.GetAttribute("Name") == name) return candidate;
            throw new InvalidOperationException("Missing XML parent " + name);
        }

        private static string InheritedText(XmlDocument xml, XmlElement node, string field)
        {
            var visited = new HashSet<XmlElement>();
            while (node != null && visited.Add(node))
            {
                string value = Read(node, field);
                if (value != null) return value;
                node = Parent(xml, node);
            }
            return null;
        }

        private static void Collect(XmlDocument xml, XmlElement race, List<XmlElement> result,
            HashSet<XmlElement> visited)
        {
            if (!visited.Add(race)) throw new InvalidOperationException("Cyclic XML inheritance");
            XmlElement direct = race["comps"];
            if (direct == null || !string.Equals(direct.GetAttribute("Inherit"), "False", StringComparison.OrdinalIgnoreCase))
            {
                XmlElement parent = Parent(xml, race);
                if (parent != null) Collect(xml, parent, result, visited);
            }
            if (direct != null)
                foreach (XmlNode child in direct.ChildNodes)
                    if (child is XmlElement element && element.Name == "li") result.Add(element);
        }

        private static XmlElement Component(XmlDocument xml, XmlElement race, params string[] types)
        {
            var components = new List<XmlElement>();
            Collect(xml, race, components, new HashSet<XmlElement>());
            var matches = components.Where(c =>
                types.Any(t => c.GetAttribute("Class") == t ||
                    c.GetAttribute("Class").EndsWith("." + t, StringComparison.Ordinal))).ToList();
            if (matches.Count == 0) return null;
            if (matches.Count != 1) throw new InvalidOperationException("Ambiguous production components");
            XmlElement match = matches[0];
            if (match.ParentNode == race["comps"]) return match;
            // Materialize the effective list only for an inherited component.
            int position = components.IndexOf(match);
            XmlElement copied = xml.CreateElement("comps");
            copied.SetAttribute("Inherit", "False");
            foreach (XmlElement component in components) copied.AppendChild(component.CloneNode(true));
            if (race["comps"] != null) race.ReplaceChild(copied, race["comps"]);
            else race.AppendChild(copied);
            return (XmlElement)copied.ChildNodes[position];
        }

        private static XmlElement AddComponent(XmlDocument xml, XmlElement race, string type)
        {
            XmlElement comps = race["comps"];
            if (comps == null)
            {
                comps = xml.CreateElement("comps");
                race.AppendChild(comps);
            }
            XmlElement entry = xml.CreateElement("li");
            entry.SetAttribute("Class", type);
            comps.AppendChild(entry);
            return entry;
        }
    }
}
