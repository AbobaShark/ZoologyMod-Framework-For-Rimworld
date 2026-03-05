// ToolWithGender.cs
using Verse;

namespace ZoologyMod
{
    // Простой подкласс Tool, в который можно положить <restrictedGender> в XML
    public class ToolWithGender : Tool
    {
        public Gender restrictedGender = Gender.None;
    }
}