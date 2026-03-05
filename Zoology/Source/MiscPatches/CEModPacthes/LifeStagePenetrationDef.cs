// LifeStagePenetrationDef.cs
using Verse;

namespace ZoologyMod
{
    // Обязательно public и наследуется от Def
    public class LifeStagePenetrationDef : Def
    {
        public float meleePenetrationSharpFactor = 1f;
        public float meleePenetrationBluntFactor = 1f;
    }
}
