"""Run production hot-path members against instrumented game stubs.

Requires Python 3 and .NET 10 SDK. Run from any directory:
python Zoology/Tests/test_npc_animal_companion_hot_path.py
This verifies call costs and behavior, not in-game frame times.
"""
from pathlib import Path
import shutil
import subprocess
import tempfile


root = Path(__file__).resolve().parents[1] / 'Source' / 'Behaviour' / 'NpcAnimalCompanions'


def member(source, signature):
    start = source.index(signature)
    brace = source.index('{', start)
    depth = 1
    end = brace + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


manager = (root / 'NpcAnimalCompanionManager.cs').read_text(encoding='utf-8-sig')
adapter = (root / 'NpcAnimalCompanionAI.cs').read_text(encoding='utf-8-sig')
patches = (root / 'NpcAnimalCompanionPatches.cs').read_text(encoding='utf-8-sig')
dotnet = shutil.which('dotnet')
if dotnet is None:
    raise SystemExit('Missing .NET SDK')

with tempfile.TemporaryDirectory(prefix='zoology-npc-hot-path-') as temporary:
    out = Path(temporary)
    (out / 'Tests.csproj').write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
    (out / 'Production.cs').write_text('''
using System.Collections.Generic; using Verse; using RimWorld; using Verse.AI;
namespace ZoologyMod {
internal class NpcAnimalCompanionManager : GameComponent {
private readonly Dictionary<Pawn, NpcAnimalCompanionLink> linkByAnimal = new();
''' + '\n'.join(
        line.strip() for line in manager.splitlines()
        if line.strip().startswith(('private static Game cachedGame;',
                                    'private static NpcAnimalCompanionManager cachedManager;'))
    ) + '\n' + member(manager, 'public static NpcAnimalCompanionManager Current')
        + '\n' + member(manager, 'public bool TryGetLink(') + '''
// Test-only lifecycle controls simulate the production index mutations.
public void Add(Pawn animal, NpcAnimalCompanionLink link) => linkByAnimal.Add(animal, link);
public void Remove(Pawn animal) => linkByAnimal.Remove(animal);
}
''' + member(adapter, 'internal static class NpcAnimalCompanionVanillaAdapter') + '''
internal static class Patch {
''' + member(patches, 'private static void Postfix(').replace('private static', 'public static', 1)
        + '\n}}', encoding='utf-8')
    (out / 'Program.cs').write_text(r'''
using System; using System.Collections.Generic; using Verse; using ZoologyMod;
namespace Verse {
class GameComponent {} class DummyComponent : GameComponent {}
class Game {
    public List<GameComponent> Components = new();
    public int Lookups, ComponentVisits;
    public T GetComponent<T>() where T : GameComponent {
        Lookups++;
        foreach (var component in Components) {
            ComponentVisits++;
            if (component is T result) return result;
        }
        return null;
    }
}
static class Current { public static Game Game; }
class Faction { public bool IsPlayer; }
class Map {}
class Pawn {
    public bool Spawned = true, Dead, Destroyed, Downed, InMentalState, Fighting;
    public bool Reachable = true, Hostile = true;
    public Faction Faction; public Map Map; public Pawn CarriedBy;
    public int ReachCalls; public Pawn LastReachTarget;
    public bool CanReach(Pawn target, Verse.AI.PathEndMode mode, Danger danger) {
        ReachCalls++; LastReachTarget = target; return Reachable;
    }
    public bool HostileTo(Pawn pawn) => Hostile;
    public bool IsFighting() => Fighting;
}
enum Danger { Deadly }
static class Extensions { public static bool DestroyedOrNull(this Pawn p) => p == null || p.Destroyed; }
}
namespace Verse.AI { enum PathEndMode { OnCell } }
namespace RimWorld {}
namespace ZoologyMod {
enum NpcAnimalCompanionState { FollowingMaster, PanicFlee }
class NpcAnimalCompanionLink { public Pawn Master; public NpcAnimalCompanionState State; }
static class NpcAnimalCompanionUtility { public static bool SystemEnabled = true; }
}
class Program {
static int checks;
static void Check(bool condition, string name) {
    if (!condition) throw new Exception(name);
    checks++; Console.WriteLine("PASS " + name);
}
static Game NewGame(NpcAnimalCompanionManager manager) {
    var game = new Game();
    for (int i = 0; i < 256; i++) game.Components.Add(new DummyComponent());
    if (manager != null) game.Components.Add(manager);
    Current.Game = game; return game;
}
static bool Follow(Pawn pawn, bool vanilla = false) { Patch.Postfix(pawn, ref vanilla); return vanilla; }
static void Main() {
    var manager = new NpcAnimalCompanionManager();
    var game = NewGame(manager);
    var map = new Map(); var npcFaction = new Faction();
    var wild = new Pawn { Map = map };
    var player = new Pawn { Map = map, Faction = new Faction { IsPlayer = true } };
    var npc = new Pawn { Map = map, Faction = npcFaction };
    const int iterations = 1_000_000;
    bool unexpected = false;
    for (int i = 0; i < iterations; i++) unexpected |= Follow(wild) || Follow(player);
    Check(!unexpected && game.Lookups == 0, "2,000,000 wild/player calls: zero component lookups");
    Check(!Follow(null) && !Follow(new Pawn { Spawned = false, Faction = npcFaction }) && game.Lookups == 0,
        "null/unspawned pawn exits before manager lookup");
    Check(Follow(npc, true) && game.Lookups == 0, "vanilla true result preserved without manager lookup");
    NpcAnimalCompanionUtility.SystemEnabled = false;
    Check(!NpcAnimalCompanionVanillaAdapter.TryGetFollowingLink(npc, out var disabledLink)
        && disabledLink == null && game.Lookups == 0, "disabled system exits before manager lookup");
    NpcAnimalCompanionUtility.SystemEnabled = true;
    for (int i = 0; i < iterations; i++) unexpected |= Follow(npc);
    Check(!unexpected && game.Lookups == 1 && game.ComponentVisits == 257 && npc.ReachCalls == 0,
        "1,000,000 unregistered NPC calls: one component lookup, zero reachability checks");
    Check(!NpcAnimalCompanionVanillaAdapter.TryGetFollowingLink(npc, out var absent) && absent == null,
        "missing link is reported as null");
    var master = new Pawn { Map = map, Faction = npcFaction };
    var link = new NpcAnimalCompanionLink { Master = master };
    manager.Add(npc, link);
    Check(Follow(npc) && npc.ReachCalls == 1, "newly registered reachable companion follows immediately");
    npc.Reachable = false;
    Check(!Follow(npc), "unreachable master does not grant fear immunity");
    npc.Reachable = true; master.Map = new Map(); var reachCalls = npc.ReachCalls;
    Check(!Follow(npc) && npc.ReachCalls == reachCalls, "master on another map exits before reachability");
    master.Map = map; master.Dead = true;
    Check(!Follow(npc), "dead master does not follow"); master.Dead = false;
    master.Destroyed = true;
    Check(!Follow(npc), "destroyed master does not follow"); master.Destroyed = false;
    master.Spawned = false;
    Check(!Follow(npc), "absent master without carrier does not follow");
    var carrier = new Pawn { Map = map }; master.CarriedBy = carrier;
    Check(Follow(npc) && npc.LastReachTarget == carrier, "kidnapped master uses carrier reachability");
    carrier.Hostile = false;
    Check(!Follow(npc), "friendly carrier does not grant fear immunity");
    master.Spawned = true; link.State = NpcAnimalCompanionState.PanicFlee; reachCalls = npc.ReachCalls;
    Check(!Follow(npc) && npc.ReachCalls == reachCalls, "panic-flee companion exits before reachability");
    link.State = NpcAnimalCompanionState.FollowingMaster;
    NpcAnimalCompanionUtility.SystemEnabled = false;
    Check(!Follow(npc) && npc.ReachCalls == reachCalls, "runtime disable takes effect immediately");
    NpcAnimalCompanionUtility.SystemEnabled = true;
    Check(Follow(npc), "runtime enable takes effect immediately");
    npc.Faction = player.Faction;
    Check(!Follow(npc) && !NpcAnimalCompanionVanillaAdapter.TryGetFollowingLink(npc, out _),
        "player takeover rejects stale NPC link"); npc.Faction = npcFaction;
    master.Fighting = true;
    Check(NpcAnimalCompanionVanillaAdapter.IsReleased(npc), "fighting master releases companion");
    master.Downed = true;
    Check(!NpcAnimalCompanionVanillaAdapter.IsReleased(npc), "downed master does not release companion");
    master.Downed = false; master.InMentalState = true;
    Check(!NpcAnimalCompanionVanillaAdapter.IsReleased(npc), "master in mental state does not release companion");
    manager.Remove(npc); reachCalls = npc.ReachCalls;
    Check(!Follow(npc) && npc.ReachCalls == reachCalls, "removed last link takes effect immediately");
    manager.Add(npc, link);
    var secondManager = new NpcAnimalCompanionManager(); var secondGame = NewGame(secondManager);
    Check(ReferenceEquals(NpcAnimalCompanionManager.Current, secondManager) && secondGame.Lookups == 1,
        "switching games replaces cached manager");
    Check(!Follow(npc) && secondGame.Lookups == 1, "new game does not retain old links or repeat component lookup");
    Current.Game = null;
    Check(NpcAnimalCompanionManager.Current == null, "leaving game clears manager cache");
    var initializingGame = NewGame(null);
    Check(NpcAnimalCompanionManager.Current == null, "missing component during initialization handled");
    initializingGame.Components.Add(secondManager);
    Check(ReferenceEquals(NpcAnimalCompanionManager.Current, secondManager), "missing component retried after initialization");
    Console.WriteLine($"{checks} checks passed");
}
}
''', encoding='utf-8')
    subprocess.run([dotnet, 'run', '--project', str(out / 'Tests.csproj'),
                    '--configuration', 'Release', '--verbosity', 'quiet'], check=True)
