
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0



// предупреждение: ЭТО НЕПОЛНЫЙ ДИЗАССЕМБЛЕРНЫЙ КОД, НЕПРИГОДНЫЙ ДЛЯ ТРАНСЛЯЦИИ



.class public abstract auto ansi sealed beforefieldinit RimWorld.GenHostility
       extends [mscorlib]System.Object
{
  .custom instance void [mscorlib]System.Runtime.CompilerServices.ExtensionAttribute::.ctor() = ( 01 00 00 00 ) 
  .method private hidebysig static bool  IsPredatorHostileTo(class Verse.Pawn predator,
                                                             class Verse.Pawn toPawn) cil managed
  {
    // Размер кода:       73 (0x49)
    .maxstack  3
    .locals init (class Verse.Pawn V_0,
             valuetype Verse.IntVec3 V_1)
    IL_0000:  ldarg.1
    IL_0001:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0006:  brtrue.s   IL_000a

    IL_0008:  ldc.i4.0
    IL_0009:  ret

    IL_000a:  ldarg.1
    IL_000b:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0010:  ldarg.0
    IL_0011:  callvirt   instance bool RimWorld.Faction::HasPredatorRecentlyAttackedAnyone(class Verse.Pawn)
    IL_0016:  brfalse.s  IL_001a

    IL_0018:  ldc.i4.1
    IL_0019:  ret

    IL_001a:  ldarg.0
    IL_001b:  ldarg.1
    IL_001c:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0021:  call       class Verse.Pawn RimWorld.GenHostility::GetPreyOfMyFaction(class Verse.Pawn,
                                                                                    class RimWorld.Faction)
    IL_0026:  stloc.0
    IL_0027:  ldloc.0
    IL_0028:  brfalse.s  IL_0047

    IL_002a:  ldarg.0
    IL_002b:  callvirt   instance valuetype Verse.IntVec3 Verse.Thing::get_Position()
    IL_0030:  stloc.1
    IL_0031:  ldloca.s   V_1
    IL_0033:  ldloc.0
    IL_0034:  callvirt   instance valuetype Verse.IntVec3 Verse.Thing::get_Position()
    IL_0039:  ldc.r4     12.
    IL_003e:  call       instance bool Verse.IntVec3::InHorDistOf(valuetype Verse.IntVec3,
                                                                  float32)
    IL_0043:  brfalse.s  IL_0047

    IL_0045:  ldc.i4.1
    IL_0046:  ret

    IL_0047:  ldc.i4.0
    IL_0048:  ret
  } // end of method GenHostility::IsPredatorHostileTo

  .method private hidebysig static bool  IsPredatorHostileTo(class Verse.Pawn predator,
                                                             class RimWorld.Faction toFaction) cil managed
  {
    // Размер кода:       24 (0x18)
    .maxstack  8
    IL_0000:  ldarg.1
    IL_0001:  ldarg.0
    IL_0002:  callvirt   instance bool RimWorld.Faction::HasPredatorRecentlyAttackedAnyone(class Verse.Pawn)
    IL_0007:  brfalse.s  IL_000b

    IL_0009:  ldc.i4.1
    IL_000a:  ret

    IL_000b:  ldarg.0
    IL_000c:  ldarg.1
    IL_000d:  call       class Verse.Pawn RimWorld.GenHostility::GetPreyOfMyFaction(class Verse.Pawn,
                                                                                    class RimWorld.Faction)
    IL_0012:  brfalse.s  IL_0016

    IL_0014:  ldc.i4.1
    IL_0015:  ret

    IL_0016:  ldc.i4.0
    IL_0017:  ret
  } // end of method GenHostility::IsPredatorHostileTo

} // end of class RimWorld.GenHostility

// *********** ДИЗАССЕМБЛИРОВАНИЕ ЗАВЕРШЕНО ***********************
// ВНИМАНИЕ: создан файл ресурсов Win32 codex-build\ildasm\GenHostility_IsPredatorHostileTo.res
