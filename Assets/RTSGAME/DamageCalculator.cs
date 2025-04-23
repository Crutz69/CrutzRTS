// Assets/RTSGAME/Scripts/Combat/DamageCalculator.cs
using UnityEngine;

namespace RTSGAME
{
    public static class DamageCalculator
    {
        private static DamageMatrix _damageMatrix;

        // Metod för att ladda/hämta Damage Matrix Asset
        private static DamageMatrix GetMatrix()
        {
            if (_damageMatrix == null)
            {
                // Viktigt: Skapa asset-filen enligt Steg 6 och placera i Resources/GameData
                _damageMatrix = Resources.Load<DamageMatrix>("GameData/DefaultDamageMatrix");
                if (_damageMatrix == null) Debug.LogError("Damage Matrix asset 'DefaultDamageMatrix' not found in Resources/GameData!");
            }
            return _damageMatrix;
        }

        // Beräknar slutlig skada
        public static float CalculateDamage(float baseDamage, DamageType damageType, ArmorType targetArmorType)
        {
            DamageMatrix matrix = GetMatrix();
            // Returnera grundskada om matrisen inte kunde laddas
            if (matrix == null) return Mathf.Max(0, baseDamage);

            float modifier = matrix.GetModifier(damageType, targetArmorType);
            float finalDamage = baseDamage * modifier;

            // Debug.Log($"Damage Calc: Base={baseDamage}, {damageType} vs {targetArmorType} => Modifier={modifier}, Final={finalDamage}");
            return Mathf.Max(0, finalDamage); // Aldrig negativ skada
        }
    }
}