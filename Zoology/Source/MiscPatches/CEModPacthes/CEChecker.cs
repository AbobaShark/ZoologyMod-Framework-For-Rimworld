// CEChecker.cs
using System;
using HarmonyLib;
using Verse;

namespace ZoologyMod
{
    public static class CEChecker
    {
        // package id Combat Extended по общепринятому идентификатору
        private const string CE_PACKAGE_ID = "CETeam.CombatExtended";

        /// <summary>
        /// Надёжно определяет — установлен ли Combat Extended.
        /// Сначала пробует по типу (быстро), затем — по package id (ModLister).
        /// Если ModLister.GetActiveModWithIdentifier недоступен — просто возвращает результат по типу.
        /// </summary>
        public static bool IsCEInstalled()
        {
            try
            {
                // Быстрая проверка по типу (наиболее надёжная и быстрая)
                bool byType = AccessTools.TypeByName("CombatExtended.StatWorker_MeleeArmorPenetration") != null
                              || AccessTools.TypeByName("CombatExtended.Verb_MeleeAttackCE") != null;

                if (byType) return true;

                // Попробуем проверить наличие активного мода по идентификатору (ModLister может не содержать этот метод в старых сборках)
                try
                {
                    var mod = ModLister.GetActiveModWithIdentifier(CE_PACKAGE_ID);
                    if (mod != null) return true;
                }
                catch
                {
                    // если ModLister.GetActiveModWithIdentifier не доступен или упало — игнорируем
                }

                // Ничего не найдено
                return false;
            }
            catch
            {
                // В худшем случае — считаем, что CE не установлен (без бросания исключений)
                return false;
            }
        }
    }
}
