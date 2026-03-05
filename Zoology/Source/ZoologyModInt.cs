// ZoologyModInt.cs

using HarmonyLib;
using UnityEngine;
using Verse;
using System.Linq; // If needed elsewhere, but not here

namespace ZoologyMod
{
    public class ZoologyMod : Mod
    {
        public static ZoologyMod Instance { get; private set; }
        public static ZoologyModSettings Settings { get; private set; }

        public ZoologyMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<ZoologyModSettings>();
            var harmony = new Harmony("com.abobashark.zoology.bionic");
            harmony.PatchAll();
            // Run the bionic patcher after defs are loaded
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (Settings.EnableHumanBionicOnAnimal)
                {
                    CombatBionicPatcher.Patch();
                    SpecialBionicPatcher.Patch();
                    SimpleBionicPatcher.Patch();
                }
            });
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var settings = GetSettings<ZoologyModSettings>();
            settings.DoWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "Zoology Mod";
        }
    }
}