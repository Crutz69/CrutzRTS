// Assets/RTSGAME/Scripts/GameData/DamageMatrix.cs
using UnityEngine;
using System.Collections.Generic;

namespace RTSGAME
{
    [System.Serializable]
    public struct DamageInteraction
    {
        public DamageType damageType;
        public ArmorType armorType;
        [Tooltip("Multiplier (e.g., 1.0 = 100%, 0.5 = 50%, 1.5 = 150%)")]
        public float modifier;
    }

    [CreateAssetMenu(fileName = "NewDamageMatrix", menuName = "RTS Game/Damage Matrix")]
    public class DamageMatrix : ScriptableObject
    {
        public List<DamageInteraction> interactions = new List<DamageInteraction>();
        private Dictionary<(DamageType, ArmorType), float> lookupTable;
        private bool isInitialized = false;

        private void InitializeLookup()
        {
            lookupTable = new Dictionary<(DamageType, ArmorType), float>();
            foreach (var interaction in interactions)
            {
                lookupTable[(interaction.damageType, interaction.armorType)] = interaction.modifier;
            }
            isInitialized = true;
        }
        public float GetModifier(DamageType damageType, ArmorType armorType)
        {
            if (!isInitialized) { InitializeLookup(); }
            if (lookupTable.TryGetValue((damageType, armorType), out float modifier))
            {
                return modifier;
            }
            return 1.0f; // Default 100%
        }
    }
}