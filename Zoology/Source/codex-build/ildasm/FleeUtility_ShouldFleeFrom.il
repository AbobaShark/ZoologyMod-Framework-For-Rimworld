
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0



// предупреждение: ЭТО НЕПОЛНЫЙ ДИЗАССЕМБЛЕРНЫЙ КОД, НЕПРИГОДНЫЙ ДЛЯ ТРАНСЛЯЦИИ



.class public abstract auto ansi sealed beforefieldinit RimWorld.FleeUtility
       extends [mscorlib]System.Object
{
  .method public hidebysig static bool  ShouldFleeFrom(class Verse.Thing t,
                                                       class Verse.Pawn pawn,
                                                       bool checkDistance,
                                                       bool checkLOS) cil managed
  {
    // Размер кода:       121 (0x79)
    .maxstack  3
    .locals init (class Verse.AI.IAttackTarget V_0,
             valuetype Verse.IntVec3 V_1)
    IL_0000:  ldarg.0
    IL_0001:  ldarg.1
    IL_0002:  beq.s      IL_0022

    IL_0004:  ldarg.2
    IL_0005:  brfalse.s  IL_0024

    IL_0007:  ldarg.0
    IL_0008:  callvirt   instance valuetype Verse.IntVec3 Verse.Thing::get_Position()
    IL_000d:  stloc.1
    IL_000e:  ldloca.s   V_1
    IL_0010:  ldarg.1
    IL_0011:  callvirt   instance valuetype Verse.IntVec3 Verse.Thing::get_Position()
    IL_0016:  ldc.r4     8.
    IL_001b:  call       instance bool Verse.IntVec3::InHorDistOf(valuetype Verse.IntVec3,
                                                                  float32)
    IL_0020:  brtrue.s   IL_0024

    IL_0022:  ldc.i4.0
    IL_0023:  ret

    IL_0024:  ldarg.0
    IL_0025:  ldfld      class Verse.ThingDef Verse.Thing::def
    IL_002a:  ldfld      bool Verse.ThingDef::alwaysFlee
    IL_002f:  brfalse.s  IL_0033

    IL_0031:  ldc.i4.1
    IL_0032:  ret

    IL_0033:  ldarg.0
    IL_0034:  ldarg.1
    IL_0035:  call       bool RimWorld.GenHostility::HostileTo(class Verse.Thing,
                                                               class Verse.Thing)
    IL_003a:  brtrue.s   IL_003e

    IL_003c:  ldc.i4.0
    IL_003d:  ret

    IL_003e:  ldarg.0
    IL_003f:  isinst     Verse.AI.IAttackTarget
    IL_0044:  stloc.0
    IL_0045:  ldloc.0
    IL_0046:  brfalse.s  IL_0075

    IL_0048:  ldloc.0
    IL_0049:  ldarg.1
    IL_004a:  callvirt   instance bool Verse.AI.IAttackTarget::ThreatDisabled(class Verse.AI.IAttackTargetSearcher)
    IL_004f:  brtrue.s   IL_0075

    IL_0051:  ldarg.0
    IL_0052:  isinst     Verse.AI.IAttackTargetSearcher
    IL_0057:  brfalse.s  IL_0075

    IL_0059:  ldarg.3
    IL_005a:  brfalse.s  IL_0077

    IL_005c:  ldarg.1
    IL_005d:  callvirt   instance valuetype Verse.IntVec3 Verse.Thing::get_Position()
    IL_0062:  ldarg.0
    IL_0063:  callvirt   instance valuetype Verse.IntVec3 Verse.Thing::get_Position()
    IL_0068:  ldarg.1
    IL_0069:  callvirt   instance class Verse.Map Verse.Thing::get_Map()
    IL_006e:  call       bool Verse.GenSight::LineOfSight(valuetype Verse.IntVec3,
                                                          valuetype Verse.IntVec3,
                                                          class Verse.Map)
    IL_0073:  brtrue.s   IL_0077

    IL_0075:  ldc.i4.0
    IL_0076:  ret

    IL_0077:  ldc.i4.1
    IL_0078:  ret
  } // end of method FleeUtility::ShouldFleeFrom

} // end of class RimWorld.FleeUtility

// *********** ДИЗАССЕМБЛИРОВАНИЕ ЗАВЕРШЕНО ***********************
// ВНИМАНИЕ: создан файл ресурсов Win32 codex-build\ildasm\FleeUtility_ShouldFleeFrom.res
