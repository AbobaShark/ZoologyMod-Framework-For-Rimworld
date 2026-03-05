// Comp_AnimalClotting.cs

using System.Collections.Generic;
using Verse;
using RimWorld;

namespace ZoologyMod
{
    // Компоновка свойств — можно настраивать в XML
    public class CompProperties_AnimalClotting : CompProperties
    {
        // Интервал проверки в тиках (по умолчанию 360)
        public int checkInterval = 360;

        // Диапазон качества лечения, передаётся в Hediff.Tended
        public FloatRange tendingQuality = new FloatRange(0.2f, 0.7f);

        public CompProperties_AnimalClotting()
        {
            this.compClass = typeof(Comp_AnimalClotting);
        }
    }

    // Сам компонент — выполняется на экземпляре Pawn
    public class Comp_AnimalClotting : ThingComp
    {
        private CompProperties_AnimalClotting Props => (CompProperties_AnimalClotting)this.props;

        public override void CompTick()
        {
            // Проводим работу только по расписанию (экономия)
            if (!parent.IsHashIntervalTick(Props.checkInterval)) return;

            Pawn pawn = parent as Pawn;
            if (pawn == null) return;
            if (pawn.Dead) return;
            if (pawn.health?.hediffSet == null) return;

            // Опционально: можно ограничить только животными
            // if (!pawn.RaceProps.Animal) return;

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            if (hediffs == null || hediffs.Count == 0) return;

            // Берём одно случайное качество на весь проход — чтобы не рандомить для каждой раны
            float quality = Props.tendingQuality.RandomInRange;
            float maxQuality = Props.tendingQuality.TrueMax;

            // Проходим в обратном порядке на случай удаления/модификации в процессе
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                Hediff h = hediffs[i];
                if (h == null) continue;

                // если хеддиф кровоточит — помечаем как подлеченное (уменьшает кровотечение)
                if (h.Bleeding)
                {
                    // Tended(quality, maxQuality, batch = 1)
                    // В оригинале метод может иметь перегрузки — этот вызов соответствует обычной сигнатуре Hediff.Tended
                    h.Tended(quality, maxQuality, 1);
                }
            }
        }
    }
}