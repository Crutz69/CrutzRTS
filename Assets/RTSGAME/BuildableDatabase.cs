// Filnamn: BuildableDatabase.cs
using UnityEngine;
using System.Collections.Generic; // För att kunna använda List<>
using System.Linq;               // För att kunna använda LINQ för filtrering (Where, FirstOrDefault)
using RTSGAME;                   // *** VIKTIGT: Lade till denna rad! ***

// Detta attribut gör att du kan skapa asset-filen via Unity-menyn:
// Assets -> Create -> CrutzRTS -> Buildable Database
[CreateAssetMenu(fileName = "BuildableDatabase", menuName = "CrutzRTS/Buildable Database")]
public class BuildableDatabase : ScriptableObject // Viktigt att den ärver från ScriptableObject
{
    // Detta är huvudlistan.
    // I Unity-inspektorn, på det asset du skapar från detta script,
    // drar du ALLA dina BuildableData-assets (WorkerUnitData, TownhallBuildingData etc.) hit.
    [Tooltip("Lista med alla byggbara enheter, byggnader och uppgraderingar i spelet.")]
    public List<BuildableData> allBuildables;

    /// <summary>
    /// Hämtar en lista med alla BuildableData som tillhör en specifik kategori.
    /// Används t.ex. för att fylla BuildablesPanel när en kategori väljs.
    /// </summary>
    /// <param name="category">Kategorin (BuildingType enum från RTSGAME namespace) att filtrera på.</param>
    /// <returns>En lista med matchande BuildableData.</returns>
    // Nu när 'using RTSGAME;' finns, refererar 'BuildingType' här korrekt till din enum.
    public List<BuildableData> GetBuildablesForCategory(BuildingType category)
    {
        // Säkerhetskoll ifall listan inte är initierad
        if (allBuildables == null)
        {
            Debug.LogError("BuildableDatabase 'allBuildables' list is null!");
            return new List<BuildableData>(); // Returnera en tom lista
        }

        // Använder LINQ för att filtrera listan
        // Väljer ut alla 'b' där 'b.category' är samma som den inskickade 'category'
        // Detta förutsätter nu att b.category i BuildableData också är av typen RTSGAME.BuildingType
        List<BuildableData> result = allBuildables
            .Where(b => b != null && b.category == category) // Säkerställ att 'b' inte är null
            .ToList(); // Gör om resultatet till en Lista

        // TODO (Avancerat): Lägg eventuellt till ytterligare filtrering här
        // baserat på om spelaren har låst upp teknologin/kraven för varje 'b'.
        // Detta kräver tillgång till spelarens data.
        // Exempel: .Where(b => IsUnlocked(b, localPlayerData))

        return result;
    }

    /// <summary>
    /// Hämtar en specifik BuildableData baserat på dess unika string ID.
    /// Användbart när du t.ex. får ett ID från nätverket (SyncList<string>)
    /// och behöver få tag på all data för det ID:t.
    /// </summary>
    /// <param name="buildableId">Det unika ID:t att söka efter.</param>
    /// <returns>Matchande BuildableData, eller null om det inte hittades.</returns>
    public BuildableData GetDataById(string buildableId)
    {
        if (allBuildables == null || string.IsNullOrEmpty(buildableId))
        {
            return null; // Ingen lista eller inget ID att söka på
        }

        // Använder LINQ för att hitta det första elementet 'b' där 'b.buildableId' matchar
        BuildableData result = allBuildables
            .FirstOrDefault(b => b != null && b.buildableId == buildableId); // Jämför ID

        if (result == null)
        {
            Debug.LogWarning($"BuildableData with ID '{buildableId}' not found in the database.");
        }

        return result;
    }

    // TODO (Avancerat): Funktion för att kolla om en viss BuildableData är upplåst
    // private bool IsUnlocked(BuildableData data, PlayerData playerData) {
    //     if (!data.isUnlockedInitially) {
    //         // Kolla om spelaren har uppfyllt data.prerequisites eller data.requiresTechTier
    //         // Kräver implementation...
    //         return false; // Placeholder
    //     }
    //     return true;
    // }

} // Slut på klassen BuildableDatabase