
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0



// предупреждение: ЭТО НЕПОЛНЫЙ ДИЗАССЕМБЛЕРНЫЙ КОД, НЕПРИГОДНЫЙ ДЛЯ ТРАНСЛЯЦИИ



.class public auto ansi beforefieldinit RimWorld.JobGiver_AnimalFlee
       extends Verse.AI.ThinkNode_JobGiver
{
  .method family hidebysig virtual instance class Verse.AI.Job 
          TryGiveJob(class Verse.Pawn pawn) cil managed
  {
    // Размер кода:       334 (0x14e)
    .maxstack  4
    .locals init (class [mscorlib]System.Collections.Generic.List`1<class Verse.Thing> V_0,
             class Verse.AI.Job V_1,
             int32 V_2,
             class Verse.AI.Job V_3,
             valuetype Verse.IntVec3 V_4,
             class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> V_5,
             int32 V_6,
             class Verse.Thing V_7,
             class Verse.Pawn V_8,
             class Verse.AI.Job V_9)
    IL_0000:  ldarg.1
    IL_0001:  ldfld      class RimWorld.Pawn_PlayerSettings Verse.Pawn::playerSettings
    IL_0006:  brfalse.s  IL_0017

    IL_0008:  ldarg.1
    IL_0009:  ldfld      class RimWorld.Pawn_PlayerSettings Verse.Pawn::playerSettings
    IL_000e:  callvirt   instance bool RimWorld.Pawn_PlayerSettings::get_UsesConfigurableHostilityResponse()
    IL_0013:  brfalse.s  IL_0017

    IL_0015:  ldnull
    IL_0016:  ret

    IL_0017:  ldarg.1
    IL_0018:  call       bool RimWorld.ThinkNode_ConditionalShouldFollowMaster::ShouldFollowMaster(class Verse.Pawn)
    IL_001d:  brfalse.s  IL_0021

    IL_001f:  ldnull
    IL_0020:  ret

    IL_0021:  ldarg.1
    IL_0022:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0027:  brtrue     IL_00ad

    IL_002c:  ldarg.1
    IL_002d:  callvirt   instance class Verse.Map Verse.Thing::get_Map()
    IL_0032:  ldfld      class Verse.ListerThings Verse.Map::listerThings
    IL_0037:  ldc.i4.s   48
    IL_0039:  callvirt   instance class [mscorlib]System.Collections.Generic.List`1<class Verse.Thing> Verse.ListerThings::ThingsInGroup(valuetype Verse.ThingRequestGroup)
    IL_003e:  stloc.0
    IL_003f:  ldc.i4.0
    IL_0040:  stloc.2
    IL_0041:  br.s       IL_008f

    IL_0043:  ldarg.1
    IL_0044:  callvirt   instance valuetype Verse.IntVec3 Verse.Thing::get_Position()
    IL_0049:  stloc.s    V_4
    IL_004b:  ldloca.s   V_4
    IL_004d:  ldloc.0
    IL_004e:  ldloc.2
    IL_004f:  callvirt   instance !0 class [mscorlib]System.Collections.Generic.List`1<class Verse.Thing>::get_Item(int32)
    IL_0054:  callvirt   instance valuetype Verse.IntVec3 Verse.Thing::get_Position()
    IL_0059:  ldc.r4     18.
    IL_005e:  call       instance bool Verse.IntVec3::InHorDistOf(valuetype Verse.IntVec3,
                                                                  float32)
    IL_0063:  brfalse.s  IL_008b

    IL_0065:  ldloc.0
    IL_0066:  ldloc.2
    IL_0067:  callvirt   instance !0 class [mscorlib]System.Collections.Generic.List`1<class Verse.Thing>::get_Item(int32)
    IL_006c:  ldarg.1
    IL_006d:  ldc.i4.0
    IL_006e:  ldc.i4.0
    IL_006f:  call       bool RimWorld.FleeUtility::ShouldFleeFrom(class Verse.Thing,
                                                                   class Verse.Pawn,
                                                                   bool,
                                                                   bool)
    IL_0074:  brfalse.s  IL_008b

    IL_0076:  ldarg.1
    IL_0077:  ldloc.0
    IL_0078:  ldloc.2
    IL_0079:  callvirt   instance !0 class [mscorlib]System.Collections.Generic.List`1<class Verse.Thing>::get_Item(int32)
    IL_007e:  ldc.i4.s   24
    IL_0080:  call       class Verse.AI.Job RimWorld.FleeUtility::FleeJob(class Verse.Pawn,
                                                                          class Verse.Thing,
                                                                          int32)
    IL_0085:  stloc.3
    IL_0086:  ldloc.3
    IL_0087:  brfalse.s  IL_008b

    IL_0089:  ldloc.3
    IL_008a:  ret

    IL_008b:  ldloc.2
    IL_008c:  ldc.i4.1
    IL_008d:  add
    IL_008e:  stloc.2
    IL_008f:  ldloc.2
    IL_0090:  ldloc.0
    IL_0091:  callvirt   instance int32 class [mscorlib]System.Collections.Generic.List`1<class Verse.Thing>::get_Count()
    IL_0096:  blt.s      IL_0043

    IL_0098:  ldarg.1
    IL_0099:  ldc.i4.s   60
    IL_009b:  ldc.i4.s   10
    IL_009d:  ldc.i4.s   24
    IL_009f:  call       class Verse.AI.Job RimWorld.FleeUtility::FleeLargeFireJob(class Verse.Pawn,
                                                                                   int32,
                                                                                   int32,
                                                                                   int32)
    IL_00a4:  stloc.1
    IL_00a5:  ldloc.1
    IL_00a6:  brfalse    IL_014c

    IL_00ab:  ldloc.1
    IL_00ac:  ret

    IL_00ad:  ldarg.1
    IL_00ae:  call       bool RimWorld.FleeUtility::ShouldAnimalFleeDanger(class Verse.Pawn)
    IL_00b3:  brfalse    IL_014c

    IL_00b8:  ldarg.1
    IL_00b9:  callvirt   instance class Verse.Map Verse.Thing::get_Map()
    IL_00be:  ldfld      class Verse.AI.AttackTargetsCache Verse.Map::attackTargetsCache
    IL_00c3:  ldarg.1
    IL_00c4:  callvirt   instance class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::GetPotentialTargetsFor(class Verse.AI.IAttackTargetSearcher)
    IL_00c9:  stloc.s    V_5
    IL_00cb:  ldc.i4.0
    IL_00cc:  stloc.s    V_6
    IL_00ce:  br.s       IL_0141

    IL_00d0:  ldloc.s    V_5
    IL_00d2:  ldloc.s    V_6
    IL_00d4:  callvirt   instance !0 class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::get_Item(int32)
    IL_00d9:  callvirt   instance class Verse.Thing Verse.AI.IAttackTarget::get_Thing()
    IL_00de:  stloc.s    V_7
    IL_00e0:  ldarg.1
    IL_00e1:  callvirt   instance valuetype Verse.IntVec3 Verse.Thing::get_Position()
    IL_00e6:  stloc.s    V_4
    IL_00e8:  ldloca.s   V_4
    IL_00ea:  ldloc.s    V_7
    IL_00ec:  callvirt   instance valuetype Verse.IntVec3 Verse.Thing::get_Position()
    IL_00f1:  ldc.r4     18.
    IL_00f6:  call       instance bool Verse.IntVec3::InHorDistOf(valuetype Verse.IntVec3,
                                                                  float32)
    IL_00fb:  brfalse.s  IL_013b

    IL_00fd:  ldloc.s    V_7
    IL_00ff:  ldarg.1
    IL_0100:  ldc.i4.0
    IL_0101:  ldc.i4.1
    IL_0102:  call       bool RimWorld.FleeUtility::ShouldFleeFrom(class Verse.Thing,
                                                                   class Verse.Pawn,
                                                                   bool,
                                                                   bool)
    IL_0107:  brfalse.s  IL_013b

    IL_0109:  ldloc.s    V_7
    IL_010b:  isinst     Verse.Pawn
    IL_0110:  stloc.s    V_8
    IL_0112:  ldloc.s    V_8
    IL_0114:  brfalse.s  IL_0128

    IL_0116:  ldloc.s    V_8
    IL_0118:  call       bool Verse.WildManUtility::AnimalOrWildMan(class Verse.Pawn)
    IL_011d:  brfalse.s  IL_0128

    IL_011f:  ldloc.s    V_8
    IL_0121:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0126:  brfalse.s  IL_013b

    IL_0128:  ldarg.1
    IL_0129:  ldloc.s    V_7
    IL_012b:  ldc.i4.s   24
    IL_012d:  call       class Verse.AI.Job RimWorld.FleeUtility::FleeJob(class Verse.Pawn,
                                                                          class Verse.Thing,
                                                                          int32)
    IL_0132:  stloc.s    V_9
    IL_0134:  ldloc.s    V_9
    IL_0136:  brfalse.s  IL_013b

    IL_0138:  ldloc.s    V_9
    IL_013a:  ret

    IL_013b:  ldloc.s    V_6
    IL_013d:  ldc.i4.1
    IL_013e:  add
    IL_013f:  stloc.s    V_6
    IL_0141:  ldloc.s    V_6
    IL_0143:  ldloc.s    V_5
    IL_0145:  callvirt   instance int32 class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::get_Count()
    IL_014a:  blt.s      IL_00d0

    IL_014c:  ldnull
    IL_014d:  ret
  } // end of method JobGiver_AnimalFlee::TryGiveJob

} // end of class RimWorld.JobGiver_AnimalFlee

// *********** ДИЗАССЕМБЛИРОВАНИЕ ЗАВЕРШЕНО ***********************
// ВНИМАНИЕ: создан файл ресурсов Win32 codex-build\ildasm\JobGiver_AnimalFlee_TryGiveJob.res
