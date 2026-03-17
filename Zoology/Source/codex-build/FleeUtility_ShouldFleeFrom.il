RimWorld.FleeUtility::ShouldFleeFrom
0000: ldarg.0 
0001: ldarg.1 
0002: beq.s IL_0022: ldc.i4.0
0004: ldarg.2 
0005: brfalse.s IL_0024: ldarg.0
0007: ldarg.0 
0008: callvirt Verse.IntVec3 Verse.Thing::get_Position()
000D: stloc.1 
000E: ldloca.s V_1
0010: ldarg.1 
0011: callvirt Verse.IntVec3 Verse.Thing::get_Position()
0016: ldc.r4 8
001B: call System.Boolean Verse.IntVec3::InHorDistOf(Verse.IntVec3,System.Single)
0020: brtrue.s IL_0024: ldarg.0
0022: ldc.i4.0 
0023: ret 
0024: ldarg.0 
0025: ldfld Verse.ThingDef Verse.Thing::def
002A: ldfld System.Boolean Verse.ThingDef::alwaysFlee
002F: brfalse.s IL_0033: ldarg.0
0031: ldc.i4.1 
0032: ret 
0033: ldarg.0 
0034: ldarg.1 
0035: call System.Boolean RimWorld.GenHostility::HostileTo(Verse.Thing,Verse.Thing)
003A: brtrue.s IL_003e: ldarg.0
003C: ldc.i4.0 
003D: ret 
003E: ldarg.0 
003F: isinst Verse.AI.IAttackTarget
0044: stloc.0 
0045: ldloc.0 
0046: brfalse.s IL_0075: ldc.i4.0
0048: ldloc.0 
0049: ldarg.1 
004A: callvirt System.Boolean Verse.AI.IAttackTarget::ThreatDisabled(Verse.AI.IAttackTargetSearcher)
004F: brtrue.s IL_0075: ldc.i4.0
0051: ldarg.0 
0052: isinst Verse.AI.IAttackTargetSearcher
0057: brfalse.s IL_0075: ldc.i4.0
0059: ldarg.3 
005A: brfalse.s IL_0077: ldc.i4.1
005C: ldarg.1 
005D: callvirt Verse.IntVec3 Verse.Thing::get_Position()
0062: ldarg.0 
0063: callvirt Verse.IntVec3 Verse.Thing::get_Position()
0068: ldarg.1 
0069: callvirt Verse.Map Verse.Thing::get_Map()
006E: call System.Boolean Verse.GenSight::LineOfSight(Verse.IntVec3,Verse.IntVec3,Verse.Map)
0073: brtrue.s IL_0077: ldc.i4.1
0075: ldc.i4.0 
0076: ret 
0077: ldc.i4.1 
0078: ret 
