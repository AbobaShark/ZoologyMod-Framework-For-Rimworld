using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    internal static class ZoologyCacheUtility
    {
        private const string PhotonozoaPropertiesTypeName = "PhotonozoaProperties";
        private const string PhotonozoaPropertiesSuffix = ".PhotonozoaProperties";
        private const string PhotonozoaFactionDefName = "Photonozoa";

        private static readonly Dictionary<ThingDef, bool> photonozoaCache = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<ThingDef, bool> insectoidCache = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<ThingDef, bool> thrumboLikeCache = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<ThingDef, bool> noFleeExtensionCache = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<ThingDef, bool> fleeFromCarrierExtensionCache = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<ThingDef, bool> ectothermicExtensionCache = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<ThingDef, bool> scavengerExtensionCache = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<ThingDef, bool> agroAtSlaughterExtensionCache = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<ThingDef, bool> cannotBeMutatedExtensionCache = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<ThingDef, bool> cannotBeAugmentedExtensionCache = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<ThingDef, bool> noPorcupineQuillExtensionCache = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<ThingDef, bool> mammalThingDefExtensionCache = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<PawnKindDef, bool> mammalPawnKindExtensionCache = new Dictionary<PawnKindDef, bool>();
        private static readonly Dictionary<OrderedDefPairKey, bool> crossbreedCache = new Dictionary<OrderedDefPairKey, bool>();

        private static FactionDef photonozoaFactionDef;
        private static bool photonozoaFactionResolved;

        public static bool IsPhotonozoa(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }

            if (photonozoaCache.TryGetValue(def, out bool cached))
            {
                return cached;
            }

            bool result = HasPhotonozoaExtension(def);
            photonozoaCache[def] = result;
            return result;
        }

        public static bool IsPhotonozoaPairInTheirFaction(Pawn first, Pawn second)
        {
            if (first?.Faction == null || second?.Faction == null)
            {
                return false;
            }

            if (!IsPhotonozoa(first.def) || !IsPhotonozoa(second.def))
            {
                return false;
            }

            FactionDef factionDef = GetPhotonozoaFactionDef();
            return factionDef != null && first.Faction.def == factionDef && second.Faction.def == factionDef;
        }

        public static bool IsInsectoid(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }

            if (insectoidCache.TryGetValue(def, out bool cached))
            {
                return cached;
            }

            bool result = def.race?.FleshType == FleshTypeDefOf.Insectoid;
            insectoidCache[def] = result;
            return result;
        }

        public static bool IsExcludedFromHumanFleeByDefault(ThingDef def)
        {
            return IsInsectoid(def)
                || IsPhotonozoa(def)
                || IsThrumboLike(def)
                || HasNoFleeExtension(def)
                || HasFleeFromCarrierExtension(def);
        }

        public static bool IsAnimalThingDef(ThingDef def)
        {
            RaceProperties race = def?.race;
            return def != null && !def.IsCorpse && race != null && race.Animal && !race.Humanlike;
        }

        public static bool IsThrumboLike(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }

            if (thrumboLikeCache.TryGetValue(def, out bool cached))
            {
                return cached;
            }

            string defName = def.defName;
            bool result = !string.IsNullOrEmpty(defName)
                && defName.IndexOf("Thrumbo", StringComparison.OrdinalIgnoreCase) >= 0;

            thrumboLikeCache[def] = result;
            return result;
        }

        public static bool HasNoFleeExtension(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }

            if (noFleeExtensionCache.TryGetValue(def, out bool cached))
            {
                return cached;
            }

            bool result = DefModExtensionCache<ModExtension_NoFlee>.Get(def) != null;
            noFleeExtensionCache[def] = result;
            return result;
        }

        public static bool HasFleeFromCarrierExtension(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }

            if (fleeFromCarrierExtensionCache.TryGetValue(def, out bool cached))
            {
                return cached;
            }

            bool result = DefModExtensionCache<ModExtension_FleeFromCarrier>.Get(def) != null;
            fleeFromCarrierExtensionCache[def] = result;
            return result;
        }

        public static bool HasEctothermicExtension(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }

            if (ectothermicExtensionCache.TryGetValue(def, out bool cached))
            {
                return cached;
            }

            bool result = DefModExtensionCache<ModExtension_Ectothermic>.Get(def) != null;
            ectothermicExtensionCache[def] = result;
            return result;
        }

        public static bool HasScavengerExtension(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }

            if (scavengerExtensionCache.TryGetValue(def, out bool cached))
            {
                return cached;
            }

            bool result = DefModExtensionCache<ModExtension_IsScavenger>.Get(def) != null;
            scavengerExtensionCache[def] = result;
            return result;
        }

        public static bool HasAgroAtSlaughterExtension(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }

            if (agroAtSlaughterExtensionCache.TryGetValue(def, out bool cached))
            {
                return cached;
            }

            bool result = DefModExtensionCache<ModExtension_AgroAtSlaughter>.Get(def) != null;
            agroAtSlaughterExtensionCache[def] = result;
            return result;
        }

        public static bool HasCannotBeMutatedExtension(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }

            if (cannotBeMutatedExtensionCache.TryGetValue(def, out bool cached))
            {
                return cached;
            }

            bool result = DefModExtensionCache<ModExtension_CannotBeMutated>.Get(def) != null;
            cannotBeMutatedExtensionCache[def] = result;
            return result;
        }

        public static bool HasCannotBeAugmentedExtension(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }

            if (cannotBeAugmentedExtensionCache.TryGetValue(def, out bool cached))
            {
                return cached;
            }

            bool result = DefModExtensionCache<ModExtension_CannotBeAugmented>.Get(def) != null;
            cannotBeAugmentedExtensionCache[def] = result;
            return result;
        }

        public static bool HasNoPorcupineQuillExtension(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }

            if (noPorcupineQuillExtensionCache.TryGetValue(def, out bool cached))
            {
                return cached;
            }

            bool result = DefModExtensionCache<ModExtension_NoPorcupineQuill>.Get(def) != null;
            noPorcupineQuillExtensionCache[def] = result;
            return result;
        }

        public static bool HasMammalExtension(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }

            if (mammalThingDefExtensionCache.TryGetValue(def, out bool cached))
            {
                return cached;
            }

            bool result = DefModExtensionCache<ModExtension_IsMammal>.Get(def) != null;
            mammalThingDefExtensionCache[def] = result;
            return result;
        }

        public static bool HasMammalExtension(PawnKindDef kindDef)
        {
            if (kindDef == null)
            {
                return false;
            }

            if (mammalPawnKindExtensionCache.TryGetValue(kindDef, out bool cached))
            {
                return cached;
            }

            bool result = DefModExtensionCache<ModExtension_IsMammal>.Get(kindDef) != null;
            mammalPawnKindExtensionCache[kindDef] = result;
            return result;
        }

        public static bool AreCrossbreedRelated(ThingDef first, ThingDef second)
        {
            if (first == null || second == null)
            {
                return false;
            }

            var key = new OrderedDefPairKey(first, second);
            if (crossbreedCache.TryGetValue(key, out bool cached))
            {
                return cached;
            }

            bool result = IsCrossbreedListed(first, second) || IsCrossbreedListed(second, first);
            crossbreedCache[key] = result;

            if (!ReferenceEquals(first, second))
            {
                crossbreedCache[new OrderedDefPairKey(second, first)] = result;
            }

            return result;
        }

        private static FactionDef GetPhotonozoaFactionDef()
        {
            if (!photonozoaFactionResolved)
            {
                photonozoaFactionDef = DefDatabase<FactionDef>.GetNamedSilentFail(PhotonozoaFactionDefName);
                photonozoaFactionResolved = true;
            }

            return photonozoaFactionDef;
        }

        private static bool HasPhotonozoaExtension(ThingDef def)
        {
            List<DefModExtension> extensions = def.modExtensions;
            if (extensions == null)
            {
                return false;
            }

            for (int i = 0; i < extensions.Count; i++)
            {
                DefModExtension extension = extensions[i];
                if (extension == null)
                {
                    continue;
                }

                Type extensionType = extension.GetType();
                if (extensionType.Name == PhotonozoaPropertiesTypeName)
                {
                    return true;
                }

                string fullName = extensionType.FullName;
                if (fullName != null && fullName.EndsWith(PhotonozoaPropertiesSuffix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCrossbreedListed(ThingDef source, ThingDef target)
        {
            List<ThingDef> crossbreedTargets = source?.race?.canCrossBreedWith;
            if (crossbreedTargets == null)
            {
                return false;
            }

            string targetDefName = target.defName;
            for (int i = 0; i < crossbreedTargets.Count; i++)
            {
                ThingDef candidate = crossbreedTargets[i];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate == target || string.Equals(candidate.defName, targetDefName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private struct OrderedDefPairKey : IEquatable<OrderedDefPairKey>
        {
            private readonly ThingDef first;
            private readonly ThingDef second;

            public OrderedDefPairKey(ThingDef first, ThingDef second)
            {
                this.first = first;
                this.second = second;
            }

            public bool Equals(OrderedDefPairKey other)
            {
                return ReferenceEquals(first, other.first) && ReferenceEquals(second, other.second);
            }

            public override bool Equals(object obj)
            {
                return obj is OrderedDefPairKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int firstHash = first != null ? RuntimeHelpers.GetHashCode(first) : 0;
                    int secondHash = second != null ? RuntimeHelpers.GetHashCode(second) : 0;
                    return (firstHash * 397) ^ secondHash;
                }
            }
        }
    }
}
