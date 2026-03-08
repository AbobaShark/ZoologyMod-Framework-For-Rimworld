using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    internal static class PredationCacheUtility
    {
        private const string PhotonozoaPropertiesTypeName = "PhotonozoaProperties";
        private const string PhotonozoaPropertiesSuffix = ".PhotonozoaProperties";
        private const string PhotonozoaFactionDefName = "Photonozoa";

        private static readonly Dictionary<ThingDef, bool> photonozoaCache = new Dictionary<ThingDef, bool>();
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
