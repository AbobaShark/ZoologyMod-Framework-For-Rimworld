# 1000 chickens: actual-game benchmark

## Результат 2026-11-04

Проверено в **RimWorld 1.7.4998 rev1299**, на Ryzen 5 1600. По два прогона
каждого варианта, 6000 измеряемых тиков после 1500 тиков прогрева. Для таблицы
взята медиана 24 окон по 500 тиков; отрисовка — диапазон медиан двух отдельных
серий по 120 настоящих кадров.

| Вариант | Полный игровой тик, мс | CPU динамической отрисовки, мс |
| --- | ---: | ---: |
| 1000 свободных кур, текущая сборка | 4.988 | 6.806–8.708 |
| 1000 кур в 7 хлевах, до оптимизации | 1.123 | 0.784–0.829 |
| 1000 кур в 7 хлевах, после оптимизации | 0.884 | 0.774–0.786 |

Упаковка в текущей сборке уменьшила стоимость полного тика на **82.3%**:
примерно **5.64 раза** быстрее. Это стоимость симуляции, не обещание фактического
TPS/FPS любой колонии. Динамическая отрисовка потребовала примерно в 9–11 раз
меньше CPU. Во всех четырёх упакованных прогонах внутри оставались все 1000
живых кур, без истощения; все семь зданий сохранили полную прочность.

Количество изображений после правки не уменьшено: **147 кур** и реальные стопки
корма на семи хлевах. Четыре хеша выбранных pawn и их точных координат до/после
совпадают. Нативный размер, видимость на отдалении, сортировка, бюджет клеток и
графика корма сохранены.

Новое ускорение касается повторных безуспешных поисков места для одинаковых
спрайтов. В одном процессе, на тех же животных, десять обновлений каждого хлева:
ускоренный вариант **14.1–15.5 мс**, исходный поиск **57.1–57.2 мс**. Время
обновления сократилось примерно на 73–75%. Исходный вариант для этой проверки
получен очисткой только нового кеша перед каждой попыткой; тестовый Harmony-hook
добавляет ему небольшую накладную стоимость.

Наблюдаемое улучшение полного тика между отдельными сборками — 21.2%, но целиком
приписывать его этой правке нельзя. В отдельном профиле исходная раскладка
занимала лишь около 5% времени Tick хлевов (54–56 мс из 1117–1142 мс), новая —
15–16 мс. Сокращение этой секции само по себе объясняет около **0.027 мс на тик**,
то есть несколько процентов общего времени. Остальная разница включает
вариативность запусков, JIT и фоновой нагрузки. Основной устойчивый выигрыш даёт
само исключение AI и перемещения содержащихся животных.

Дальнейшие крупные сокращения упираются в сохранённую симуляцию животных.
Частоту физиологии, производства, питания, обслуживания, пожара и обновления
графики эта правка не меняет. В штатном наборе прошли **104 проверки**, включая
точное сравнение с исходным размещением для смешанных групп, коров, кур,
взрослых свиней и поросят, питание, лактацию, сохранения и огонь. Release 1.7:
0 ошибок и 0 предупреждений. Сборка 1.6 не изменялась.

Логи: `performance-before.txt`, `performance-after.txt`, `last-run.txt`.
Агрегаты и проверка четырёх хешей: `performance-summary.json`; пересчёт —
`python validation/AnimalBarn17/summarize-performance.py`.

Исходная DLL (HEAD e2f23b9):
`8D9AC0884450BCBE202C61242AD64568EC946CF38CA725CC92BB891D2ED322B1`.
Проверенная оптимизированная DLL:
`810F61CEB76CC0DE6474672D987F29B06415EDF7EA3EF0220958066B975B9FD2`.

Сравнение не включает работу колонистов по пополнению корма, полный суточный
цикл, массовое размножение, лечение или пожарную нагрузку. Свободные куры сами
выбирают сон, прогулки, траву и комбикорм, поэтому их текущий уровень питания и
остаток комбикорма отличаются от содержащихся. Это не сравнение одинакового
рациона: расход настоящих предметов и физиология проверяются отдельно.

## Reproduction and scope

RimWorld 1.7 only. The fixture is compiled into the isolated validation assembly,
never into the distributed mod. Hardware: AMD Ryzen 5 1600, 12 logical processors.

```powershell
./validation/AnimalBarn17/run.ps1 -Run -Benchmark -BenchmarkLabel before
# After changing the barn, use the exact same saved animals and map:
./validation/AnimalBarn17/run.ps1 -Run -Benchmark -BenchmarkLabel after -ReuseBenchmarkSave
```

Read `%LOCALAPPDATA%/Temp/ZoologyBarnPerformance17/barn-performance.txt` until
`COMPLETE performance`. Each previous report is rotated before a new run. The
script stops only an earlier instance with this exact isolated profile.

The benchmark creates a single 250 x 250 map, clears other pawns and items, and
builds a 96 x 96 outdoor fenced pen with a vanilla pen marker, mature grass and
1,000 player-owned female chickens aged one year. All chickens start inside the
fence. Their food and rest levels vary deterministically to avoid synchronizing
all feeding jobs. Storyteller incidents are disabled for the fixture.

At the default capacity of 32.5 adult BodySize, seven barns hold 162 chickens
each, except the last. Each starts with 18 full, real kibble stacks: 9,450 units
in total. In the free-roaming branch the same stacks are on the ground and the
chickens choose food and grazing through their ordinary AI. In the packed branch
real animals are admitted at the entrance and the same stacks transferred into
their real owners. Empty barns also exist in the free branch to retain the same
background buildings. There are no handlers or feed hauling in either branch.

Both branches use **the same mod list and settings**, including Zoology. The
free branch measures ordinary map-animal behavior with Zoology installed, not a
Core-only installation. Other Zoology behavior is held constant between branches.

A common base save supplies the same pawn IDs, age, health, food, map and tick.
The order is free / packed / packed / free, reloading the base before each repeat.
Each branch warms up for 1,500 game ticks and measures twelve windows of 500 ticks
through `TickManager.DoSingleTick`, including native map, world, AI, pathfinding,
needs and health work. This is not a synthetic loop of only the barn method.
The game remains paused between windows; elapsed tick work gives simulation
throughput without the game's normal speed cap or rendering wait.

Dynamic rendering is measured separately over 120 real paused frames through
`DynamicDrawManager.DrawDynamicThings`, at the same camera location and zoom.
It measures CPU dynamic-render preparation/submission, including native work;
it does not measure the full frame or GPU, and must not be reported as FPS.
Allocation figures cover the calling thread. Tick measurements exclude save/load,
generation, packing, profiling hooks and these paused rendering samples.

The fixture checks survival, containment and lack of malnutrition, and records
remaining kibble, grass, nutrition, AI jobs and actual visible residents. The
free branch may graze and spends time walking/eating; equal remaining food is
therefore not a performance-test requirement. Physiology/ingestion equivalence
has separate controlled tests in `BarnValidation.cs`.

Section profiling uses a separate 1,500-tick diagnostic window after timing.
Inclusive times overlap and must not be summed. The placement-cache test also
compares selected pawn identities and exact positions with the original grid
search by clearing only the new failed-shape cache before each placement. It
does not lower the drawing limit, hide food or alter native sprite scale.

The optimization stores failed `(BodySize, graphic width, graphic height)` shapes
only during one refresh. Occupancy and tile loads only increase during that
refresh, so a failed shape cannot become placeable later. The cache clears every
refresh; growth, changed graphics, deaths, admissions and removals are reconsidered
normally. Runtime physiological, food, health, reproduction and fire schedules
are unchanged.
