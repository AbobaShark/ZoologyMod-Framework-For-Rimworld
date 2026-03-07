using Verse;
using System.Linq;

namespace ZoologyMod
{
    public static class ModConstants
    {
        
        public const float DefaultSmallPetBodySizeThreshold = 0.45f;
        public const float DefaultSafePredatorBodySizeThreshold = 0.7f;
        public const float DefaultSafeNonPredatorBodySizeThreshold = 3f;

        
        public static ZoologyModSettings Settings => ZoologyMod.Instance?.GetSettings<ZoologyModSettings>() ?? new ZoologyModSettings();

        
        public static float SmallPetBodySizeThreshold => Settings.SmallPetBodySizeThreshold;
        public static float SafePredatorBodySizeThreshold => Settings.SafePredatorBodySizeThreshold;
        public static float SafeNonPredatorBodySizeThreshold => Settings.SafeNonPredatorBodySizeThreshold;
    }
}