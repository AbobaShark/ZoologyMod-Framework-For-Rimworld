
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0



// предупреждение: ЭТО НЕПОЛНЫЙ ДИЗАССЕМБЛЕРНЫЙ КОД, НЕПРИГОДНЫЙ ДЛЯ ТРАНСЛЯЦИИ



.class public auto ansi beforefieldinit Verse.AI.AttackTargetsCache
       extends [mscorlib]System.Object
{
  .method public hidebysig instance class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> 
          GetPotentialTargetsFor(class Verse.AI.IAttackTargetSearcher th) cil managed
  {
    // Размер кода:       464 (0x1d0)
    .maxstack  3
    .locals init (class Verse.Thing V_0,
             class RimWorld.Faction V_1,
             class Verse.Pawn V_2,
             valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<class Verse.AI.IAttackTarget> V_3,
             class Verse.AI.IAttackTarget V_4,
             valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<class Verse.Pawn> V_5,
             class Verse.Pawn V_6,
             class Verse.Pawn V_7,
             class RimWorld.Faction V_8,
             class [mscorlib]System.Collections.Generic.List`1<class Verse.Pawn> V_9,
             int32 V_10,
             class RimWorld.Faction V_11,
             class [mscorlib]System.Collections.Generic.List`1<class Verse.Pawn> V_12,
             int32 V_13)
    IL_0000:  ldarg.1
    IL_0001:  callvirt   instance class Verse.Thing Verse.AI.IAttackTargetSearcher::get_Thing()
    IL_0006:  stloc.0
    IL_0007:  ldsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::targets
    IL_000c:  callvirt   instance void class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::Clear()
    IL_0011:  ldloc.0
    IL_0012:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0017:  stloc.1
    IL_0018:  ldloc.1
    IL_0019:  brfalse.s  IL_0067

    IL_001b:  ldarg.0
    IL_001c:  ldloc.1
    IL_001d:  call       instance class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::TargetsHostileToFaction(class RimWorld.Faction)
    IL_0022:  callvirt   instance valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<!0> class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>::GetEnumerator()
    IL_0027:  stloc.3
    .try
    {
      IL_0028:  br.s       IL_004e

      IL_002a:  ldloca.s   V_3
      IL_002c:  call       instance !0 valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<class Verse.AI.IAttackTarget>::get_Current()
      IL_0031:  stloc.s    V_4
      IL_0033:  ldloc.0
      IL_0034:  ldloc.s    V_4
      IL_0036:  callvirt   instance class Verse.Thing Verse.AI.IAttackTarget::get_Thing()
      IL_003b:  call       bool RimWorld.GenHostility::HostileTo(class Verse.Thing,
                                                                 class Verse.Thing)
      IL_0040:  brfalse.s  IL_004e

      IL_0042:  ldsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::targets
      IL_0047:  ldloc.s    V_4
      IL_0049:  callvirt   instance void class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::Add(!0)
      IL_004e:  ldloca.s   V_3
      IL_0050:  call       instance bool valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<class Verse.AI.IAttackTarget>::MoveNext()
      IL_0055:  brtrue.s   IL_002a

      IL_0057:  leave.s    IL_0067

    }  // end .try
    finally
    {
      IL_0059:  ldloca.s   V_3
      IL_005b:  constrained. valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<class Verse.AI.IAttackTarget>
      IL_0061:  callvirt   instance void [mscorlib]System.IDisposable::Dispose()
      IL_0066:  endfinally
    }  // end handler
    IL_0067:  ldarg.0
    IL_0068:  ldfld      class [System.Core]System.Collections.Generic.HashSet`1<class Verse.Pawn> Verse.AI.AttackTargetsCache::pawnsInAggroMentalState
    IL_006d:  callvirt   instance valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<!0> class [System.Core]System.Collections.Generic.HashSet`1<class Verse.Pawn>::GetEnumerator()
    IL_0072:  stloc.s    V_5
    .try
    {
      IL_0074:  br.s       IL_0095

      IL_0076:  ldloca.s   V_5
      IL_0078:  call       instance !0 valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<class Verse.Pawn>::get_Current()
      IL_007d:  stloc.s    V_6
      IL_007f:  ldloc.0
      IL_0080:  ldloc.s    V_6
      IL_0082:  call       bool RimWorld.GenHostility::HostileTo(class Verse.Thing,
                                                                 class Verse.Thing)
      IL_0087:  brfalse.s  IL_0095

      IL_0089:  ldsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::targets
      IL_008e:  ldloc.s    V_6
      IL_0090:  callvirt   instance void class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::Add(!0)
      IL_0095:  ldloca.s   V_5
      IL_0097:  call       instance bool valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<class Verse.Pawn>::MoveNext()
      IL_009c:  brtrue.s   IL_0076

      IL_009e:  leave.s    IL_00ae

    }  // end .try
    finally
    {
      IL_00a0:  ldloca.s   V_5
      IL_00a2:  constrained. valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<class Verse.Pawn>
      IL_00a8:  callvirt   instance void [mscorlib]System.IDisposable::Dispose()
      IL_00ad:  endfinally
    }  // end handler
    IL_00ae:  ldarg.0
    IL_00af:  ldfld      class [System.Core]System.Collections.Generic.HashSet`1<class Verse.Pawn> Verse.AI.AttackTargetsCache::factionlessHumanlikes
    IL_00b4:  callvirt   instance valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<!0> class [System.Core]System.Collections.Generic.HashSet`1<class Verse.Pawn>::GetEnumerator()
    IL_00b9:  stloc.s    V_5
    .try
    {
      IL_00bb:  br.s       IL_00dc

      IL_00bd:  ldloca.s   V_5
      IL_00bf:  call       instance !0 valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<class Verse.Pawn>::get_Current()
      IL_00c4:  stloc.s    V_7
      IL_00c6:  ldloc.0
      IL_00c7:  ldloc.s    V_7
      IL_00c9:  call       bool RimWorld.GenHostility::HostileTo(class Verse.Thing,
                                                                 class Verse.Thing)
      IL_00ce:  brfalse.s  IL_00dc

      IL_00d0:  ldsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::targets
      IL_00d5:  ldloc.s    V_7
      IL_00d7:  callvirt   instance void class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::Add(!0)
      IL_00dc:  ldloca.s   V_5
      IL_00de:  call       instance bool valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<class Verse.Pawn>::MoveNext()
      IL_00e3:  brtrue.s   IL_00bd

      IL_00e5:  leave.s    IL_00f5

    }  // end .try
    finally
    {
      IL_00e7:  ldloca.s   V_5
      IL_00e9:  constrained. valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<class Verse.Pawn>
      IL_00ef:  callvirt   instance void [mscorlib]System.IDisposable::Dispose()
      IL_00f4:  endfinally
    }  // end handler
    IL_00f5:  ldarg.1
    IL_00f6:  isinst     Verse.Pawn
    IL_00fb:  stloc.2
    IL_00fc:  ldloc.2
    IL_00fd:  brfalse.s  IL_0162

    IL_00ff:  ldloc.2
    IL_0100:  call       bool RimWorld.PrisonBreakUtility::IsPrisonBreaking(class Verse.Pawn)
    IL_0105:  brfalse.s  IL_0162

    IL_0107:  ldloc.2
    IL_0108:  ldfld      class RimWorld.Pawn_GuestTracker Verse.Pawn::guest
    IL_010d:  callvirt   instance class RimWorld.Faction RimWorld.Pawn_GuestTracker::get_HostFaction()
    IL_0112:  stloc.s    V_8
    IL_0114:  ldarg.0
    IL_0115:  ldfld      class Verse.Map Verse.AI.AttackTargetsCache::map
    IL_011a:  ldfld      class Verse.MapPawns Verse.Map::mapPawns
    IL_011f:  ldloc.s    V_8
    IL_0121:  callvirt   instance class [mscorlib]System.Collections.Generic.List`1<class Verse.Pawn> Verse.MapPawns::SpawnedPawnsInFaction(class RimWorld.Faction)
    IL_0126:  stloc.s    V_9
    IL_0128:  ldc.i4.0
    IL_0129:  stloc.s    V_10
    IL_012b:  br.s       IL_0157

    IL_012d:  ldloc.0
    IL_012e:  ldloc.s    V_9
    IL_0130:  ldloc.s    V_10
    IL_0132:  callvirt   instance !0 class [mscorlib]System.Collections.Generic.List`1<class Verse.Pawn>::get_Item(int32)
    IL_0137:  call       bool RimWorld.GenHostility::HostileTo(class Verse.Thing,
                                                               class Verse.Thing)
    IL_013c:  brfalse.s  IL_0151

    IL_013e:  ldsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::targets
    IL_0143:  ldloc.s    V_9
    IL_0145:  ldloc.s    V_10
    IL_0147:  callvirt   instance !0 class [mscorlib]System.Collections.Generic.List`1<class Verse.Pawn>::get_Item(int32)
    IL_014c:  callvirt   instance void class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::Add(!0)
    IL_0151:  ldloc.s    V_10
    IL_0153:  ldc.i4.1
    IL_0154:  add
    IL_0155:  stloc.s    V_10
    IL_0157:  ldloc.s    V_10
    IL_0159:  ldloc.s    V_9
    IL_015b:  callvirt   instance int32 class [mscorlib]System.Collections.Generic.List`1<class Verse.Pawn>::get_Count()
    IL_0160:  blt.s      IL_012d

    IL_0162:  ldloc.2
    IL_0163:  brfalse.s  IL_01ca

    IL_0165:  call       bool Verse.ModsConfig::get_IdeologyActive()
    IL_016a:  brfalse.s  IL_01ca

    IL_016c:  ldloc.2
    IL_016d:  call       bool RimWorld.SlaveRebellionUtility::IsRebelling(class Verse.Pawn)
    IL_0172:  brfalse.s  IL_01ca

    IL_0174:  ldloc.2
    IL_0175:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_017a:  stloc.s    V_11
    IL_017c:  ldarg.0
    IL_017d:  ldfld      class Verse.Map Verse.AI.AttackTargetsCache::map
    IL_0182:  ldfld      class Verse.MapPawns Verse.Map::mapPawns
    IL_0187:  ldloc.s    V_11
    IL_0189:  callvirt   instance class [mscorlib]System.Collections.Generic.List`1<class Verse.Pawn> Verse.MapPawns::SpawnedPawnsInFaction(class RimWorld.Faction)
    IL_018e:  stloc.s    V_12
    IL_0190:  ldc.i4.0
    IL_0191:  stloc.s    V_13
    IL_0193:  br.s       IL_01bf

    IL_0195:  ldloc.0
    IL_0196:  ldloc.s    V_12
    IL_0198:  ldloc.s    V_13
    IL_019a:  callvirt   instance !0 class [mscorlib]System.Collections.Generic.List`1<class Verse.Pawn>::get_Item(int32)
    IL_019f:  call       bool RimWorld.GenHostility::HostileTo(class Verse.Thing,
                                                               class Verse.Thing)
    IL_01a4:  brfalse.s  IL_01b9

    IL_01a6:  ldsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::targets
    IL_01ab:  ldloc.s    V_12
    IL_01ad:  ldloc.s    V_13
    IL_01af:  callvirt   instance !0 class [mscorlib]System.Collections.Generic.List`1<class Verse.Pawn>::get_Item(int32)
    IL_01b4:  callvirt   instance void class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::Add(!0)
    IL_01b9:  ldloc.s    V_13
    IL_01bb:  ldc.i4.1
    IL_01bc:  add
    IL_01bd:  stloc.s    V_13
    IL_01bf:  ldloc.s    V_13
    IL_01c1:  ldloc.s    V_12
    IL_01c3:  callvirt   instance int32 class [mscorlib]System.Collections.Generic.List`1<class Verse.Pawn>::get_Count()
    IL_01c8:  blt.s      IL_0195

    IL_01ca:  ldsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::targets
    IL_01cf:  ret
  } // end of method AttackTargetsCache::GetPotentialTargetsFor

} // end of class Verse.AI.AttackTargetsCache

// *********** ДИЗАССЕМБЛИРОВАНИЕ ЗАВЕРШЕНО ***********************
// ВНИМАНИЕ: создан файл ресурсов Win32 codex-build\ildasm\AttackTargetsCache_GetPotentialTargetsFor.res
