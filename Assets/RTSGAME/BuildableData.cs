using UnityEngine;

// Definiera typerna - lägg gärna detta i en egen fil, t.ex. Enums.cs
public enum BuildableItemType { Unit, Building, Upgrade }
public enum BuildingType { None, House, Shield, Cavalry, Infantry, Archer, Flying, Golem } // Matcha dina kategorier

[CreateAssetMenu(fileName = "NewBuildable", menuName = "CrutzRTS/Buildable Data")]
public class BuildableData : ScriptableObject
{
    [Header("Identification")]
    public string buildableName = "New Item"; // Namn som visas i UI (tooltip?)
    public string buildableId;   // Unik identifierare för nätverkskommandon & SyncLists
    public BuildableItemType itemType = BuildableItemType.Unit; // *** Viktigt: Sätt rätt typ! ***
    public BuildingType category = BuildingType.House; // Vilken byggkategori tillhör den (för filtrering)

    [Header("UI")]
    public Sprite icon; // Ikon som visas på knappen

    [Header("Costs & Time")]
    public int creditCost = 100;
    // public int manaCost = 0; // BORTTAGEN - Mana är upkeep för byggnader
    public float buildTime = 10f; // Byggtid för enhet / Forskningstid för uppgradering

    [Header("Prefabs & Requirements")]
    public GameObject prefabToBuild; // Faktisk enhet/BYGGNADS-prefab (för byggnader, detta är den FÄRDIGA byggnaden)
    public GameObject ghostPrefab;   // Ghost/Blueprint prefab (ENDAST för byggnader där itemType == Building)
    // public List<BuildableData> prerequisites; // Lista på andra BuildableData som krävs (TODO)
    // public bool requiresTechTier = 1; // TODO: Lägg till krav

    [Header("Gameplay - Set Correctly!")]
    public bool isUnlockedInitially = true; // Är den upplåst från start? (Används för att styra knappens state)
    public int queueBatchAmount = 5; // Antal som köas med Shift+Click (för Units/Upgrades)

    // Du kan lägga till fler variabler här:
    // - Beskrivningstext för tooltip
    // - Statistik (för units/buildings)
    // - Effekt (för upgrades)
}