// LifeStageUtility.cs
using RimWorld;
using Verse;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace ZoologyMod
{
    public static class LifeStageUtility
    {
        // флаг, чтобы предупреждение о пустом DefDatabase выводилось только один раз
        private static bool warnedNoDefsOnce = false;

        /// <summary>
        /// Возвращает LifeStagePenetrationDef для pawn (или null).
        /// Возвращает только значения из DefDatabase; если дефов нет — возвращается null.
        /// Если DefDatabase пуст и Prefs.DevMode == true — выводится однократное предупреждение в лог.
        /// </summary>
        public static LifeStagePenetrationDef GetPenetrationDefForPawn(Pawn pawn)
        {
            if (pawn == null) return null;
            var stage = pawn.ageTracker?.CurLifeStage;
            if (stage == null) return null;
            string stageName = stage.defName;

            // Получаем все определённые дефы (обычный путь — через XML)
            var all = DefDatabase<LifeStagePenetrationDef>.AllDefsListForReading;
            if (all.Count == 0)
            {
                // Однократно логируем отсутствие дефов в DevMode — это помогает моддерам понять, почему нет эффекта.
                if (Prefs.DevMode && !warnedNoDefsOnce)
                {
                    warnedNoDefsOnce = true;
                    Log.Warning("[Zoology] No LifeStagePenetrationDef defs found in DefDatabase. Zoology will not replace life-stage penetration multipliers until XML defs are provided.");
                }
                return null;
            }

            // 1) точное совпадение по defName
            var byDef = all.FirstOrDefault(d => string.Equals(d.defName, stageName, StringComparison.Ordinal));
            if (byDef != null) return byDef;

            // 2) совпадение по имени, игнорируя регистр
            var byDefIgnore = all.FirstOrDefault(d => string.Equals(d.defName, stageName, StringComparison.OrdinalIgnoreCase));
            if (byDefIgnore != null) return byDefIgnore;

            // 3) совпадение по label (игнорируя регистр)
            var byLabel = all.FirstOrDefault(d => string.Equals(d.label, stageName, StringComparison.OrdinalIgnoreCase));
            if (byLabel != null) return byLabel;

            // 4) частичное совпадение (defName ⊂ stageName или наоборот)
            var partial = all.FirstOrDefault(d =>
                (!string.IsNullOrEmpty(d.defName) && stageName.IndexOf(d.defName, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrEmpty(stageName) && d.defName.IndexOf(stageName, StringComparison.OrdinalIgnoreCase) >= 0));
            if (partial != null) return partial;

            // Если не найден — возвращаем null (без встроенных значений)
            return null;
        }
    }
}