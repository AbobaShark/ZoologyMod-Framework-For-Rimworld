
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0



// предупреждение: ЭТО НЕПОЛНЫЙ ДИЗАССЕМБЛЕРНЫЙ КОД, НЕПРИГОДНЫЙ ДЛЯ ТРАНСЛЯЦИИ



.class public abstract auto ansi sealed beforefieldinit RimWorld.FleeUtility
       extends [mscorlib]System.Object
{
  .method public hidebysig static bool  ShouldAnimalFleeDanger(class Verse.Pawn pawn) cil managed
  {
    // Размер кода:       217 (0xd9)
    .maxstack  2
    IL_0000:  ldarg.0
    IL_0001:  callvirt   instance bool Verse.Pawn::get_IsAnimal()
    IL_0006:  brfalse    IL_00d7

    IL_000b:  ldarg.0
    IL_000c:  callvirt   instance bool Verse.Pawn::get_InMentalState()
    IL_0011:  brtrue     IL_00d7

    IL_0016:  ldarg.0
    IL_0017:  call       bool RimWorld.PawnUtility::IsFighting(class Verse.Pawn)
    IL_001c:  brtrue     IL_00d7

    IL_0021:  ldarg.0
    IL_0022:  callvirt   instance bool Verse.Pawn::get_Downed()
    IL_0027:  brtrue     IL_00d7

    IL_002c:  ldarg.0
    IL_002d:  callvirt   instance bool Verse.Pawn::get_Dead()
    IL_0032:  brtrue     IL_00d7

    IL_0037:  ldarg.0
    IL_0038:  call       bool RimWorld.ThinkNode_ConditionalShouldFollowMaster::ShouldFollowMaster(class Verse.Pawn)
    IL_003d:  brtrue     IL_00d7

    IL_0042:  ldarg.0
    IL_0043:  call       class Verse.AI.Group.Lord Verse.AI.Group.LordUtility::GetLord(class Verse.Pawn)
    IL_0048:  brtrue     IL_00d7

    IL_004d:  ldarg.0
    IL_004e:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0053:  call       class RimWorld.Faction RimWorld.Faction::get_OfPlayer()
    IL_0058:  bne.un.s   IL_0067

    IL_005a:  ldarg.0
    IL_005b:  callvirt   instance class Verse.Map Verse.Thing::get_Map()
    IL_0060:  callvirt   instance bool Verse.Map::get_IsPlayerHome()
    IL_0065:  brtrue.s   IL_00d7

    IL_0067:  ldarg.0
    IL_0068:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_006d:  brfalse.s  IL_0081

    IL_006f:  ldarg.0
    IL_0070:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0075:  ldfld      class RimWorld.FactionDef RimWorld.Faction::def
    IL_007a:  ldfld      bool RimWorld.FactionDef::animalsFleeDanger
    IL_007f:  brfalse.s  IL_00d7

    IL_0081:  ldarg.0
    IL_0082:  callvirt   instance class Verse.AI.Job Verse.Pawn::get_CurJob()
    IL_0087:  brfalse.s  IL_0096

    IL_0089:  ldarg.0
    IL_008a:  callvirt   instance class Verse.JobDef Verse.Pawn::get_CurJobDef()
    IL_008f:  ldfld      bool Verse.JobDef::neverFleeFromEnemies
    IL_0094:  brtrue.s   IL_00d7

    IL_0096:  ldarg.0
    IL_0097:  callvirt   instance class Verse.AI.Job Verse.Pawn::get_CurJob()
    IL_009c:  brfalse.s  IL_00d5

    IL_009e:  ldarg.0
    IL_009f:  ldfld      class Verse.AI.Pawn_JobTracker Verse.Pawn::jobs
    IL_00a4:  ldfld      class Verse.AI.Job Verse.AI.Pawn_JobTracker::curJob
    IL_00a9:  ldfld      class Verse.JobDef Verse.AI.Job::def
    IL_00ae:  ldsfld     class Verse.JobDef RimWorld.JobDefOf::Flee
    IL_00b3:  bne.un.s   IL_00d5

    IL_00b5:  ldarg.0
    IL_00b6:  ldfld      class Verse.AI.Pawn_JobTracker Verse.Pawn::jobs
    IL_00bb:  ldfld      class Verse.AI.Job Verse.AI.Pawn_JobTracker::curJob
    IL_00c0:  ldfld      int32 Verse.AI.Job::startTick
    IL_00c5:  call       class Verse.TickManager Verse.Find::get_TickManager()
    IL_00ca:  callvirt   instance int32 Verse.TickManager::get_TicksGame()
    IL_00cf:  ceq
    IL_00d1:  ldc.i4.0
    IL_00d2:  ceq
    IL_00d4:  ret

    IL_00d5:  ldc.i4.1
    IL_00d6:  ret

    IL_00d7:  ldc.i4.0
    IL_00d8:  ret
  } // end of method FleeUtility::ShouldAnimalFleeDanger

} // end of class RimWorld.FleeUtility

// *********** ДИЗАССЕМБЛИРОВАНИЕ ЗАВЕРШЕНО ***********************
// ВНИМАНИЕ: создан файл ресурсов Win32 codex-build\ildasm\FleeUtility_ShouldAnimalFleeDanger.res
