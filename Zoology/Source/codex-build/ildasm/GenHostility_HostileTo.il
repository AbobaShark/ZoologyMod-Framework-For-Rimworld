
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0



// предупреждение: ЭТО НЕПОЛНЫЙ ДИЗАССЕМБЛЕРНЫЙ КОД, НЕПРИГОДНЫЙ ДЛЯ ТРАНСЛЯЦИИ



.class public abstract auto ansi sealed beforefieldinit RimWorld.GenHostility
       extends [mscorlib]System.Object
{
  .custom instance void [mscorlib]System.Runtime.CompilerServices.ExtensionAttribute::.ctor() = ( 01 00 00 00 ) 
  .method public hidebysig static bool  HostileTo(class Verse.Thing a,
                                                  class Verse.Thing b) cil managed
  {
    .custom instance void [mscorlib]System.Runtime.CompilerServices.ExtensionAttribute::.ctor() = ( 01 00 00 00 ) 
    // Размер кода:       1070 (0x42e)
    .maxstack  2
    .locals init (class Verse.Pawn V_0,
             class Verse.Pawn V_1)
    IL_0000:  ldarg.0
    IL_0001:  callvirt   instance bool Verse.Thing::get_Destroyed()
    IL_0006:  brtrue.s   IL_0014

    IL_0008:  ldarg.1
    IL_0009:  callvirt   instance bool Verse.Thing::get_Destroyed()
    IL_000e:  brtrue.s   IL_0014

    IL_0010:  ldarg.0
    IL_0011:  ldarg.1
    IL_0012:  bne.un.s   IL_0016

    IL_0014:  ldc.i4.0
    IL_0015:  ret

    IL_0016:  ldarg.0
    IL_0017:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_001c:  brtrue.s   IL_0026

    IL_001e:  ldarg.0
    IL_001f:  call       !!0 Verse.ThingCompUtility::TryGetComp<class RimWorld.CompCauseGameCondition>(class Verse.Thing)
    IL_0024:  brtrue.s   IL_0036

    IL_0026:  ldarg.1
    IL_0027:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_002c:  brtrue.s   IL_0038

    IL_002e:  ldarg.1
    IL_002f:  call       !!0 Verse.ThingCompUtility::TryGetComp<class RimWorld.CompCauseGameCondition>(class Verse.Thing)
    IL_0034:  brfalse.s  IL_0038

    IL_0036:  ldc.i4.1
    IL_0037:  ret

    IL_0038:  ldarg.0
    IL_0039:  isinst     Verse.Pawn
    IL_003e:  stloc.0
    IL_003f:  ldarg.1
    IL_0040:  isinst     Verse.Pawn
    IL_0045:  stloc.1
    IL_0046:  ldloc.0
    IL_0047:  call       bool RimWorld.GenHostility::IsActivityDormant(class Verse.Pawn)
    IL_004c:  brtrue.s   IL_0056

    IL_004e:  ldloc.1
    IL_004f:  call       bool RimWorld.GenHostility::IsActivityDormant(class Verse.Pawn)
    IL_0054:  brfalse.s  IL_0058

    IL_0056:  ldc.i4.0
    IL_0057:  ret

    IL_0058:  ldloc.0
    IL_0059:  brfalse.s  IL_0068

    IL_005b:  ldloc.0
    IL_005c:  ldfld      class Verse.PawnKindDef Verse.Pawn::kindDef
    IL_0061:  ldfld      bool Verse.PawnKindDef::hostileToAll
    IL_0066:  brtrue.s   IL_0078

    IL_0068:  ldloc.1
    IL_0069:  brfalse.s  IL_007a

    IL_006b:  ldloc.1
    IL_006c:  ldfld      class Verse.PawnKindDef Verse.Pawn::kindDef
    IL_0071:  ldfld      bool Verse.PawnKindDef::hostileToAll
    IL_0076:  brfalse.s  IL_007a

    IL_0078:  ldc.i4.1
    IL_0079:  ret

    IL_007a:  ldloc.0
    IL_007b:  brfalse.s  IL_00b8

    IL_007d:  ldloc.1
    IL_007e:  brfalse.s  IL_00b8

    IL_0080:  ldloc.0
    IL_0081:  ldfld      class RimWorld.Pawn_StoryTracker Verse.Pawn::story
    IL_0086:  brfalse.s  IL_009b

    IL_0088:  ldloc.0
    IL_0089:  ldfld      class RimWorld.Pawn_StoryTracker Verse.Pawn::story
    IL_008e:  ldfld      class RimWorld.TraitSet RimWorld.Pawn_StoryTracker::traits
    IL_0093:  ldloc.1
    IL_0094:  callvirt   instance bool RimWorld.TraitSet::DisableHostilityFrom(class Verse.Pawn)
    IL_0099:  brtrue.s   IL_00b6

    IL_009b:  ldloc.1
    IL_009c:  ldfld      class RimWorld.Pawn_StoryTracker Verse.Pawn::story
    IL_00a1:  brfalse.s  IL_00b8

    IL_00a3:  ldloc.1
    IL_00a4:  ldfld      class RimWorld.Pawn_StoryTracker Verse.Pawn::story
    IL_00a9:  ldfld      class RimWorld.TraitSet RimWorld.Pawn_StoryTracker::traits
    IL_00ae:  ldloc.0
    IL_00af:  callvirt   instance bool RimWorld.TraitSet::DisableHostilityFrom(class Verse.Pawn)
    IL_00b4:  brfalse.s  IL_00b8

    IL_00b6:  ldc.i4.0
    IL_00b7:  ret

    IL_00b8:  ldloc.0
    IL_00b9:  brfalse.s  IL_00d1

    IL_00bb:  ldloc.0
    IL_00bc:  callvirt   instance class Verse.AI.MentalState Verse.Pawn::get_MentalState()
    IL_00c1:  brfalse.s  IL_00d1

    IL_00c3:  ldloc.0
    IL_00c4:  callvirt   instance class Verse.AI.MentalState Verse.Pawn::get_MentalState()
    IL_00c9:  ldarg.1
    IL_00ca:  callvirt   instance bool Verse.AI.MentalState::ForceHostileTo(class Verse.Thing)
    IL_00cf:  brtrue.s   IL_00ea

    IL_00d1:  ldloc.1
    IL_00d2:  brfalse.s  IL_00ec

    IL_00d4:  ldloc.1
    IL_00d5:  callvirt   instance class Verse.AI.MentalState Verse.Pawn::get_MentalState()
    IL_00da:  brfalse.s  IL_00ec

    IL_00dc:  ldloc.1
    IL_00dd:  callvirt   instance class Verse.AI.MentalState Verse.Pawn::get_MentalState()
    IL_00e2:  ldarg.0
    IL_00e3:  callvirt   instance bool Verse.AI.MentalState::ForceHostileTo(class Verse.Thing)
    IL_00e8:  brfalse.s  IL_00ec

    IL_00ea:  ldc.i4.1
    IL_00eb:  ret

    IL_00ec:  ldloc.0
    IL_00ed:  brfalse.s  IL_0106

    IL_00ef:  ldloc.1
    IL_00f0:  brfalse.s  IL_0106

    IL_00f2:  ldloc.0
    IL_00f3:  ldloc.1
    IL_00f4:  call       bool RimWorld.GenHostility::IsPredatorHostileTo(class Verse.Pawn,
                                                                         class Verse.Pawn)
    IL_00f9:  brtrue.s   IL_0104

    IL_00fb:  ldloc.1
    IL_00fc:  ldloc.0
    IL_00fd:  call       bool RimWorld.GenHostility::IsPredatorHostileTo(class Verse.Pawn,
                                                                         class Verse.Pawn)
    IL_0102:  brfalse.s  IL_0106

    IL_0104:  ldc.i4.1
    IL_0105:  ret

    IL_0106:  ldarg.0
    IL_0107:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_010c:  brfalse.s  IL_0132

    IL_010e:  ldloc.1
    IL_010f:  brfalse.s  IL_0132

    IL_0111:  ldloc.1
    IL_0112:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
    IL_0117:  ldarg.0
    IL_0118:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_011d:  bne.un.s   IL_0132

    IL_011f:  ldloc.0
    IL_0120:  brfalse.s  IL_012a

    IL_0122:  ldloc.0
    IL_0123:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
    IL_0128:  brtrue.s   IL_0132

    IL_012a:  ldloc.1
    IL_012b:  call       bool RimWorld.PrisonBreakUtility::IsPrisonBreaking(class Verse.Pawn)
    IL_0130:  brtrue.s   IL_015e

    IL_0132:  ldarg.1
    IL_0133:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0138:  brfalse.s  IL_0160

    IL_013a:  ldloc.0
    IL_013b:  brfalse.s  IL_0160

    IL_013d:  ldloc.0
    IL_013e:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
    IL_0143:  ldarg.1
    IL_0144:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0149:  bne.un.s   IL_0160

    IL_014b:  ldloc.1
    IL_014c:  brfalse.s  IL_0156

    IL_014e:  ldloc.1
    IL_014f:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
    IL_0154:  brtrue.s   IL_0160

    IL_0156:  ldloc.0
    IL_0157:  call       bool RimWorld.PrisonBreakUtility::IsPrisonBreaking(class Verse.Pawn)
    IL_015c:  brfalse.s  IL_0160

    IL_015e:  ldc.i4.1
    IL_015f:  ret

    IL_0160:  ldarg.0
    IL_0161:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0166:  brfalse.s  IL_0194

    IL_0168:  ldloc.1
    IL_0169:  brfalse.s  IL_0194

    IL_016b:  ldloc.1
    IL_016c:  callvirt   instance bool Verse.Pawn::get_IsSlave()
    IL_0171:  brfalse.s  IL_0194

    IL_0173:  ldloc.1
    IL_0174:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0179:  ldarg.0
    IL_017a:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_017f:  bne.un.s   IL_0194

    IL_0181:  ldloc.0
    IL_0182:  brfalse.s  IL_018c

    IL_0184:  ldloc.0
    IL_0185:  callvirt   instance bool Verse.Pawn::get_IsSlave()
    IL_018a:  brtrue.s   IL_0194

    IL_018c:  ldloc.1
    IL_018d:  call       bool RimWorld.SlaveRebellionUtility::IsRebelling(class Verse.Pawn)
    IL_0192:  brtrue.s   IL_01c8

    IL_0194:  ldarg.1
    IL_0195:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_019a:  brfalse.s  IL_01ca

    IL_019c:  ldloc.0
    IL_019d:  brfalse.s  IL_01ca

    IL_019f:  ldloc.0
    IL_01a0:  callvirt   instance bool Verse.Pawn::get_IsSlave()
    IL_01a5:  brfalse.s  IL_01ca

    IL_01a7:  ldloc.0
    IL_01a8:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_01ad:  ldarg.1
    IL_01ae:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_01b3:  bne.un.s   IL_01ca

    IL_01b5:  ldloc.1
    IL_01b6:  brfalse.s  IL_01c0

    IL_01b8:  ldloc.1
    IL_01b9:  callvirt   instance bool Verse.Pawn::get_IsSlave()
    IL_01be:  brtrue.s   IL_01ca

    IL_01c0:  ldloc.0
    IL_01c1:  call       bool RimWorld.SlaveRebellionUtility::IsRebelling(class Verse.Pawn)
    IL_01c6:  brfalse.s  IL_01ca

    IL_01c8:  ldc.i4.1
    IL_01c9:  ret

    IL_01ca:  ldarg.0
    IL_01cb:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_01d0:  brfalse.s  IL_01e3

    IL_01d2:  ldloc.1
    IL_01d3:  brfalse.s  IL_01e3

    IL_01d5:  ldloc.1
    IL_01d6:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
    IL_01db:  ldarg.0
    IL_01dc:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_01e1:  beq.s      IL_01fc

    IL_01e3:  ldarg.1
    IL_01e4:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_01e9:  brfalse.s  IL_01fe

    IL_01eb:  ldloc.0
    IL_01ec:  brfalse.s  IL_01fe

    IL_01ee:  ldloc.0
    IL_01ef:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
    IL_01f4:  ldarg.1
    IL_01f5:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_01fa:  bne.un.s   IL_01fe

    IL_01fc:  ldc.i4.0
    IL_01fd:  ret

    IL_01fe:  ldloc.0
    IL_01ff:  brfalse.s  IL_0216

    IL_0201:  ldloc.0
    IL_0202:  callvirt   instance bool Verse.Pawn::get_IsPrisoner()
    IL_0207:  brfalse.s  IL_0216

    IL_0209:  ldloc.1
    IL_020a:  brfalse.s  IL_0216

    IL_020c:  ldloc.1
    IL_020d:  callvirt   instance bool Verse.Pawn::get_IsPrisoner()
    IL_0212:  brfalse.s  IL_0216

    IL_0214:  ldc.i4.0
    IL_0215:  ret

    IL_0216:  ldloc.0
    IL_0217:  brfalse.s  IL_022e

    IL_0219:  ldloc.0
    IL_021a:  callvirt   instance bool Verse.Pawn::get_IsSlave()
    IL_021f:  brfalse.s  IL_022e

    IL_0221:  ldloc.1
    IL_0222:  brfalse.s  IL_022e

    IL_0224:  ldloc.1
    IL_0225:  callvirt   instance bool Verse.Pawn::get_IsSlave()
    IL_022a:  brfalse.s  IL_022e

    IL_022c:  ldc.i4.0
    IL_022d:  ret

    IL_022e:  ldloc.0
    IL_022f:  brfalse.s  IL_0272

    IL_0231:  ldloc.1
    IL_0232:  brfalse.s  IL_0272

    IL_0234:  ldloc.0
    IL_0235:  callvirt   instance bool Verse.Pawn::get_IsPrisoner()
    IL_023a:  brfalse.s  IL_0252

    IL_023c:  ldloc.0
    IL_023d:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
    IL_0242:  ldloc.1
    IL_0243:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
    IL_0248:  bne.un.s   IL_0252

    IL_024a:  ldloc.0
    IL_024b:  call       bool RimWorld.PrisonBreakUtility::IsPrisonBreaking(class Verse.Pawn)
    IL_0250:  brfalse.s  IL_0270

    IL_0252:  ldloc.1
    IL_0253:  callvirt   instance bool Verse.Pawn::get_IsPrisoner()
    IL_0258:  brfalse.s  IL_0272

    IL_025a:  ldloc.1
    IL_025b:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
    IL_0260:  ldloc.0
    IL_0261:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
    IL_0266:  bne.un.s   IL_0272

    IL_0268:  ldloc.1
    IL_0269:  call       bool RimWorld.PrisonBreakUtility::IsPrisonBreaking(class Verse.Pawn)
    IL_026e:  brtrue.s   IL_0272

    IL_0270:  ldc.i4.0
    IL_0271:  ret

    IL_0272:  ldloc.0
    IL_0273:  brfalse.s  IL_02d0

    IL_0275:  ldloc.1
    IL_0276:  brfalse.s  IL_02d0

    IL_0278:  ldloc.0
    IL_0279:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
    IL_027e:  brfalse.s  IL_02a3

    IL_0280:  ldloc.1
    IL_0281:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0286:  brfalse.s  IL_02a3

    IL_0288:  ldloc.0
    IL_0289:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
    IL_028e:  ldloc.1
    IL_028f:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0294:  call       bool RimWorld.FactionUtility::HostileTo(class RimWorld.Faction,
                                                                 class RimWorld.Faction)
    IL_0299:  brtrue.s   IL_02a3

    IL_029b:  ldloc.0
    IL_029c:  call       bool RimWorld.PrisonBreakUtility::IsPrisonBreaking(class Verse.Pawn)
    IL_02a1:  brfalse.s  IL_02ce

    IL_02a3:  ldloc.1
    IL_02a4:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
    IL_02a9:  brfalse.s  IL_02d0

    IL_02ab:  ldloc.0
    IL_02ac:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_02b1:  brfalse.s  IL_02d0

    IL_02b3:  ldloc.1
    IL_02b4:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
    IL_02b9:  ldloc.0
    IL_02ba:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_02bf:  call       bool RimWorld.FactionUtility::HostileTo(class RimWorld.Faction,
                                                                 class RimWorld.Faction)
    IL_02c4:  brtrue.s   IL_02d0

    IL_02c6:  ldloc.1
    IL_02c7:  call       bool RimWorld.PrisonBreakUtility::IsPrisonBreaking(class Verse.Pawn)
    IL_02cc:  brtrue.s   IL_02d0

    IL_02ce:  ldc.i4.0
    IL_02cf:  ret

    IL_02d0:  ldarg.0
    IL_02d1:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_02d6:  brfalse.s  IL_02f5

    IL_02d8:  ldarg.0
    IL_02d9:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_02de:  callvirt   instance bool RimWorld.Faction::get_IsPlayer()
    IL_02e3:  brfalse.s  IL_02f5

    IL_02e5:  ldloc.1
    IL_02e6:  brfalse.s  IL_02f5

    IL_02e8:  ldloc.1
    IL_02e9:  ldfld      class Verse.AI.Pawn_MindState Verse.Pawn::mindState
    IL_02ee:  callvirt   instance bool Verse.AI.Pawn_MindState::get_WillJoinColonyIfRescued()
    IL_02f3:  brtrue.s   IL_031a

    IL_02f5:  ldarg.1
    IL_02f6:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_02fb:  brfalse.s  IL_031c

    IL_02fd:  ldarg.1
    IL_02fe:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0303:  callvirt   instance bool RimWorld.Faction::get_IsPlayer()
    IL_0308:  brfalse.s  IL_031c

    IL_030a:  ldloc.0
    IL_030b:  brfalse.s  IL_031c

    IL_030d:  ldloc.0
    IL_030e:  ldfld      class Verse.AI.Pawn_MindState Verse.Pawn::mindState
    IL_0313:  callvirt   instance bool Verse.AI.Pawn_MindState::get_WillJoinColonyIfRescued()
    IL_0318:  brfalse.s  IL_031c

    IL_031a:  ldc.i4.0
    IL_031b:  ret

    IL_031c:  ldloc.0
    IL_031d:  brfalse.s  IL_0336

    IL_031f:  ldloc.1
    IL_0320:  brfalse.s  IL_0336

    IL_0322:  ldloc.0
    IL_0323:  ldloc.1
    IL_0324:  callvirt   instance bool Verse.Pawn::ThreatDisabledBecauseNonAggressiveRoamer(class Verse.Pawn)
    IL_0329:  brtrue.s   IL_0334

    IL_032b:  ldloc.1
    IL_032c:  ldloc.0
    IL_032d:  callvirt   instance bool Verse.Pawn::ThreatDisabledBecauseNonAggressiveRoamer(class Verse.Pawn)
    IL_0332:  brfalse.s  IL_0336

    IL_0334:  ldc.i4.0
    IL_0335:  ret

    IL_0336:  ldloc.0
    IL_0337:  brfalse.s  IL_0349

    IL_0339:  ldloc.0
    IL_033a:  call       bool MechanitorUtility::IsPlayerOverseerSubject(class Verse.Pawn)
    IL_033f:  brfalse.s  IL_0349

    IL_0341:  ldloc.0
    IL_0342:  callvirt   instance bool Verse.Pawn::get_IsColonyMechPlayerControlled()
    IL_0347:  brfalse.s  IL_035c

    IL_0349:  ldloc.1
    IL_034a:  brfalse.s  IL_035e

    IL_034c:  ldloc.1
    IL_034d:  call       bool MechanitorUtility::IsPlayerOverseerSubject(class Verse.Pawn)
    IL_0352:  brfalse.s  IL_035e

    IL_0354:  ldloc.1
    IL_0355:  callvirt   instance bool Verse.Pawn::get_IsColonyMechPlayerControlled()
    IL_035a:  brtrue.s   IL_035e

    IL_035c:  ldc.i4.0
    IL_035d:  ret

    IL_035e:  ldloc.0
    IL_035f:  brfalse.s  IL_0390

    IL_0361:  ldloc.0
    IL_0362:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0367:  brtrue.s   IL_0390

    IL_0369:  ldloc.0
    IL_036a:  callvirt   instance class Verse.RaceProperties Verse.Pawn::get_RaceProps()
    IL_036f:  callvirt   instance bool Verse.RaceProperties::get_Humanlike()
    IL_0374:  brfalse.s  IL_0390

    IL_0376:  ldarg.1
    IL_0377:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_037c:  brfalse.s  IL_0390

    IL_037e:  ldarg.1
    IL_037f:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0384:  ldfld      class RimWorld.FactionDef RimWorld.Faction::def
    IL_0389:  ldfld      bool RimWorld.FactionDef::hostileToFactionlessHumanlikes
    IL_038e:  brtrue.s   IL_03c2

    IL_0390:  ldloc.1
    IL_0391:  brfalse.s  IL_03c4

    IL_0393:  ldloc.1
    IL_0394:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0399:  brtrue.s   IL_03c4

    IL_039b:  ldloc.1
    IL_039c:  callvirt   instance class Verse.RaceProperties Verse.Pawn::get_RaceProps()
    IL_03a1:  callvirt   instance bool Verse.RaceProperties::get_Humanlike()
    IL_03a6:  brfalse.s  IL_03c4

    IL_03a8:  ldarg.0
    IL_03a9:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_03ae:  brfalse.s  IL_03c4

    IL_03b0:  ldarg.0
    IL_03b1:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_03b6:  ldfld      class RimWorld.FactionDef RimWorld.Faction::def
    IL_03bb:  ldfld      bool RimWorld.FactionDef::hostileToFactionlessHumanlikes
    IL_03c0:  brfalse.s  IL_03c4

    IL_03c2:  ldc.i4.1
    IL_03c3:  ret

    IL_03c4:  ldloc.0
    IL_03c5:  brfalse.s  IL_03e2

    IL_03c7:  ldloc.1
    IL_03c8:  brfalse.s  IL_03e2

    IL_03ca:  ldloc.0
    IL_03cb:  callvirt   instance bool Verse.Pawn::get_IsShambler()
    IL_03d0:  brtrue.s   IL_03da

    IL_03d2:  ldloc.1
    IL_03d3:  callvirt   instance bool Verse.Pawn::get_IsShambler()
    IL_03d8:  brfalse.s  IL_03e2

    IL_03da:  ldloc.0
    IL_03db:  ldloc.1
    IL_03dc:  call       bool RimWorld.MutantUtility::CheckShamblerHostility(class Verse.Pawn,
                                                                             class Verse.Pawn)
    IL_03e1:  ret

    IL_03e2:  ldloc.0
    IL_03e3:  brfalse.s  IL_03f5

    IL_03e5:  ldarg.1
    IL_03e6:  isinst     RimWorld.Building_Turret
    IL_03eb:  brfalse.s  IL_03f5

    IL_03ed:  ldloc.0
    IL_03ee:  call       bool RimWorld.InvisibilityUtility::IsPsychologicallyInvisible(class Verse.Pawn)
    IL_03f3:  brtrue.s   IL_0408

    IL_03f5:  ldloc.1
    IL_03f6:  brfalse.s  IL_040a

    IL_03f8:  ldarg.0
    IL_03f9:  isinst     RimWorld.Building_Turret
    IL_03fe:  brfalse.s  IL_040a

    IL_0400:  ldloc.1
    IL_0401:  call       bool RimWorld.InvisibilityUtility::IsPsychologicallyInvisible(class Verse.Pawn)
    IL_0406:  brfalse.s  IL_040a

    IL_0408:  ldc.i4.0
    IL_0409:  ret

    IL_040a:  ldarg.0
    IL_040b:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0410:  brfalse.s  IL_041a

    IL_0412:  ldarg.1
    IL_0413:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0418:  brtrue.s   IL_041c

    IL_041a:  ldc.i4.0
    IL_041b:  ret

    IL_041c:  ldarg.0
    IL_041d:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0422:  ldarg.1
    IL_0423:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0428:  call       bool RimWorld.FactionUtility::HostileTo(class RimWorld.Faction,
                                                                 class RimWorld.Faction)
    IL_042d:  ret
  } // end of method GenHostility::HostileTo

  .method public hidebysig static bool  HostileTo(class Verse.Thing t,
                                                  class RimWorld.Faction fac) cil managed
  {
    .custom instance void [mscorlib]System.Runtime.CompilerServices.ExtensionAttribute::.ctor() = ( 01 00 00 00 ) 
    // Размер кода:       284 (0x11c)
    .maxstack  2
    .locals init (class Verse.Pawn V_0,
             class Verse.AI.MentalState V_1)
    IL_0000:  ldarg.0
    IL_0001:  callvirt   instance bool Verse.Thing::get_Destroyed()
    IL_0006:  brfalse.s  IL_000a

    IL_0008:  ldc.i4.0
    IL_0009:  ret

    IL_000a:  ldarg.1
    IL_000b:  brtrue.s   IL_000f

    IL_000d:  ldc.i4.0
    IL_000e:  ret

    IL_000f:  ldarg.0
    IL_0010:  isinst     Verse.Pawn
    IL_0015:  stloc.0
    IL_0016:  ldloc.0
    IL_0017:  brfalse    IL_00f3

    IL_001c:  ldloc.0
    IL_001d:  callvirt   instance class Verse.AI.MentalState Verse.Pawn::get_MentalState()
    IL_0022:  stloc.1
    IL_0023:  ldloc.1
    IL_0024:  brfalse.s  IL_0031

    IL_0026:  ldloc.1
    IL_0027:  ldarg.1
    IL_0028:  callvirt   instance bool Verse.AI.MentalState::ForceHostileTo(class RimWorld.Faction)
    IL_002d:  brfalse.s  IL_0031

    IL_002f:  ldc.i4.1
    IL_0030:  ret

    IL_0031:  ldloc.0
    IL_0032:  ldarg.1
    IL_0033:  call       bool RimWorld.GenHostility::IsPredatorHostileTo(class Verse.Pawn,
                                                                         class RimWorld.Faction)
    IL_0038:  brfalse.s  IL_003c

    IL_003a:  ldc.i4.1
    IL_003b:  ret

    IL_003c:  ldloc.0
    IL_003d:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
    IL_0042:  ldarg.1
    IL_0043:  bne.un.s   IL_004f

    IL_0045:  ldloc.0
    IL_0046:  call       bool RimWorld.PrisonBreakUtility::IsPrisonBreaking(class Verse.Pawn)
    IL_004b:  brfalse.s  IL_004f

    IL_004d:  ldc.i4.1
    IL_004e:  ret

    IL_004f:  ldloc.0
    IL_0050:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0055:  ldarg.1
    IL_0056:  bne.un.s   IL_0062

    IL_0058:  ldloc.0
    IL_0059:  call       bool RimWorld.SlaveRebellionUtility::IsRebelling(class Verse.Pawn)
    IL_005e:  brfalse.s  IL_0062

    IL_0060:  ldc.i4.1
    IL_0061:  ret

    IL_0062:  ldloc.0
    IL_0063:  callvirt   instance bool Verse.Pawn::get_IsShambler()
    IL_0068:  brfalse.s  IL_0074

    IL_006a:  ldloc.0
    IL_006b:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0070:  brtrue.s   IL_0074

    IL_0072:  ldc.i4.1
    IL_0073:  ret

    IL_0074:  ldloc.0
    IL_0075:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
    IL_007a:  ldarg.1
    IL_007b:  bne.un.s   IL_007f

    IL_007d:  ldc.i4.0
    IL_007e:  ret

    IL_007f:  ldloc.0
    IL_0080:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
    IL_0085:  brfalse.s  IL_009f

    IL_0087:  ldloc.0
    IL_0088:  callvirt   instance class RimWorld.Faction Verse.Pawn::get_HostFaction()
    IL_008d:  ldarg.1
    IL_008e:  call       bool RimWorld.FactionUtility::HostileTo(class RimWorld.Faction,
                                                                 class RimWorld.Faction)
    IL_0093:  brtrue.s   IL_009f

    IL_0095:  ldloc.0
    IL_0096:  call       bool RimWorld.PrisonBreakUtility::IsPrisonBreaking(class Verse.Pawn)
    IL_009b:  brtrue.s   IL_009f

    IL_009d:  ldc.i4.0
    IL_009e:  ret

    IL_009f:  ldarg.1
    IL_00a0:  callvirt   instance bool RimWorld.Faction::get_IsPlayer()
    IL_00a5:  brfalse.s  IL_00b6

    IL_00a7:  ldloc.0
    IL_00a8:  ldfld      class Verse.AI.Pawn_MindState Verse.Pawn::mindState
    IL_00ad:  callvirt   instance bool Verse.AI.Pawn_MindState::get_WillJoinColonyIfRescued()
    IL_00b2:  brfalse.s  IL_00b6

    IL_00b4:  ldc.i4.0
    IL_00b5:  ret

    IL_00b6:  ldloc.0
    IL_00b7:  call       bool RimWorld.GenHostility::IsActivityDormant(class Verse.Pawn)
    IL_00bc:  brfalse.s  IL_00c0

    IL_00be:  ldc.i4.0
    IL_00bf:  ret

    IL_00c0:  ldloc.0
    IL_00c1:  ldfld      class Verse.PawnKindDef Verse.Pawn::kindDef
    IL_00c6:  ldfld      bool Verse.PawnKindDef::hostileToAll
    IL_00cb:  brfalse.s  IL_00cf

    IL_00cd:  ldc.i4.1
    IL_00ce:  ret

    IL_00cf:  ldarg.1
    IL_00d0:  ldfld      class RimWorld.FactionDef RimWorld.Faction::def
    IL_00d5:  ldfld      bool RimWorld.FactionDef::hostileToFactionlessHumanlikes
    IL_00da:  brfalse.s  IL_0105

    IL_00dc:  ldloc.0
    IL_00dd:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_00e2:  brtrue.s   IL_0105

    IL_00e4:  ldloc.0
    IL_00e5:  callvirt   instance class Verse.RaceProperties Verse.Pawn::get_RaceProps()
    IL_00ea:  callvirt   instance bool Verse.RaceProperties::get_Humanlike()
    IL_00ef:  brfalse.s  IL_0105

    IL_00f1:  ldc.i4.1
    IL_00f2:  ret

    IL_00f3:  ldarg.0
    IL_00f4:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_00f9:  brtrue.s   IL_0105

    IL_00fb:  ldarg.0
    IL_00fc:  call       !!0 Verse.ThingCompUtility::TryGetComp<class RimWorld.CompCauseGameCondition>(class Verse.Thing)
    IL_0101:  brfalse.s  IL_0105

    IL_0103:  ldc.i4.1
    IL_0104:  ret

    IL_0105:  ldarg.0
    IL_0106:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_010b:  brtrue.s   IL_010f

    IL_010d:  ldc.i4.0
    IL_010e:  ret

    IL_010f:  ldarg.0
    IL_0110:  callvirt   instance class RimWorld.Faction Verse.Thing::get_Faction()
    IL_0115:  ldarg.1
    IL_0116:  call       bool RimWorld.FactionUtility::HostileTo(class RimWorld.Faction,
                                                                 class RimWorld.Faction)
    IL_011b:  ret
  } // end of method GenHostility::HostileTo

} // end of class RimWorld.GenHostility

// *********** ДИЗАССЕМБЛИРОВАНИЕ ЗАВЕРШЕНО ***********************
// ВНИМАНИЕ: создан файл ресурсов Win32 codex-build\ildasm\GenHostility_HostileTo.res
