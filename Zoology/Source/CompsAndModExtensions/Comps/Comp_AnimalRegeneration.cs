// Comp_AnimalRegeneration.cs

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // CompProperties — настраивается в XML
    public class CompProperties_AnimalRegeneration : CompProperties
    {
        // Hediff'ы для трёх стадий (можно указывать null — тогда стадия игнорируется)
        public HediffDef hediffBaby;
        public HediffDef hediffJuvenile;
        public HediffDef hediffAdult;

        // Пороги в долях от race.baseBodySize (например 0.2, 0.5, 1.0)
        public float babyFraction = 0.2f;
        public float juvenileFraction = 0.5f;
        public float adultFraction = 1.0f;

        // Интервал проверки в тиках
        public int checkIntervalTicks = 720;

        public CompProperties_AnimalRegeneration()
        {
            this.compClass = typeof(Comp_AnimalRegeneration);
        }
    }

    // Сам компонент, помещаем в AnimalRegeneration.cs
    public class Comp_AnimalRegeneration : ThingComp
    {
        private CompProperties_AnimalRegeneration Props => (CompProperties_AnimalRegeneration)this.props;

        // Стандартные значения (в случае некорректных в XML)
        private const float DefaultBabyFraction = 0.2f;
        private const float DefaultJuvenileFraction = 0.5f;
        private const float DefaultAdultFraction = 1.0f;
        private const int MinIntervalTicks = 60;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            ValidateAndFixFractions();
            if (Props.checkIntervalTicks < MinIntervalTicks)
            {
                Props.checkIntervalTicks = MinIntervalTicks;
            }
        }

        // Проверяем и корректируем дроби; логируем если были некорректности
        private void ValidateAndFixFractions()
        {
            if (Props == null) return;

            bool needFix = false;

            float b = Props.babyFraction;
            float j = Props.juvenileFraction;
            float a = Props.adultFraction;

            // Любая дробь должна быть > 0 (нулевая/отрицательная бессмысленна).
            if (!(b > 0f) || !(j > 0f) || !(a > 0f))
            {
                needFix = true;
            }

            // Также необходимо, чтобы b < j < a (строго возрастающие), иначе — фиксим.
            if (!(b < j && j < a))
            {
                needFix = true;
            }

            if (needFix)
            {
                Log.Warning($"[Zoology] Comp_AnimalRegeneration: invalid fraction(s) in CompProperties for {parent?.def?.defName ?? "unknown"}. Replacing with defaults ({DefaultBabyFraction}, {DefaultJuvenileFraction}, {DefaultAdultFraction}).");
                Props.babyFraction = DefaultBabyFraction;
                Props.juvenileFraction = DefaultJuvenileFraction;
                Props.adultFraction = DefaultAdultFraction;
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            if (Props == null) return;
            if (!parent.IsHashIntervalTick(Props.checkIntervalTicks)) return;

            Pawn pawn = parent as Pawn;
            if (pawn == null) return;
            if (pawn.Dead) return;
            if (pawn.health?.hediffSet == null) return;

            // Получаем базовый размер расы (baseBodySize). За счёт этого пороги одинаковы для всех экземпляров расы.
            float baseBodySize = 1.0f;
            try
            {
                baseBodySize = pawn.RaceProps?.baseBodySize ?? 1.0f;
                if (baseBodySize <= 0f) baseBodySize = 1.0f;
            }
            catch
            {
                baseBodySize = 1.0f;
            }

            // Вычисляем абсолютные пороги в тех же единицах, что и pawn.BodySize
            float babyThreshold = baseBodySize * Props.babyFraction;
            float juvenileThreshold = baseBodySize * Props.juvenileFraction;
            float adultThreshold = baseBodySize * Props.adultFraction;

            float currentBodySize = pawn.BodySize;

            // Определяем целевой hediff (приоритет сверху вниз)
            HediffDef targetDef = null;
            if (Props.hediffAdult != null && currentBodySize >= adultThreshold)
            {
                targetDef = Props.hediffAdult;
            }
            else if (Props.hediffJuvenile != null && currentBodySize >= juvenileThreshold)
            {
                targetDef = Props.hediffJuvenile;
            }
            else if (Props.hediffBaby != null && currentBodySize >= babyThreshold)
            {
                targetDef = Props.hediffBaby;
            }
            else
            {
                targetDef = null;
            }

            // Текущий реген хеддиф (если есть один из настроенных) — выбираем любой из трёх если присутствует.
            Hediff current = GetCurrentRegenerationHediff(pawn);

            // Если текущий отличается от целевого — меняем
            if ((current == null && targetDef != null) || (current != null && current.def != targetDef))
            {
                if (current != null)
                {
                    try
                    {
                        pawn.health.RemoveHediff(current);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[Zoology] Comp_AnimalRegeneration: failed to remove existing regen hediff {current.def?.defName} from {pawn.LabelShort}: {e}");
                    }
                }

                if (targetDef != null)
                {
                    try
                    {
                        var newH = HediffMaker.MakeHediff(targetDef, pawn);
                        pawn.health.AddHediff(newH);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[Zoology] Comp_AnimalRegeneration: failed to add regen hediff {targetDef.defName} to {pawn.LabelShort}: {e}");
                    }
                }
            }

            // Иначе — ничего не делаем (чтобы не спамить)
        }

        // Ищем любой из настроенных hediff'ов на pawn
        private Hediff GetCurrentRegenerationHediff(Pawn pawn)
        {
            if (pawn == null || pawn.health?.hediffSet == null) return null;

            var hediffs = pawn.health.hediffSet.hediffs;
            if (hediffs == null || hediffs.Count == 0) return null;

            var configured = new HashSet<HediffDef>();
            if (Props.hediffBaby != null) configured.Add(Props.hediffBaby);
            if (Props.hediffJuvenile != null) configured.Add(Props.hediffJuvenile);
            if (Props.hediffAdult != null) configured.Add(Props.hediffAdult);

            foreach (var h in hediffs)
            {
                if (h?.def != null && configured.Contains(h.def)) return h;
            }

            return null;
        }
    }
}
