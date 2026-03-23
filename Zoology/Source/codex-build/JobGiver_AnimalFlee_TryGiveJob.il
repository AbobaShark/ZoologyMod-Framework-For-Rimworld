RimWorld.JobGiver_AnimalFlee::TryGiveJob
0000: ldarg.1 
0001: ldfld RimWorld.Pawn_PlayerSettings Verse.Pawn::playerSettings
0006: brfalse.s IL_0017: ldarg.1
0008: ldarg.1 
0009: ldfld RimWorld.Pawn_PlayerSettings Verse.Pawn::playerSettings
000E: callvirt System.Boolean RimWorld.Pawn_PlayerSettings::get_UsesConfigurableHostilityResponse()
0013: brfalse.s IL_0017: ldarg.1
0015: ldnull 
0016: ret 
0017: ldarg.1 
0018: call System.Boolean RimWorld.ThinkNode_ConditionalShouldFollowMaster::ShouldFollowMaster(Verse.Pawn)
001D: brfalse.s IL_0021: ldarg.1
001F: ldnull 
0020: ret 
0021: ldarg.1 
0022: callvirt RimWorld.Faction Verse.Thing::get_Faction()
0027: brtrue IL_00ad: ldarg.1
002C: ldarg.1 
002D: callvirt Verse.Map Verse.Thing::get_Map()
0032: ldfld Verse.ListerThings Verse.Map::listerThings
0037: ldc.i4.s 48
0039: callvirt System.Collections.Generic.List`1<Verse.Thing> Verse.ListerThings::ThingsInGroup(Verse.ThingRequestGroup)
003E: stloc.0 
003F: ldc.i4.0 
0040: stloc.2 
0041: br.s IL_008f: ldloc.2
0043: ldarg.1 
0044: callvirt Verse.IntVec3 Verse.Thing::get_Position()
0049: stloc.s V_4
004B: ldloca.s V_4
004D: ldloc.0 
004E: ldloc.2 
004F: callvirt !0 System.Collections.Generic.List`1<Verse.Thing>::get_Item(System.Int32)
0054: callvirt Verse.IntVec3 Verse.Thing::get_Position()
0059: ldc.r4 18
005E: call System.Boolean Verse.IntVec3::InHorDistOf(Verse.IntVec3,System.Single)
0063: brfalse.s IL_008b: ldloc.2
0065: ldloc.0 
0066: ldloc.2 
0067: callvirt !0 System.Collections.Generic.List`1<Verse.Thing>::get_Item(System.Int32)
006C: ldarg.1 
006D: ldc.i4.0 
006E: ldc.i4.0 
006F: call System.Boolean RimWorld.FleeUtility::ShouldFleeFrom(Verse.Thing,Verse.Pawn,System.Boolean,System.Boolean)
0074: brfalse.s IL_008b: ldloc.2
0076: ldarg.1 
0077: ldloc.0 
0078: ldloc.2 
0079: callvirt !0 System.Collections.Generic.List`1<Verse.Thing>::get_Item(System.Int32)
007E: ldc.i4.s 24
0080: call Verse.AI.Job RimWorld.FleeUtility::FleeJob(Verse.Pawn,Verse.Thing,System.Int32)
0085: stloc.3 
0086: ldloc.3 
0087: brfalse.s IL_008b: ldloc.2
0089: ldloc.3 
008A: ret 
008B: ldloc.2 
008C: ldc.i4.1 
008D: add 
008E: stloc.2 
008F: ldloc.2 
0090: ldloc.0 
0091: callvirt System.Int32 System.Collections.Generic.List`1<Verse.Thing>::get_Count()
0096: blt.s IL_0043: ldarg.1
0098: ldarg.1 
0099: ldc.i4.s 60
009B: ldc.i4.s 10
009D: ldc.i4.s 24
009F: call Verse.AI.Job RimWorld.FleeUtility::FleeLargeFireJob(Verse.Pawn,System.Int32,System.Int32,System.Int32)
00A4: stloc.1 
00A5: ldloc.1 
00A6: brfalse IL_014c: ldnull
00AB: ldloc.1 
00AC: ret 
00AD: ldarg.1 
00AE: call System.Boolean RimWorld.FleeUtility::ShouldAnimalFleeDanger(Verse.Pawn)
00B3: brfalse IL_014c: ldnull
00B8: ldarg.1 
00B9: callvirt Verse.Map Verse.Thing::get_Map()
00BE: ldfld Verse.AI.AttackTargetsCache Verse.Map::attackTargetsCache
00C3: ldarg.1 
00C4: callvirt System.Collections.Generic.List`1<Verse.AI.IAttackTarget> Verse.AI.AttackTargetsCache::GetPotentialTargetsFor(Verse.AI.IAttackTargetSearcher)
00C9: stloc.s V_5
00CB: ldc.i4.0 
00CC: stloc.s V_6
00CE: br.s IL_0141: ldloc.s V_6
00D0: ldloc.s V_5
00D2: ldloc.s V_6
00D4: callvirt !0 System.Collections.Generic.List`1<Verse.AI.IAttackTarget>::get_Item(System.Int32)
00D9: callvirt Verse.Thing Verse.AI.IAttackTarget::get_Thing()
00DE: stloc.s V_7
00E0: ldarg.1 
00E1: callvirt Verse.IntVec3 Verse.Thing::get_Position()
00E6: stloc.s V_4
00E8: ldloca.s V_4
00EA: ldloc.s V_7
00EC: callvirt Verse.IntVec3 Verse.Thing::get_Position()
00F1: ldc.r4 18
00F6: call System.Boolean Verse.IntVec3::InHorDistOf(Verse.IntVec3,System.Single)
00FB: brfalse.s IL_013b: ldloc.s V_6
00FD: ldloc.s V_7
00FF: ldarg.1 
0100: ldc.i4.0 
0101: ldc.i4.1 
0102: call System.Boolean RimWorld.FleeUtility::ShouldFleeFrom(Verse.Thing,Verse.Pawn,System.Boolean,System.Boolean)
0107: brfalse.s IL_013b: ldloc.s V_6
0109: ldloc.s V_7
010B: isinst Verse.Pawn
0110: stloc.s V_8
0112: ldloc.s V_8
0114: brfalse.s IL_0128: ldarg.1
0116: ldloc.s V_8
0118: call System.Boolean Verse.WildManUtility::AnimalOrWildMan(Verse.Pawn)
011D: brfalse.s IL_0128: ldarg.1
011F: ldloc.s V_8
0121: callvirt RimWorld.Faction Verse.Thing::get_Faction()
0126: brfalse.s IL_013b: ldloc.s V_6
0128: ldarg.1 
0129: ldloc.s V_7
012B: ldc.i4.s 24
012D: call Verse.AI.Job RimWorld.FleeUtility::FleeJob(Verse.Pawn,Verse.Thing,System.Int32)
0132: stloc.s V_9
0134: ldloc.s V_9
0136: brfalse.s IL_013b: ldloc.s V_6
0138: ldloc.s V_9
013A: ret 
013B: ldloc.s V_6
013D: ldc.i4.1 
013E: add 
013F: stloc.s V_6
0141: ldloc.s V_6
0143: ldloc.s V_5
0145: callvirt System.Int32 System.Collections.Generic.List`1<Verse.AI.IAttackTarget>::get_Count()
014A: blt.s IL_00d0: ldloc.s V_5
014C: ldnull 
014D: ret 
