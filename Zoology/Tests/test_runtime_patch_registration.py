"""Exercise production patch lifecycle members with real Harmony and game stubs.

Requires Windows, Python 3, .NET SDK, .NET Framework 4.7.2, and the project's
restored Lib.Harmony 2.4.1. Uses Framework because Harmony 2.4.1 cannot patch .NET 10.
Run: python Zoology/Tests/test_runtime_patch_registration.py
"""
from pathlib import Path
import os
import subprocess
import tempfile
from xml.sax.saxutils import escape


source = (Path(__file__).resolve().parents[1] / 'Source' / 'ZoologyModInt.cs').read_text(
    encoding='utf-8-sig')


def member(signature):
    start = source.index(signature)
    brace = source.index('{', start)
    end, depth = brace + 1, 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


harmony = Path(os.environ.get('NUGET_PACKAGES', Path.home() / '.nuget' / 'packages')) / (
    'lib.harmony/2.4.1/lib/net472/0Harmony.dll')
if not harmony.exists():
    raise SystemExit('Restore the Zoology project first: Lib.Harmony 2.4.1 is missing.')

with tempfile.TemporaryDirectory(prefix='zoology-patch-registration-') as temporary:
    out = Path(temporary)
    (out / 'Tests.csproj').write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net472</TargetFramework><LangVersion>latest</LangVersion>'
        '<NoWarn>CS0649</NoWarn></PropertyGroup>'
        '<ItemGroup><Reference Include="0Harmony"><HintPath>'
        + escape(str(harmony)) + '</HintPath></Reference></ItemGroup></Project>')
    members = '\n'.join(member(signature) for signature in (
        'public static void SyncRuntimePatchesWithSettings(',
        'private static void EnsureRuntimeAttributePatches(',
        'private static void EnableAllRuntimePatches(',
        'private static void DisableAllRuntimePatches(',
        'private static void UnpatchAllZoologyHarmonyIds(',
        'private static void TryUnpatchHarmonyId(',
    ))
    fields = '\n'.join(line.strip() for line in source.splitlines() if line.strip().startswith((
        'private const string RuntimeHarmonyId', 'private static bool runtimeAttributePatchesApplied')))
    (out / 'Production.cs').write_text('''
using System; using System.Collections.Generic; using HarmonyLib; using Verse;
namespace ZoologyMod {
public static class ZoologyMod {
public static ZoologyModSettings Settings { get; set; } = new();
public static void Initialize() => EnsureRuntimeAttributePatches();
private static void ApplyDisabledFeatureUnpatches(ZoologyModSettings settings) {}
private static void EnsureEggFoodHooksMatchSettings(ZoologyModSettings settings) {}
''' + fields + '\n' + members + '\n}}', encoding='utf-8')
    patchers = (
        'PredationHarmonyPatches', 'LactationPatcher', 'AgelessHarmonyInit',
        'DrugsImmuneHarmonyInit', 'Ectothermic_HarmonyPatches',
        'NoPorcupineQuill_HarmonyPatches', 'CEPatches_Melee')
    (out / 'Stubs.cs').write_text('''
namespace Verse {
public static class Log {
public static void Message(string s) {}
public static void Warning(string s) => throw new System.Exception(s);
}}
namespace ZoologyMod {
public class ZoologyModSettings {
public static ZoologyModSettings Instance;
public bool Disabled;
public bool AreAllRuntimeTogglesDisabled() => Disabled;
}
public static class PetPlayUtility {
public static void SyncJoyGiverDefAvailability(ZoologyModSettings s) {}
}
public static class Patch_SmallPetThreatDisabled {
public static void NotifySettingsChanged() {}
}
''' + '\n'.join('public static class ' + name + ''' {
public static void EnsurePatched() {}
public static void ResetPatchedState() {}
}''' for name in patchers) + '''
}
namespace ZoologyMod.Patches {
public static class DamageReduction_AnimalTypes_PawnTakeDamage {
public static void SyncPatchState() {}
public static void ResetPatchedState() {}
}}
''', encoding='utf-8')
    (out / 'Program.cs').write_text(r'''
using System; using System.Linq; using System.Runtime.CompilerServices;
using HarmonyLib; using ZoologyMod;
static class Target {
public static int Calls;
[MethodImpl(MethodImplOptions.NoInlining)]
public static void Run() { Calls++; }
}
// The outer and inner annotation layout matches the scavenger patch classes.
[HarmonyPatch]
static class Outer {
    [HarmonyPatch(typeof(Target), nameof(Target.Run))]
    static class Inner {
        public static int Calls;
        static void Prefix() { Calls++; }
        public static int ReadCalls() => Calls;
    }
    public static int ReadCalls() => Inner.ReadCalls();
}
// A foreign owner's patch must survive Zoology disable/rebuild cycles.
static class ForeignPatch {
public static int Calls;
public static void Prefix() { Calls++; }
}
static class Program {
static int checks;
static void Check(bool ok, string message) {
    checks++;
    if (!ok) throw new Exception(message);
}
static void Verify(int expected, string phase) {
    var patches = Harmony.GetPatchInfo(AccessTools.Method(typeof(Target), nameof(Target.Run)));
    Check(patches.Prefixes.Count(p => p.owner == "com.abobashark.zoology.bionic") == expected,
          phase + ": Zoology prefix count");
    Check(patches.Prefixes.Count(p => p.owner == "test.foreign") == 1,
          phase + ": foreign prefix survives");
    int before = Outer.ReadCalls(), foreignBefore = ForeignPatch.Calls, originalBefore = Target.Calls;
    Target.Run();
    Check(Outer.ReadCalls() - before == expected, phase + ": actual prefix calls");
    Check(ForeignPatch.Calls - foreignBefore == 1, phase + ": foreign prefix runs once");
    Check(Target.Calls - originalBefore == 1, phase + ": original runs once");
}
static void Main() {
    new Harmony("test.foreign").Patch(
        AccessTools.Method(typeof(Target), nameof(Target.Run)),
        prefix: new HarmonyMethod(typeof(ForeignPatch), nameof(ForeignPatch.Prefix)));
    var repeated = new Harmony("com.abobashark.zoology.bionic");
    repeated.PatchAll(typeof(Program).Assembly);
    Verify(1, "one raw PatchAll with nested classes");
    repeated.PatchAll(typeof(Program).Assembly);
    Verify(2, "reproduced duplicate registration from repeated PatchAll");
    repeated.UnpatchAll("com.abobashark.zoology.bionic");
    Verify(0, "reproduction cleanup");
    ZoologyMod.ZoologyMod.Initialize();
    Verify(1, "initial registration with nested patch class");
    ZoologyMod.ZoologyMod.Initialize();
    Verify(1, "repeated initialization");
    ZoologyMod.ZoologyMod.SyncRuntimePatchesWithSettings(forceRebuild: true);
    Verify(1, "startup rebuild");
    for (int i = 0; i < 3; i++) {
        ZoologyMod.ZoologyMod.SyncRuntimePatchesWithSettings();
        Verify(1, "ordinary sync " + i);
    }
    for (int i = 0; i < 3; i++) {
        ZoologyMod.ZoologyMod.Settings.Disabled = true;
        ZoologyMod.ZoologyMod.SyncRuntimePatchesWithSettings();
        Verify(0, "disable " + i);
        ZoologyMod.ZoologyMod.Settings.Disabled = false;
        ZoologyMod.ZoologyMod.SyncRuntimePatchesWithSettings();
        Verify(1, "enable " + i);
        ZoologyMod.ZoologyMod.SyncRuntimePatchesWithSettings(forceRebuild: true);
        Verify(1, "forced rebuild " + i);
    }
    Console.WriteLine($"Passed {checks} real-Harmony patch lifecycle checks.");
}
}
''', encoding='utf-8')
    subprocess.run(['dotnet', 'build', str(out / 'Tests.csproj'), '-c', 'Release', '--nologo'],
                   check=True)
    subprocess.run([str(out / 'bin' / 'Release' / 'net472' / 'Tests.exe')], check=True)
