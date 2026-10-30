# Биомы, удержание и питание: 28 октября 2026

Манифест содержит 115 файлов, из них 31 биомный. Выполнены генерация биомов из настроенной Google-таблицы, dry-run, перенос и повторный перенос: 115 SKIP. Формулы и правила Horse/Muffalo не менялись. Дополнительные ручные патчи VAE сохранены. Биомы без найденного существующего патча помещены в Core/Patches/GeneratedBiomes с условием существования BiomeDef; принадлежность неизвестному модулю не выдумывается.

## Биомные назначения

| Файл | Зависимость LoadFolders |
|---|---|
| Core/Patches/Core/BiomeDefs/Biomes_AridShrubland.xml | Core; для новых биомов — проверка существования Def |
| Core/Patches/Core/BiomeDefs/Biomes_BorealForest.xml | Core; для новых биомов — проверка существования Def |
| Core/Patches/Core/BiomeDefs/Biomes_ColdBog.xml | Core; для новых биомов — проверка существования Def |
| Core/Patches/Core/BiomeDefs/Biomes_Desert.xml | Core; для новых биомов — проверка существования Def |
| Core/Patches/Core/BiomeDefs/Biomes_ExtremeDesert.xml | Core; для новых биомов — проверка существования Def |
| Core/Patches/Core/BiomeDefs/Biomes_IceSheet.xml | Core; для новых биомов — проверка существования Def |
| Core/Patches/Core/BiomeDefs/Biomes_SeaIce.xml | Core; для новых биомов — проверка существования Def |
| Core/Patches/Core/BiomeDefs/Biomes_TemperateForest.xml | Core; для новых биомов — проверка существования Def |
| Core/Patches/Core/BiomeDefs/Biomes_TemperateSwamp.xml | Core; для новых биомов — проверка существования Def |
| Core/Patches/Core/BiomeDefs/Biomes_TropicalRainforest.xml | Core; для новых биомов — проверка существования Def |
| Core/Patches/Core/BiomeDefs/Biomes_TropicalSwamp.xml | Core; для новых биомов — проверка существования Def |
| Core/Patches/Core/BiomeDefs/Biomes_Tundra.xml | Core; для новых биомов — проверка существования Def |
| Core/Patches/GeneratedBiomes/Biomes_Alpine.xml | Core; для новых биомов — проверка существования Def |
| Core/Patches/GeneratedBiomes/Biomes_MammothSteppe.xml | Core; для новых биомов — проверка существования Def |
| DLC/Odyssey/Patches/BiomeDefs/Biomes_GlacialPlain.xml | Ludeon.Rimworld.Odyssey |
| DLC/Odyssey/Patches/BiomeDefs/Biomes_Glowforest.xml | Ludeon.Rimworld.Odyssey |
| DLC/Odyssey/Patches/BiomeDefs/Biomes_Grasslands.xml | Ludeon.Rimworld.Odyssey |
| DLC/Odyssey/Patches/BiomeDefs/Biomes_LavaField.xml | Ludeon.Rimworld.Odyssey |
| DLC/Odyssey/Patches/BiomeDefs/Biomes_Scarlands.xml | Ludeon.Rimworld.Odyssey |
| ModPatches/AlphaBiomes/Patches/Biomes/Biomes_AB_FeraliskInfestedJungle.xml | sarg.alphabiomes |
| ModPatches/AlphaBiomes/Patches/Biomes/Biomes_AB_GallatrossGraveyard.xml | sarg.alphabiomes |
| ModPatches/AlphaBiomes/Patches/Biomes/Biomes_AB_GelatinousSuperorganism.xml | sarg.alphabiomes |
| ModPatches/AlphaBiomes/Patches/Biomes/Biomes_AB_IdyllicMeadows.xml | sarg.alphabiomes |
| ModPatches/AlphaBiomes/Patches/Biomes/Biomes_AB_MechanoidIntrusion.xml | sarg.alphabiomes |
| ModPatches/AlphaBiomes/Patches/Biomes/Biomes_AB_MiasmicMangrove.xml | sarg.alphabiomes |
| ModPatches/AlphaBiomes/Patches/Biomes/Biomes_AB_MycoticJungle.xml | sarg.alphabiomes |
| ModPatches/AlphaBiomes/Patches/Biomes/Biomes_AB_OcularForest.xml | sarg.alphabiomes |
| ModPatches/AlphaBiomes/Patches/Biomes/Biomes_AB_PropaneLakes.xml | sarg.alphabiomes |
| ModPatches/AlphaBiomes/Patches/Biomes/Biomes_AB_PyroclasticConflagration.xml | sarg.alphabiomes |
| ModPatches/AlphaBiomes/Patches/Biomes/Biomes_AB_RockyCrags.xml | sarg.alphabiomes |
| ModPatches/AlphaBiomes/Patches/Biomes/Biomes_AB_TarPits.xml | sarg.alphabiomes |

## Игровые изменения

Во всех шести стадиях Constriction прежний offset Moving заменён на setMax=0.15. Движок имеет minForCapable=0.15; изолированный запуск 1.7 подтвердил немедленный Downed и восстановление Moving после снятия удержания. Параметры кровообращения, дыхания и скорости прогрессии не менялись.

Смерть от Hediff ранее регистрировалась только последующим Zoology RegisterPairFromKill. Это не передавало источника в оригинальный Pawn.Kill: его ветка WasKilledByHunter обходилась, и трупу назначался SetForbiddenIfOutsideHomeArea. Для явно причинного Hediff_AnimalConstriction префикс передаёт DamageInfo с нулевым уроном и Instigator=держатель. Узкая проверка охотничьего удержания расширяет WasKilledByHunter. Глобального снятия Forbidden нет. Игровые сценарии прирученной/дикой змеи и ручного запрета ещё не проверены; исправление атрибуции пока подтверждено кодом и сборкой.

SNAKE BUG найден в пользовательском Saves. Оригинал не записывался. SHA256 ADDF0681DE53529D4AEB442CF5D80040CF8BE928D189E09F50CF6D4DCE4DE670. Разобраны сериализованные Pawn AEXP_Rattlesnake21486, JobDriver_Ingest (toil 2, startTick 133651), цель Corpse_Vulture21144, внутренний Vulture20654 с четырьмя MissingBodyPart. Диагностическая копия сохранена в Source/codex-build/implementation/snake-bug.

По коду: WillEat-патч CannotChew был отключён Prepare=false; окончательное потребление слишком крупного трупа возвращало numTaken=0/nutrition=0, а ванильный Ingest возвращается к жеванию, пока голоден. Включён существующий WillEat-фильтр и добавлено одно end-condition в JobDriver_Ingest, которое создаётся также для восстановленных заданий и проверяет тот же IsCorpseTooLarge. Размер берётся из InnerPawn.BodySize, потеря частей не делает слишком крупный труп допустимым. Семантика разрешённых источников не менялась. Само сохранение в игровом runtime не загружалось, устранение дефекта после его загрузки НЕ подтверждено.

## Проверки и ограничения

65 Python-тестов: OK. Сборки 1.6 и 1.7: OK (в 1.7 прежнее предупреждение FormatterServices). Изолированный runtime 1.7 прошёл проверки захвата, общей клетки, немедленного Downed, восстановления движения, сохранения удержания при Downed держателя, порогового освобождения и повторных событий атак. CE в этом этапе не запускался. Тесты forbidden/kill attribution, SNAKE BUG после загрузки, замены/исчезновения пищи и save/load во время Ingest остаются обязательными незакрытыми проверками.

Биомная кнопка GUI и --biomes-only CLI используют общий generate_bundle/xml_routing. Экспорты из OriginalXML и табличных групп пока по-прежнему не подключены к управляемой маршрутизации. Полный первоначальный прогон со старой загруженной версией маршрутизатора отказал на конфликте базового и добавочного AridShrubland; после различения операций полного обновления и добавочных патчей выполнен успешный управляемый биомный прогон.

Изменены: checker/managed_generation.py; checker/generate_animal_pipeline.py; checker/rimworld_patch_fixer.py; Zoology/Core/Defs/HediffDefs/AnimalConstriction.xml; Zoology/Source/Behaviour/Feeding/Patch_CannotChew.cs; Zoology/Source/Behaviour/Predation/AnimalConstriction.cs; Zoology/Source/Behaviour/Predation/PreyProtection/Patch_Predation.cs; обе DLL; tests/RimWorld17/RuntimeValidation.cs; новые test_constriction_caps.py и test_biome_pipeline.py; биомные выходы и манифесты. Commit/push не выполнялись.
