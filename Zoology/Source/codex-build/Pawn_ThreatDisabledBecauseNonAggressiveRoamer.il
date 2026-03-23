Verse.Pawn::ThreatDisabledBecauseNonAggressiveRoamer
0000: ldarg.0 
0001: call System.Boolean Verse.Pawn::get_Roamer()
0006: brfalse.s IL_0015: ldc.i4.0
0008: ldarg.0 
0009: call RimWorld.Faction Verse.Thing::get_Faction()
000E: call RimWorld.Faction RimWorld.Faction::get_OfPlayer()
0013: beq.s IL_0017: ldarg.1
0015: ldc.i4.0 
0016: ret 
0017: ldarg.1 
0018: brtrue.s IL_001d: ldarg.1
001A: ldnull 
001B: br.s IL_0023: stloc.0
001D: ldarg.1 
001E: call Verse.AI.Group.Lord Verse.AI.Group.LordUtility::GetLord(Verse.Pawn)
0023: stloc.0 
0024: ldloc.0 
0025: brfalse.s IL_0036: ldarg.0
0027: ldloc.0 
0028: callvirt Verse.AI.Group.LordToil Verse.AI.Group.Lord::get_CurLordToil()
002D: callvirt System.Boolean Verse.AI.Group.LordToil::get_AllowAggressiveTargetingOfRoamers()
0032: brfalse.s IL_0036: ldarg.0
0034: ldc.i4.0 
0035: ret 
0036: ldarg.0 
0037: call System.Boolean Verse.Pawn::get_InAggroMentalState()
003C: brtrue.s IL_0063: ldc.i4.0
003E: ldarg.0 
003F: call System.Boolean RimWorld.PawnUtility::IsFighting(Verse.Pawn)
0044: brtrue.s IL_0063: ldc.i4.0
0046: call Verse.TickManager Verse.Find::get_TickManager()
004B: callvirt System.Int32 Verse.TickManager::get_TicksGame()
0050: ldarg.0 
0051: ldfld Verse.AI.Pawn_MindState Verse.Pawn::mindState
0056: ldfld System.Int32 Verse.AI.Pawn_MindState::lastEngageTargetTick
005B: ldc.i4 360
0060: add 
0061: bge.s IL_0065: ldc.i4.1
0063: ldc.i4.0 
0064: ret 
0065: ldc.i4.1 
0066: ret 
