// Filnamn: BuildableData.cs
// Placeras förslagsvis i Assets/RTSGAME/ScriptableObjects/Data/ eller liknande

using UnityEngine;

// *** Lägg till using för din namespace om Enums finns där ***
// (Behövs egentligen inte om denna fil OCKSÅ ligger i samma namespace)
// using RTSGAME; // Kommentera in om nödvändigt, men klassen nedan ligger nu i RTSGAME

namespace RTSGAME // *** Lades till för att matcha resten av koden ***
{
    

    [CreateAssetMenu(fileName = "NewBuildable", menuName = "CrutzRTS/Buildable Data")]
    public class BuildableData : ScriptableObject
    {
        [Header("Identification")]
        [Tooltip("Namn som visas i UI (tooltip, knapp etc.)")]
        public string displayName = "New Item"; // Ändrade från buildableName för tydlighet mot UI

        [Tooltip("Unik sträng-identifierare (används internt, för nätverk etc.)")]
        public string buildableId; // MÅSTE vara unikt för varje BuildableData asset!

        [Tooltip("Vilken grundtyp är detta? (Unit, Building, Upgrade)")]
        public BuildableItemType itemType = BuildableItemType.Unit; // Använder enum från Enums.cs

        [Tooltip("Vilken UI-kategori tillhör den? (För filtrering i byggmenyn)")]
        public BuildingType category = BuildingType.None; // Använder enum från Enums.cs

        [Header("UI")]
        [Tooltip("Ikon som visas på knappen")]
        public Sprite icon;
        [TextArea] public string description = "Item Description."; // För tooltips

        [Header("Costs & Time")]
        [Tooltip("Kostnad i Credits")]
        public int creditCost = 100;
        // public int manaCost = 0; // Borttagen, mana hanteras via upkeep/generation

        [Tooltip("Produktionstid (för Units/Upgrades) eller Konstruktionstid (för Buildings)")]
        public float buildTime = 10f;

        [Header("Prefabs & Requirements")]
        [Tooltip("Prefab som ska spawnas (Färdig Unit/Byggnad, eller GameObj för Upgrade-logik?)")]
        public GameObject prefabToBuild; // För Units: Enhetens prefab. För Buildings: Den *färdiga* byggnadens prefab. För Upgrades: Kan vara null eller ett logik-objekt.

        [Tooltip("Ghost/Blueprint prefab som visas vid placering (ENDAST FÖR Buildings!)")]
        public GameObject ghostPrefab;   // Ska bara användas om itemType == BuildableItemType.Building

        [Tooltip("Byggarbetsplats-prefab som spawnas först (ENDAST FÖR Buildings!)")]
        public GameObject constructionSitePrefab; // *** Lades till! Ska bara användas om itemType == BuildableItemType.Building ***

        // TODO: Implementera prerequisites om det behövs
        // public List<BuildableData> prerequisites; // Lista på andra BuildableData som krävs
        // public int requiredTechTier = 1;

        [Header("Gameplay")]
        [Tooltip("Är denna upplåst från start? (Påverkar om knappen är klickbar initialt)")]
        public bool isUnlockedInitially = true;

        [Tooltip("Antal som köas med Shift+Click (för Units/Upgrades)")]
        public int queueBatchAmount = 5;

        // --- Validering i Editor (valfritt men bra) ---
        protected virtual void OnValidate()
        {
            // Säkerställ att buildableId inte är tomt (viktigt!)
            if (string.IsNullOrWhiteSpace(buildableId))
            {
                // Försök generera ett ID från namnet om möjligt (ta bort mellanslag etc.)
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    // Enkel generering: ta bort mellanslag och gör till gemener
                    buildableId = displayName.Replace(" ", "").ToLowerInvariant();
#if UNITY_EDITOR
                    // Markera asset som "dirty" så ändringen sparas
                    UnityEditor.EditorUtility.SetDirty(this);
#endif
                    Debug.Log($"Generated buildableId '{buildableId}' from displayName '{displayName}' for {this.name}. Please verify uniqueness.", this);
                }
                else
                {
                    Debug.LogError($"BuildableData asset '{this.name}' MUST have a unique 'Buildable Id' set!", this);
                }
            }

            // Nollställ prefabs som inte är relevanta för typen
            if (itemType != BuildableItemType.Building)
            {
                if (ghostPrefab != null)
                {
                    //Debug.LogWarning($"Ghost Prefab is only used for Buildings. Clearing for '{this.name}'.", this);
                    //ghostPrefab = null; // Nollställ automatiskt? Kan vara irriterande.
                }
                if (constructionSitePrefab != null)
                {
                    //Debug.LogWarning($"Construction Site Prefab is only used for Buildings. Clearing for '{this.name}'.", this);
                    //constructionSitePrefab = null;
                }
            }
            if (itemType != BuildableItemType.Unit && itemType != BuildableItemType.Upgrade)
            {
                if (queueBatchAmount != 1 && queueBatchAmount != 5) // Återställ till default om inte Unit/Upgrade?
                {
                    //queueBatchAmount = 1;
                }
            }
        }

    } // Slut på klass BuildableData
} // Slut på namespace RTSGAME