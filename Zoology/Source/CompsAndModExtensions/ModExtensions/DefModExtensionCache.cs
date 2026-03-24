using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    internal static class DefModExtensionCache<TExtension> where TExtension : DefModExtension
    {
        private static readonly Dictionary<ThingDef, TExtension> thingDefCache = new Dictionary<ThingDef, TExtension>();
        private static readonly Dictionary<PawnKindDef, TExtension> pawnKindDefCache = new Dictionary<PawnKindDef, TExtension>();

        public static TExtension Get(ThingDef def)
        {
            if (def == null)
            {
                return null;
            }

            if (thingDefCache.TryGetValue(def, out TExtension cached))
            {
                return cached;
            }

            TExtension extension = def.GetModExtension<TExtension>();
            thingDefCache[def] = extension;
            return extension;
        }

        public static TExtension Get(PawnKindDef kindDef)
        {
            if (kindDef == null)
            {
                return null;
            }

            if (pawnKindDefCache.TryGetValue(kindDef, out TExtension cached))
            {
                return cached;
            }

            TExtension extension = kindDef.GetModExtension<TExtension>();
            pawnKindDefCache[kindDef] = extension;
            return extension;
        }

        public static bool TryGet(Pawn pawn, out TExtension extension)
        {
            extension = Get(pawn?.kindDef);
            if (extension != null)
            {
                return true;
            }

            extension = Get(pawn?.def);
            return extension != null;
        }

        public static bool Has(Pawn pawn)
        {
            return TryGet(pawn, out _);
        }

        public static void Clear()
        {
            thingDefCache.Clear();
            pawnKindDefCache.Clear();
        }
    }
}
