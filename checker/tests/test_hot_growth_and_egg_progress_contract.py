"""Runtime hot-path contracts for growth, rendering and clutch development."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2] / "Zoology/Source/Behaviour"


class HotGrowthContracts(unittest.TestCase):
    def test_body_and_draw_use_bounded_thread_local_cache(self):
        src = (ROOT / "Shared/ZoologyAnimalGrowth.cs").read_text(encoding="utf-8")
        self.assertIn("[ThreadStatic] private static HotRatio[] hotRatios;", src)
        self.assertIn("pawn.thingIDNumber & (HotCacheSize - 1)", src)
        for key in ("entry.pawn == pawn", "entry.race == pawn.def", "entry.kind == pawn.kindDef",
                    "entry.stage == stage", "entry.day == day", "entry.gender == pawn.gender",
                    "entry.version == version", "entry.mode == mode"):
            self.assertIn(key, src)
        self.assertIn("if (!entry.bodyReady)", src)
        self.assertIn("if (!entry.drawReady)", src)
        self.assertIn("snapshot.sexEnabled != ZoologySexAdultProfiles.Enabled", src)

    def test_parallel_render_only_reads_prepared_graphic_bounds(self):
        bounds = (ROOT / "Shared/ZoologyAnimalGraphicBounds.cs").read_text(encoding="utf-8")
        graphics = (ROOT / "Shared/ZoologyAnimalGraphics.cs").read_text(encoding="utf-8")
        prepare = bounds.split("internal static void PrepareCache()", 1)[1].split(
            "internal static Vector2 NormalizedDrawSize", 1)[0]
        self.assertIn("new Dictionary<PawnKindDef, KindBoundsData[]>()", prepare)
        self.assertIn("ZoologyAnimalGrowth.HandlesRace(kind.race)", prepare)
        self.assertIn("Build(kind, false)", prepare)
        self.assertIn("Build(kind, true)", prepare)
        self.assertLess(prepare.index("prepared.Add(kind"), prepare.index("Volatile.Write(ref kindBounds, prepared)"))
        lookup = bounds.split("private static KindBoundsData GetPrepared(", 1)[1].split(
            "private static KindBoundsData Build(", 1)[0]
        self.assertIn("Volatile.Read(ref kindBounds)", lookup)
        self.assertIn("prepared.TryGetValue(kind", lookup)
        for unsafe in ("Build(", ".Add(", ".Clear(", "TryBounds(", "ResolveGraphic("):
            self.assertNotIn(unsafe, lookup)
        self.assertNotIn("GetOrBuild(", bounds)
        self.assertEqual(bounds.count("Volatile.Write(ref kindBounds, prepared)"), 1)
        sync = graphics.split("internal static void Sync(", 1)[1].split(
            "private static List<PawnKindLifeStage> BuildVariant", 1)[0]
        self.assertLess(sync.index("item.Key.lifeStages = cached[mode]"),
                        sync.index("ZoologyAnimalGraphicBounds.PrepareCache();"))
        self.assertLess(sync.index("ZoologyAnimalGraphicBounds.PrepareCache();"),
                        sync.index("Interlocked.Increment(ref graphicVersion);"))

    def test_descendant_render_returns_without_callback(self):
        src = (ROOT / "Shared/Patch_ZoologyAnimalGrowth.cs").read_text(encoding="utf-8")
        render = src.split("internal static class Patch_ZoologyDailyAnimalRenderScale", 1)[1]
        self.assertNotIn("void Postfix", render)
        self.assertIn("OpCodes.Ldfld, parent", render)
        self.assertLess(render.index("OpCodes.Brtrue, done"), render.index("OpCodes.Call, scale"))
        self.assertIn("ReferenceEquals(node.tree?.rootNode, node)", render)

    def test_egg_progress_writes_keep_native_value_and_sync_transitions(self):
        src = (ROOT / "Reproduction/ZoologyReproductiveEnergetics.cs").read_text(encoding="utf-8")
        hook = src.split("internal static class EggProgressWrite", 1)[1].split(
            "internal static class Patch_ZoologyEggLaid", 1)[0]
        self.assertNotIn("void Postfix", hook)
        self.assertIn("i.opcode == OpCodes.Stfld", hook)
        self.assertIn("instruction.opcode = OpCodes.Call", hook)
        self.assertIn("wasDeveloping != developing", hook)
        self.assertIn("developing && layer.parent is Pawn pawn && pawn.IsHashIntervalTick(250, interval)", hook)
        self.assertIn("progress = value;", src)
        self.assertIn("EggProgressWrite.Transpile(instructions, false)", hook)
        self.assertIn("EggProgressWrite.Transpile(instructions, true)", hook)


if __name__ == "__main__":
    unittest.main()
