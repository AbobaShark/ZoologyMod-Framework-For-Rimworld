
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0



// предупреждение: ЭТО НЕПОЛНЫЙ ДИЗАССЕМБЛЕРНЫЙ КОД, НЕПРИГОДНЫЙ ДЛЯ ТРАНСЛЯЦИИ



.class public auto ansi beforefieldinit Verse.AI.AttackTargetsCache
       extends [mscorlib]System.Object
{
  .field private class Verse.Map map
  .field private class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> allTargets
  .field private class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>> targetsHostileToFaction
  .field private class [System.Core]System.Collections.Generic.HashSet`1<class Verse.Pawn> pawnsInAggroMentalState
  .field private class [System.Core]System.Collections.Generic.HashSet`1<class Verse.Pawn> factionlessHumanlikes
  .field private static class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> targets
  .field private static initonly class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> emptySet
  .field private static initonly class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> tmpTargets
  .field private static initonly class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> tmpToUpdate
  .method public hidebysig specialname instance class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> 
          get_TargetsHostileToColony() cil managed
  {
    // Размер кода:       12 (0xc)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  call       class RimWorld.Faction RimWorld.Faction::get_OfPlayer()
    IL_0006:  call       instance class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::TargetsHostileToFaction(class RimWorld.Faction)
    IL_000b:  ret
  } // end of method AttackTargetsCache::get_TargetsHostileToColony

  .method public hidebysig specialname rtspecialname 
          instance void  .ctor(class Verse.Map map) cil managed
  {
    // Размер кода:       104 (0x68)
    .maxstack  3
    IL_0000:  ldarg.0
    IL_0001:  newobj     instance void class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>::.ctor()
    IL_0006:  stfld      class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::allTargets
    IL_000b:  ldarg.0
    IL_000c:  newobj     instance void class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>>::.ctor()
    IL_0011:  stfld      class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>> Verse.AI.AttackTargetsCache::targetsHostileToFaction
    IL_0016:  ldarg.0
    IL_0017:  newobj     instance void class [System.Core]System.Collections.Generic.HashSet`1<class Verse.Pawn>::.ctor()
    IL_001c:  stfld      class [System.Core]System.Collections.Generic.HashSet`1<class Verse.Pawn> Verse.AI.AttackTargetsCache::pawnsInAggroMentalState
    IL_0021:  ldarg.0
    IL_0022:  newobj     instance void class [System.Core]System.Collections.Generic.HashSet`1<class Verse.Pawn>::.ctor()
    IL_0027:  stfld      class [System.Core]System.Collections.Generic.HashSet`1<class Verse.Pawn> Verse.AI.AttackTargetsCache::factionlessHumanlikes
    IL_002c:  ldarg.0
    IL_002d:  call       instance void [mscorlib]System.Object::.ctor()
    IL_0032:  ldarg.0
    IL_0033:  ldarg.1
    IL_0034:  stfld      class Verse.Map Verse.AI.AttackTargetsCache::map
    IL_0039:  ldarg.1
    IL_003a:  ldfld      class Verse.MapEvents Verse.Map::events
    IL_003f:  ldarg.0
    IL_0040:  ldftn      instance void Verse.AI.AttackTargetsCache::Notify_FactionAdded(class RimWorld.Faction)
    IL_0046:  newobj     instance void class [mscorlib]System.Action`1<class RimWorld.Faction>::.ctor(object,
                                                                                                      native int)
    IL_004b:  callvirt   instance void Verse.MapEvents::add_FactionAdded(class [mscorlib]System.Action`1<class RimWorld.Faction>)
    IL_0050:  ldarg.1
    IL_0051:  ldfld      class Verse.MapEvents Verse.Map::events
    IL_0056:  ldarg.0
    IL_0057:  ldftn      instance void Verse.AI.AttackTargetsCache::Notify_FactionRemoved(class RimWorld.Faction)
    IL_005d:  newobj     instance void class [mscorlib]System.Action`1<class RimWorld.Faction>::.ctor(object,
                                                                                                      native int)
    IL_0062:  callvirt   instance void Verse.MapEvents::add_FactionRemoved(class [mscorlib]System.Action`1<class RimWorld.Faction>)
    IL_0067:  ret
  } // end of method AttackTargetsCache::.ctor

  .method public hidebysig static void  AttackTargetsCacheStaticUpdate() cil managed
  {
    // Размер кода:       11 (0xb)
    .maxstack  8
    IL_0000:  ldsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::targets
    IL_0005:  callvirt   instance void class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::Clear()
    IL_000a:  ret
  } // end of method AttackTargetsCache::AttackTargetsCacheStaticUpdate

  .method public hidebysig instance void 
          UpdateTarget(class Verse.AI.IAttackTarget t) cil managed
  {
    // Размер кода:       59 (0x3b)
    .maxstack  2
    .locals init (class Verse.Thing V_0)
    IL_0000:  ldarg.0
    IL_0001:  ldfld      class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::allTargets
    IL_0006:  ldarg.1
    IL_0007:  callvirt   instance bool class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>::Contains(!0)
    IL_000c:  brtrue.s   IL_000f

    IL_000e:  ret

    IL_000f:  ldarg.0
    IL_0010:  ldarg.1
    IL_0011:  call       instance void Verse.AI.AttackTargetsCache::DeregisterTarget(class Verse.AI.IAttackTarget)
    IL_0016:  ldarg.1
    IL_0017:  callvirt   instance class Verse.Thing Verse.AI.IAttackTarget::get_Thing()
    IL_001c:  stloc.0
    IL_001d:  ldloc.0
    IL_001e:  callvirt   instance bool Verse.Thing::get_Spawned()
    IL_0023:  brfalse.s  IL_003a

    IL_0025:  ldloc.0
    IL_0026:  callvirt   instance class Verse.Map Verse.Thing::get_Map()
    IL_002b:  ldarg.0
    IL_002c:  ldfld      class Verse.Map Verse.AI.AttackTargetsCache::map
    IL_0031:  bne.un.s   IL_003a

    IL_0033:  ldarg.0
    IL_0034:  ldarg.1
    IL_0035:  call       instance void Verse.AI.AttackTargetsCache::RegisterTarget(class Verse.AI.IAttackTarget)
    IL_003a:  ret
  } // end of method AttackTargetsCache::UpdateTarget

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

  .method public hidebysig instance class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> 
          TargetsHostileToFaction(class RimWorld.Faction f) cil managed
  {
    // Размер кода:       43 (0x2b)
    .maxstack  3
    .locals init (class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> V_0)
    IL_0000:  ldarg.1
    IL_0001:  brtrue.s   IL_0013

    IL_0003:  ldstr      "Called TargetsHostileToFaction with null faction."
    IL_0008:  call       void Verse.Log::Warning(string)
    IL_000d:  ldsfld     class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::emptySet
    IL_0012:  ret

    IL_0013:  ldarg.0
    IL_0014:  ldfld      class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>> Verse.AI.AttackTargetsCache::targetsHostileToFaction
    IL_0019:  ldarg.1
    IL_001a:  ldloca.s   V_0
    IL_001c:  callvirt   instance bool class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>>::TryGetValue(!0,
                                                                                                                                                                                                                          !1&)
    IL_0021:  brfalse.s  IL_0025

    IL_0023:  ldloc.0
    IL_0024:  ret

    IL_0025:  ldsfld     class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::emptySet
    IL_002a:  ret
  } // end of method AttackTargetsCache::TargetsHostileToFaction

  .method public hidebysig instance void 
          Notify_ThingSpawned(class Verse.Thing th) cil managed
  {
    // Размер кода:       18 (0x12)
    .maxstack  2
    .locals init (class Verse.AI.IAttackTarget V_0)
    IL_0000:  ldarg.1
    IL_0001:  isinst     Verse.AI.IAttackTarget
    IL_0006:  stloc.0
    IL_0007:  ldloc.0
    IL_0008:  brfalse.s  IL_0011

    IL_000a:  ldarg.0
    IL_000b:  ldloc.0
    IL_000c:  call       instance void Verse.AI.AttackTargetsCache::RegisterTarget(class Verse.AI.IAttackTarget)
    IL_0011:  ret
  } // end of method AttackTargetsCache::Notify_ThingSpawned

  .method public hidebysig instance void 
          Notify_ThingDespawned(class Verse.Thing th) cil managed
  {
    // Размер кода:       18 (0x12)
    .maxstack  2
    .locals init (class Verse.AI.IAttackTarget V_0)
    IL_0000:  ldarg.1
    IL_0001:  isinst     Verse.AI.IAttackTarget
    IL_0006:  stloc.0
    IL_0007:  ldloc.0
    IL_0008:  brfalse.s  IL_0011

    IL_000a:  ldarg.0
    IL_000b:  ldloc.0
    IL_000c:  call       instance void Verse.AI.AttackTargetsCache::DeregisterTarget(class Verse.AI.IAttackTarget)
    IL_0011:  ret
  } // end of method AttackTargetsCache::Notify_ThingDespawned

  .method public hidebysig instance void 
          Notify_FactionHostilityChanged(class RimWorld.Faction f1,
                                         class RimWorld.Faction f2) cil managed
  {
    // Размер кода:       178 (0xb2)
    .maxstack  3
    .locals init (valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<class Verse.AI.IAttackTarget> V_0,
             class Verse.AI.IAttackTarget V_1,
             class Verse.Thing V_2,
             class Verse.Pawn V_3,
             int32 V_4)
    IL_0000:  ldsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::tmpTargets
    IL_0005:  callvirt   instance void class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::Clear()
    IL_000a:  ldarg.0
    IL_000b:  ldfld      class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::allTargets
    IL_0010:  callvirt   instance valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<!0> class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>::GetEnumerator()
    IL_0015:  stloc.0
    .try
    {
      IL_0016:  br.s       IL_0063

      IL_0018:  ldloca.s   V_0
      IL_001a:  call       instance !0 valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<class Verse.AI.IAttackTarget>::get_Current()
      IL_001f:  stloc.1
      IL_0020:  ldloc.1
      IL_0021:  callvirt   instance class Verse.Thing Verse.AI.IAttackTarget::get_Thing()
      IL_0026:  stloc.2
      IL_0027:  ldloc.2
      IL_0028:  isinst     Verse.Pawn
      IL_002d:  stloc.3
      IL_002e:  ldloc.2
      IL_002f:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
      IL_0034:  ldarg.1
      IL_0035:  beq.s      IL_0058

      IL_0037:  ldloc.2
      IL_0038:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
      IL_003d:  ldarg.2
      IL_003e:  beq.s      IL_0058

      IL_0040:  ldloc.3
      IL_0041:  brfalse.s  IL_004c

      IL_0043:  ldloc.3
      IL_0044:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
      IL_0049:  ldarg.1
      IL_004a:  beq.s      IL_0058

      IL_004c:  ldloc.3
      IL_004d:  brfalse.s  IL_0063

      IL_004f:  ldloc.3
      IL_0050:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
      IL_0055:  ldarg.2
      IL_0056:  bne.un.s   IL_0063

      IL_0058:  ldsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::tmpTargets
      IL_005d:  ldloc.1
      IL_005e:  callvirt   instance void class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::Add(!0)
      IL_0063:  ldloca.s   V_0
      IL_0065:  call       instance bool valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<class Verse.AI.IAttackTarget>::MoveNext()
      IL_006a:  brtrue.s   IL_0018

      IL_006c:  leave.s    IL_007c

    }  // end .try
    finally
    {
      IL_006e:  ldloca.s   V_0
      IL_0070:  constrained. valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<class Verse.AI.IAttackTarget>
      IL_0076:  callvirt   instance void [mscorlib]System.IDisposable::Dispose()
      IL_007b:  endfinally
    }  // end handler
    IL_007c:  ldc.i4.0
    IL_007d:  stloc.s    V_4
    IL_007f:  br.s       IL_0099

    IL_0081:  ldarg.0
    IL_0082:  ldsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::tmpTargets
    IL_0087:  ldloc.s    V_4
    IL_0089:  callvirt   instance !0 class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::get_Item(int32)
    IL_008e:  call       instance void Verse.AI.AttackTargetsCache::UpdateTarget(class Verse.AI.IAttackTarget)
    IL_0093:  ldloc.s    V_4
    IL_0095:  ldc.i4.1
    IL_0096:  add
    IL_0097:  stloc.s    V_4
    IL_0099:  ldloc.s    V_4
    IL_009b:  ldsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::tmpTargets
    IL_00a0:  callvirt   instance int32 class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::get_Count()
    IL_00a5:  blt.s      IL_0081

    IL_00a7:  ldsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::tmpTargets
    IL_00ac:  callvirt   instance void class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::Clear()
    IL_00b1:  ret
  } // end of method AttackTargetsCache::Notify_FactionHostilityChanged

  .method private hidebysig instance void 
          RegisterTarget(class Verse.AI.IAttackTarget target) cil managed
  {
    // Размер кода:       350 (0x15e)
    .maxstack  5
    .locals init (class Verse.Thing V_0,
             class [mscorlib]System.Collections.Generic.List`1<class RimWorld.Faction> V_1,
             class Verse.Pawn V_2,
             int32 V_3)
    IL_0000:  ldarg.0
    IL_0001:  ldfld      class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::allTargets
    IL_0006:  ldarg.1
    IL_0007:  callvirt   instance bool class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>::Contains(!0)
    IL_000c:  brfalse.s  IL_003b

    IL_000e:  ldstr      "Tried to register the same target twice "
    IL_0013:  ldarg.1
    IL_0014:  call       string Verse.Gen::ToStringSafe<class Verse.AI.IAttackTarget>(!!0)
    IL_0019:  ldstr      " in "
    IL_001e:  ldarg.0
    IL_001f:  call       instance class [mscorlib]System.Type [mscorlib]System.Object::GetType()
    IL_0024:  dup
    IL_0025:  brtrue.s   IL_002b

    IL_0027:  pop
    IL_0028:  ldnull
    IL_0029:  br.s       IL_0030

    IL_002b:  callvirt   instance string [mscorlib]System.Object::ToString()
    IL_0030:  call       string [mscorlib]System.String::Concat(string,
                                                                string,
                                                                string,
                                                                string)
    IL_0035:  call       void Verse.Log::Warning(string)
    IL_003a:  ret

    IL_003b:  ldarg.1
    IL_003c:  callvirt   instance class Verse.Thing Verse.AI.IAttackTarget::get_Thing()
    IL_0041:  stloc.0
    IL_0042:  ldloc.0
    IL_0043:  callvirt   instance bool Verse.Thing::get_Spawned()
    IL_0048:  brtrue.s   IL_0077

    IL_004a:  ldstr      "Tried to register unspawned thing "
    IL_004f:  ldloc.0
    IL_0050:  call       string Verse.Gen::ToStringSafe<class Verse.Thing>(!!0)
    IL_0055:  ldstr      " in "
    IL_005a:  ldarg.0
    IL_005b:  call       instance class [mscorlib]System.Type [mscorlib]System.Object::GetType()
    IL_0060:  dup
    IL_0061:  brtrue.s   IL_0067

    IL_0063:  pop
    IL_0064:  ldnull
    IL_0065:  br.s       IL_006c

    IL_0067:  callvirt   instance string [mscorlib]System.Object::ToString()
    IL_006c:  call       string [mscorlib]System.String::Concat(string,
                                                                string,
                                                                string,
                                                                string)
    IL_0071:  call       void Verse.Log::Warning(string)
    IL_0076:  ret

    IL_0077:  ldloc.0
    IL_0078:  callvirt   instance class Verse.Map Verse.Thing::get_Map()
    IL_007d:  ldarg.0
    IL_007e:  ldfld      class Verse.Map Verse.AI.AttackTargetsCache::map
    IL_0083:  beq.s      IL_00a0

    IL_0085:  ldstr      "Tried to register attack target "
    IL_008a:  ldloc.0
    IL_008b:  call       string Verse.Gen::ToStringSafe<class Verse.Thing>(!!0)
    IL_0090:  ldstr      " but its Map is not this one."
    IL_0095:  call       string [mscorlib]System.String::Concat(string,
                                                                string,
                                                                string)
    IL_009a:  call       void Verse.Log::Warning(string)
    IL_009f:  ret

    IL_00a0:  ldarg.0
    IL_00a1:  ldfld      class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::allTargets
    IL_00a6:  ldarg.1
    IL_00a7:  callvirt   instance bool class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>::Add(!0)
    IL_00ac:  pop
    IL_00ad:  call       class RimWorld.FactionManager Verse.Find::get_FactionManager()
    IL_00b2:  callvirt   instance class [mscorlib]System.Collections.Generic.List`1<class RimWorld.Faction> RimWorld.FactionManager::get_AllFactionsListForReading()
    IL_00b7:  stloc.1
    IL_00b8:  ldc.i4.0
    IL_00b9:  stloc.3
    IL_00ba:  br.s       IL_0113

    IL_00bc:  ldloc.0
    IL_00bd:  ldloc.1
    IL_00be:  ldloc.3
    IL_00bf:  callvirt   instance !0 class [mscorlib]System.Collections.Generic.List`1<class RimWorld.Faction>::get_Item(int32)
    IL_00c4:  call       bool RimWorld.GenHostility::HostileTo(class Verse.Thing,
                                                               class RimWorld.Faction)
    IL_00c9:  brfalse.s  IL_010f

    IL_00cb:  ldarg.0
    IL_00cc:  ldfld      class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>> Verse.AI.AttackTargetsCache::targetsHostileToFaction
    IL_00d1:  ldloc.1
    IL_00d2:  ldloc.3
    IL_00d3:  callvirt   instance !0 class [mscorlib]System.Collections.Generic.List`1<class RimWorld.Faction>::get_Item(int32)
    IL_00d8:  callvirt   instance bool class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>>::ContainsKey(!0)
    IL_00dd:  brtrue.s   IL_00f6

    IL_00df:  ldarg.0
    IL_00e0:  ldfld      class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>> Verse.AI.AttackTargetsCache::targetsHostileToFaction
    IL_00e5:  ldloc.1
    IL_00e6:  ldloc.3
    IL_00e7:  callvirt   instance !0 class [mscorlib]System.Collections.Generic.List`1<class RimWorld.Faction>::get_Item(int32)
    IL_00ec:  newobj     instance void class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>::.ctor()
    IL_00f1:  callvirt   instance void class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>>::Add(!0,
                                                                                                                                                                                                                  !1)
    IL_00f6:  ldarg.0
    IL_00f7:  ldfld      class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>> Verse.AI.AttackTargetsCache::targetsHostileToFaction
    IL_00fc:  ldloc.1
    IL_00fd:  ldloc.3
    IL_00fe:  callvirt   instance !0 class [mscorlib]System.Collections.Generic.List`1<class RimWorld.Faction>::get_Item(int32)
    IL_0103:  callvirt   instance !1 class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>>::get_Item(!0)
    IL_0108:  ldarg.1
    IL_0109:  callvirt   instance bool class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>::Add(!0)
    IL_010e:  pop
    IL_010f:  ldloc.3
    IL_0110:  ldc.i4.1
    IL_0111:  add
    IL_0112:  stloc.3
    IL_0113:  ldloc.3
    IL_0114:  ldloc.1
    IL_0115:  callvirt   instance int32 class [mscorlib]System.Collections.Generic.List`1<class RimWorld.Faction>::get_Count()
    IL_011a:  blt.s      IL_00bc

    IL_011c:  ldarg.1
    IL_011d:  isinst     Verse.Pawn
    IL_0122:  stloc.2
    IL_0123:  ldloc.2
    IL_0124:  brfalse.s  IL_015d

    IL_0126:  ldloc.2
    IL_0127:  callvirt   instance bool Verse.Pawn::get_InAggroMentalState()
    IL_012c:  brfalse.s  IL_013b

    IL_012e:  ldarg.0
    IL_012f:  ldfld      class [System.Core]System.Collections.Generic.HashSet`1<class Verse.Pawn> Verse.AI.AttackTargetsCache::pawnsInAggroMentalState
    IL_0134:  ldloc.2
    IL_0135:  callvirt   instance bool class [System.Core]System.Collections.Generic.HashSet`1<class Verse.Pawn>::Add(!0)
    IL_013a:  pop
    IL_013b:  ldloc.2
    IL_013c:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0141:  brtrue.s   IL_015d

    IL_0143:  ldloc.2
    IL_0144:  callvirt   instance class Verse.RaceProperties Verse.Pawn::get_RaceProps()
    IL_0149:  callvirt   instance bool Verse.RaceProperties::get_Humanlike()
    IL_014e:  brfalse.s  IL_015d

    IL_0150:  ldarg.0
    IL_0151:  ldfld      class [System.Core]System.Collections.Generic.HashSet`1<class Verse.Pawn> Verse.AI.AttackTargetsCache::factionlessHumanlikes
    IL_0156:  ldloc.2
    IL_0157:  callvirt   instance bool class [System.Core]System.Collections.Generic.HashSet`1<class Verse.Pawn>::Add(!0)
    IL_015c:  pop
    IL_015d:  ret
  } // end of method AttackTargetsCache::RegisterTarget

  .method private hidebysig instance void 
          DeregisterTarget(class Verse.AI.IAttackTarget target) cil managed
  {
    // Размер кода:       177 (0xb1)
    .maxstack  5
    .locals init (class Verse.Pawn V_0,
             valuetype [mscorlib]System.Collections.Generic.Dictionary`2/Enumerator<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>> V_1,
             valuetype [mscorlib]System.Collections.Generic.KeyValuePair`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>> V_2)
    IL_0000:  ldarg.0
    IL_0001:  ldfld      class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::allTargets
    IL_0006:  ldarg.1
    IL_0007:  callvirt   instance bool class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>::Contains(!0)
    IL_000c:  brtrue.s   IL_0042

    IL_000e:  ldstr      "Tried to deregister "
    IL_0013:  ldarg.1
    IL_0014:  dup
    IL_0015:  brtrue.s   IL_001b

    IL_0017:  pop
    IL_0018:  ldnull
    IL_0019:  br.s       IL_0020

    IL_001b:  callvirt   instance string [mscorlib]System.Object::ToString()
    IL_0020:  ldstr      " but it's not in "
    IL_0025:  ldarg.0
    IL_0026:  call       instance class [mscorlib]System.Type [mscorlib]System.Object::GetType()
    IL_002b:  dup
    IL_002c:  brtrue.s   IL_0032

    IL_002e:  pop
    IL_002f:  ldnull
    IL_0030:  br.s       IL_0037

    IL_0032:  callvirt   instance string [mscorlib]System.Object::ToString()
    IL_0037:  call       string [mscorlib]System.String::Concat(string,
                                                                string,
                                                                string,
                                                                string)
    IL_003c:  call       void Verse.Log::Warning(string)
    IL_0041:  ret

    IL_0042:  ldarg.0
    IL_0043:  ldfld      class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::allTargets
    IL_0048:  ldarg.1
    IL_0049:  callvirt   instance bool class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>::Remove(!0)
    IL_004e:  pop
    IL_004f:  ldarg.0
    IL_0050:  ldfld      class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>> Verse.AI.AttackTargetsCache::targetsHostileToFaction
    IL_0055:  callvirt   instance valuetype [mscorlib]System.Collections.Generic.Dictionary`2/Enumerator<!0,!1> class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>>::GetEnumerator()
    IL_005a:  stloc.1
    .try
    {
      IL_005b:  br.s       IL_0073

      IL_005d:  ldloca.s   V_1
      IL_005f:  call       instance valuetype [mscorlib]System.Collections.Generic.KeyValuePair`2<!0,!1> valuetype [mscorlib]System.Collections.Generic.Dictionary`2/Enumerator<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>>::get_Current()
      IL_0064:  stloc.2
      IL_0065:  ldloca.s   V_2
      IL_0067:  call       instance !1 valuetype [mscorlib]System.Collections.Generic.KeyValuePair`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>>::get_Value()
      IL_006c:  ldarg.1
      IL_006d:  callvirt   instance bool class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>::Remove(!0)
      IL_0072:  pop
      IL_0073:  ldloca.s   V_1
      IL_0075:  call       instance bool valuetype [mscorlib]System.Collections.Generic.Dictionary`2/Enumerator<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>>::MoveNext()
      IL_007a:  brtrue.s   IL_005d

      IL_007c:  leave.s    IL_008c

    }  // end .try
    finally
    {
      IL_007e:  ldloca.s   V_1
      IL_0080:  constrained. valuetype [mscorlib]System.Collections.Generic.Dictionary`2/Enumerator<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>>
      IL_0086:  callvirt   instance void [mscorlib]System.IDisposable::Dispose()
      IL_008b:  endfinally
    }  // end handler
    IL_008c:  ldarg.1
    IL_008d:  isinst     Verse.Pawn
    IL_0092:  stloc.0
    IL_0093:  ldloc.0
    IL_0094:  brfalse.s  IL_00b0

    IL_0096:  ldarg.0
    IL_0097:  ldfld      class [System.Core]System.Collections.Generic.HashSet`1<class Verse.Pawn> Verse.AI.AttackTargetsCache::pawnsInAggroMentalState
    IL_009c:  ldloc.0
    IL_009d:  callvirt   instance bool class [System.Core]System.Collections.Generic.HashSet`1<class Verse.Pawn>::Remove(!0)
    IL_00a2:  pop
    IL_00a3:  ldarg.0
    IL_00a4:  ldfld      class [System.Core]System.Collections.Generic.HashSet`1<class Verse.Pawn> Verse.AI.AttackTargetsCache::factionlessHumanlikes
    IL_00a9:  ldloc.0
    IL_00aa:  callvirt   instance bool class [System.Core]System.Collections.Generic.HashSet`1<class Verse.Pawn>::Remove(!0)
    IL_00af:  pop
    IL_00b0:  ret
  } // end of method AttackTargetsCache::DeregisterTarget

  .method private hidebysig instance void 
          Notify_FactionAdded(class RimWorld.Faction faction) cil managed
  {
    // Размер кода:       32 (0x20)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldfld      class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>> Verse.AI.AttackTargetsCache::targetsHostileToFaction
    IL_0006:  ldarg.1
    IL_0007:  callvirt   instance bool class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>>::ContainsKey(!0)
    IL_000c:  brtrue.s   IL_001f

    IL_000e:  ldarg.0
    IL_000f:  ldfld      class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>> Verse.AI.AttackTargetsCache::targetsHostileToFaction
    IL_0014:  ldarg.1
    IL_0015:  newobj     instance void class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>::.ctor()
    IL_001a:  callvirt   instance void class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>>::Add(!0,
                                                                                                                                                                                                                  !1)
    IL_001f:  ret
  } // end of method AttackTargetsCache::Notify_FactionAdded

  .method private hidebysig instance void 
          Notify_FactionRemoved(class RimWorld.Faction faction) cil managed
  {
    // Размер кода:       28 (0x1c)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldfld      class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>> Verse.AI.AttackTargetsCache::targetsHostileToFaction
    IL_0006:  ldarg.1
    IL_0007:  callvirt   instance bool class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>>::ContainsKey(!0)
    IL_000c:  brfalse.s  IL_001b

    IL_000e:  ldarg.0
    IL_000f:  ldfld      class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>> Verse.AI.AttackTargetsCache::targetsHostileToFaction
    IL_0014:  ldarg.1
    IL_0015:  callvirt   instance bool class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>>::Remove(!0)
    IL_001a:  pop
    IL_001b:  ret
  } // end of method AttackTargetsCache::Notify_FactionRemoved

  .method private hidebysig instance void 
          Debug_AssertHostile(class RimWorld.Faction f,
                              class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> targets) cil managed
  {
    // Размер кода:       206 (0xce)
    .maxstack  4
    .locals init (valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<class Verse.AI.IAttackTarget> V_0,
             class Verse.AI.IAttackTarget V_1,
             int32 V_2)
    IL_0000:  ldsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::tmpToUpdate
    IL_0005:  callvirt   instance void class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::Clear()
    IL_000a:  ldarg.2
    IL_000b:  callvirt   instance valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<!0> class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>::GetEnumerator()
    IL_0010:  stloc.0
    .try
    {
      IL_0011:  br.s       IL_0084

      IL_0013:  ldloca.s   V_0
      IL_0015:  call       instance !0 valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<class Verse.AI.IAttackTarget>::get_Current()
      IL_001a:  stloc.1
      IL_001b:  ldloc.1
      IL_001c:  callvirt   instance class Verse.Thing Verse.AI.IAttackTarget::get_Thing()
      IL_0021:  ldarg.1
      IL_0022:  call       bool RimWorld.GenHostility::HostileTo(class Verse.Thing,
                                                                 class RimWorld.Faction)
      IL_0027:  brtrue.s   IL_0084

      IL_0029:  ldsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::tmpToUpdate
      IL_002e:  ldloc.1
      IL_002f:  callvirt   instance void class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::Add(!0)
      IL_0034:  ldc.i4.7
      IL_0035:  newarr     [mscorlib]System.String
      IL_003a:  dup
      IL_003b:  ldc.i4.0
      IL_003c:  ldstr      "Target "
      IL_0041:  stelem.ref
      IL_0042:  dup
      IL_0043:  ldc.i4.1
      IL_0044:  ldloc.1
      IL_0045:  call       string Verse.Gen::ToStringSafe<class Verse.AI.IAttackTarget>(!!0)
      IL_004a:  stelem.ref
      IL_004b:  dup
      IL_004c:  ldc.i4.2
      IL_004d:  ldstr      " is not hostile to "
      IL_0052:  stelem.ref
      IL_0053:  dup
      IL_0054:  ldc.i4.3
      IL_0055:  ldarg.1
      IL_0056:  call       string Verse.Gen::ToStringSafe<class RimWorld.Faction>(!!0)
      IL_005b:  stelem.ref
      IL_005c:  dup
      IL_005d:  ldc.i4.4
      IL_005e:  ldstr      " (in "
      IL_0063:  stelem.ref
      IL_0064:  dup
      IL_0065:  ldc.i4.5
      IL_0066:  ldarg.0
      IL_0067:  call       instance class [mscorlib]System.Type [mscorlib]System.Object::GetType()
      IL_006c:  callvirt   instance string [mscorlib]System.Reflection.MemberInfo::get_Name()
      IL_0071:  stelem.ref
      IL_0072:  dup
      IL_0073:  ldc.i4.6
      IL_0074:  ldstr      ") but it's in the list (forgot to update the targe"
      + "t somewhere\?). Trying to update the target..."
      IL_0079:  stelem.ref
      IL_007a:  call       string [mscorlib]System.String::Concat(string[])
      IL_007f:  call       void Verse.Log::Error(string)
      IL_0084:  ldloca.s   V_0
      IL_0086:  call       instance bool valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<class Verse.AI.IAttackTarget>::MoveNext()
      IL_008b:  brtrue.s   IL_0013

      IL_008d:  leave.s    IL_009d

    }  // end .try
    finally
    {
      IL_008f:  ldloca.s   V_0
      IL_0091:  constrained. valuetype [System.Core]System.Collections.Generic.HashSet`1/Enumerator<class Verse.AI.IAttackTarget>
      IL_0097:  callvirt   instance void [mscorlib]System.IDisposable::Dispose()
      IL_009c:  endfinally
    }  // end handler
    IL_009d:  ldc.i4.0
    IL_009e:  stloc.2
    IL_009f:  br.s       IL_00b6

    IL_00a1:  ldarg.0
    IL_00a2:  ldsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::tmpToUpdate
    IL_00a7:  ldloc.2
    IL_00a8:  callvirt   instance !0 class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::get_Item(int32)
    IL_00ad:  call       instance void Verse.AI.AttackTargetsCache::UpdateTarget(class Verse.AI.IAttackTarget)
    IL_00b2:  ldloc.2
    IL_00b3:  ldc.i4.1
    IL_00b4:  add
    IL_00b5:  stloc.2
    IL_00b6:  ldloc.2
    IL_00b7:  ldsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::tmpToUpdate
    IL_00bc:  callvirt   instance int32 class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::get_Count()
    IL_00c1:  blt.s      IL_00a1

    IL_00c3:  ldsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::tmpToUpdate
    IL_00c8:  callvirt   instance void class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::Clear()
    IL_00cd:  ret
  } // end of method AttackTargetsCache::Debug_AssertHostile

  .method public hidebysig instance bool 
          Debug_CheckIfInAllTargets(class Verse.AI.IAttackTarget t) cil managed
  {
    // Размер кода:       18 (0x12)
    .maxstack  8
    IL_0000:  ldarg.1
    IL_0001:  brfalse.s  IL_0010

    IL_0003:  ldarg.0
    IL_0004:  ldfld      class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::allTargets
    IL_0009:  ldarg.1
    IL_000a:  callvirt   instance bool class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>::Contains(!0)
    IL_000f:  ret

    IL_0010:  ldc.i4.0
    IL_0011:  ret
  } // end of method AttackTargetsCache::Debug_CheckIfInAllTargets

  .method public hidebysig instance bool 
          Debug_CheckIfHostileToFaction(class RimWorld.Faction f,
                                        class Verse.AI.IAttackTarget t) cil managed
  {
    // Размер кода:       29 (0x1d)
    .maxstack  8
    IL_0000:  ldarg.1
    IL_0001:  brtrue.s   IL_0005

    IL_0003:  ldc.i4.0
    IL_0004:  ret

    IL_0005:  ldarg.2
    IL_0006:  brfalse.s  IL_001b

    IL_0008:  ldarg.0
    IL_0009:  ldfld      class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>> Verse.AI.AttackTargetsCache::targetsHostileToFaction
    IL_000e:  ldarg.1
    IL_000f:  callvirt   instance !1 class [mscorlib]System.Collections.Generic.Dictionary`2<class RimWorld.Faction,class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>>::get_Item(!0)
    IL_0014:  ldarg.2
    IL_0015:  callvirt   instance bool class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>::Contains(!0)
    IL_001a:  ret

    IL_001b:  ldc.i4.0
    IL_001c:  ret
  } // end of method AttackTargetsCache::Debug_CheckIfHostileToFaction

  .method private hidebysig specialname rtspecialname static 
          void  .cctor() cil managed
  {
    // Размер кода:       41 (0x29)
    .maxstack  8
    IL_0000:  newobj     instance void class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::.ctor()
    IL_0005:  stsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::targets
    IL_000a:  newobj     instance void class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>::.ctor()
    IL_000f:  stsfld     class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::emptySet
    IL_0014:  newobj     instance void class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::.ctor()
    IL_0019:  stsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::tmpTargets
    IL_001e:  newobj     instance void class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget>::.ctor()
    IL_0023:  stsfld     class [mscorlib]System.Collections.Generic.List`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::tmpToUpdate
    IL_0028:  ret
  } // end of method AttackTargetsCache::.cctor

  .property instance class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget>
          TargetsHostileToColony()
  {
    .get instance class [System.Core]System.Collections.Generic.HashSet`1<class Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::get_TargetsHostileToColony()
  } // end of property AttackTargetsCache::TargetsHostileToColony
} // end of class Verse.AI.AttackTargetsCache

// *********** ДИЗАССЕМБЛИРОВАНИЕ ЗАВЕРШЕНО ***********************
// ВНИМАНИЕ: создан файл ресурсов Win32 codex-build\ildasm\AttackTargetsCache.res
