// Filnamn: BuildingPlacer.cs
// Uppdaterad version med korrekt placering av groundLayerMask

using UnityEngine;
using Mirror; // För NetworkPlayer referens och Cmd
using System.Collections.Generic; // För List<>
using RTSGAME; // <-- Lägg till denna om BuildableData/Enums finns i namespacet

public class BuildingPlacer : MonoBehaviour
{
    private bool isPlacing = false;
    private BuildableData buildingToPlaceData; // Använder BuildableData
    private GameObject currentGhostInstance;
    private NetworkPlayer localPlayer; // Referens till den lokala spelarens script

    private bool isShiftDragging = false;
    private Vector3 dragStartPositionWorld;
    private List<GameObject> dragGhostInstances = new List<GameObject>(); // För att visa flera ghosts vid drag

    // *** FLYTTAD HIT: *** Deklareras nu som ett fält i klassen
    [Tooltip("Layer(s) som representerar marken där musen ska träffa.")]
    [SerializeField] private LayerMask groundLayerMask = 1; // Sätt korrekt lager i inspektorn!

    void Start()
    {
        // Hitta den lokala spelaren (kan göras på bättre sätt)
        localPlayer = NetworkClient.localPlayer?.GetComponent<NetworkPlayer>();
    }

    void Update()
    {
        // Hitta spelaren igen om den saknas (enkel fallback)
        if (localPlayer == null && NetworkClient.active)
        {
            localPlayer = NetworkClient.localPlayer?.GetComponent<NetworkPlayer>();
        }

        if (!isPlacing || buildingToPlaceData == null || localPlayer == null)
        {
            CleanupGhosts();
            return;
        }

        // Avbryt med högerklick eller Escape
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
        {
            CancelPlacement();
            return;
        }

        // Hämta musposition på marken
        Vector3 targetPosition = GetMouseWorldPosition(); // Använder nu klassfältet groundLayerMask internt

        // Uppdatera ghost(s) position och validitet
        UpdateGhostVisuals(targetPosition); // Innehåller SnapToGrid och CanPlaceAt

        // Hantera Input
        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        // Starta Shift+Dra
        if (shiftHeld && Input.GetMouseButtonDown(0))
        {
            // Kolla validitet för startpositionen för drag
            Vector3 snappedStartPos = SnapToGrid(targetPosition);
            if (CanPlaceAt(snappedStartPos)) // TODO: Implementera CanPlaceAt
            {
                isShiftDragging = true;
                dragStartPositionWorld = snappedStartPos; // Spara snäppt startposition
                                                          // TODO: Initial logik för drag? Visa första drag-ghost?
            }
            else { /* Spela ljud "kan inte starta här"? */ }
        }

        // Uppdatera Shift+Dra
        if (isShiftDragging && Input.GetMouseButton(0) && shiftHeld)
        {
            // TODO: Logik för att räkna ut grid/line-positioner mellan dragStartPositionWorld och targetPosition
            // TODO: Uppdatera dragGhostInstances listan och deras positioner/validitet
        }

        // Släpp Shift+Dra
        if (isShiftDragging && Input.GetMouseButtonUp(0)) // Kolla om Shift *var* nere när vi släppte
        {
            isShiftDragging = false;
            // Hämta alla valida positioner från dragGhostInstances
            List<Vector3> validPlacementPositions = GetValidDragPositions(); // TODO: Implementera
            // Skicka kommando för varje position
            foreach (Vector3 pos in validPlacementPositions)
            {
                localPlayer.CmdPlaceBuilding(buildingToPlaceData.buildableId, pos, Quaternion.identity); // Anpassa rotation
            }
            // Stanna kvar i placeringsläge om Shift fortfarande hålls nere?
            if (!shiftHeld)
            {
                CancelPlacement(); // Avsluta om Shift släpptes
            }
            else
            {
                CleanupDragGhosts(); // Ta bort drag-ghosts men fortsätt placera (Shift-klick logik)
            }
        }

        // Normalt Klick eller Shift+Klick (om vi inte precis avslutat en drag)
        if (!isShiftDragging && Input.GetMouseButtonDown(0))
        {
            Vector3 snappedPlacementPos = SnapToGrid(targetPosition);
            if (CanPlaceAt(snappedPlacementPos)) // TODO: Implementera CanPlaceAt
            {
                localPlayer.CmdPlaceBuilding(buildingToPlaceData.buildableId, snappedPlacementPos, Quaternion.identity); // Anpassa rotation

                // Om Shift INTE hålls nere, avsluta placeringsläget
                if (!shiftHeld)
                {
                    CancelPlacement();
                }
                // Om Shift hålls nere, gör ingenting (stanna kvar i läget för nästa klick)
            }
            else
            {
                // TODO: Spela upp ljud "kan inte placera här"? Visa feedback?
            }
        }
    }

    // --- Hjälpfunktioner ---

    public void StartPlacement(BuildableData buildingData)
    {
        // Säkerställ att det faktiskt är en byggnad vi försöker placera
        if (buildingData == null || buildingData.itemType != BuildableItemType.Building)
        {
            Debug.LogError($"StartPlacement called with invalid data or non-building item: {buildingData?.displayName}");
            CancelPlacement();
            return;
        }

        // Kolla om spelaren har råd (enkel klient-check för feedback)
        if (localPlayer != null && localPlayer.credits < buildingData.creditCost)
        {
            Debug.Log($"Cannot start placement: Not enough credits for {buildingData.displayName}.");
            // TODO: Visa detta i UI? Spela ljud?
            CancelPlacement(); // Avbryt om inte råd
            return;
        }

        buildingToPlaceData = buildingData;
        isPlacing = true;

        // Skapa den första ghosten (om den inte finns)
        if (currentGhostInstance == null && buildingData.ghostPrefab != null)
        {
            currentGhostInstance = Instantiate(buildingData.ghostPrefab);
            currentGhostInstance.SetActive(false); // Starta som inaktiv tills musen är över giltigt område
        }
        else if (buildingData.ghostPrefab == null)
        {
            Debug.LogWarning($"BuildableData '{buildingData.displayName}' is missing a Ghost Prefab!");
        }

        Debug.Log($"Started placing: {buildingData.displayName}");
    }

    public void CancelPlacement()
    {
        isPlacing = false;
        isShiftDragging = false;
        buildingToPlaceData = null;
        CleanupGhosts();
    }

    private void CleanupGhosts()
    {
        if (currentGhostInstance != null) Destroy(currentGhostInstance);
        currentGhostInstance = null;
        CleanupDragGhosts();
    }
    private void CleanupDragGhosts()
    {
        foreach (var ghost in dragGhostInstances) Destroy(ghost);
        dragGhostInstances.Clear();
    }

    // Använder nu klassfältet groundLayerMask
    private Vector3 GetMouseWorldPosition()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        // Variabeln deklareras INTE här längre
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayerMask)) // Använder klassfältet
        {
            return hit.point;
        }
        return Vector3.positiveInfinity; // Returnera ogiltig position vid misslyckande
    }

    private void UpdateGhostVisuals(Vector3 targetPosition)
    {
        if (targetPosition == Vector3.positiveInfinity)
        { // Om musen inte träffar marken
            if (currentGhostInstance) currentGhostInstance.SetActive(false);
            // TODO: Dölj även drag ghosts?
            return;
        }

        if (isShiftDragging)
        {
            // TODO: Uppdatera position/validitet för alla ghosts i dragGhostInstances
            if (currentGhostInstance) currentGhostInstance.SetActive(false); // Dölj singel-ghost vid drag
        }
        else if (currentGhostInstance != null)
        {
            currentGhostInstance.SetActive(true); // Visa ghosten om den var dold
            Vector3 snappedPosition = SnapToGrid(targetPosition);
            currentGhostInstance.transform.position = snappedPosition;

            bool canPlace = CanPlaceAt(snappedPosition); // TODO: Implementera CanPlaceAt
            SetGhostColor(currentGhostInstance, canPlace);
        }
    }

    // Exempel på Grid Snapping (justera efter behov)
    private Vector3 SnapToGrid(Vector3 originalPosition)
    {
        float gridSize = 1.0f; // Storlek på rutnätet (bör kanske vara konfigurerbart?)
        return new Vector3(
            Mathf.Round(originalPosition.x / gridSize) * gridSize,
            originalPosition.y, // Behåll Y från raycast för att följa marken?
            Mathf.Round(originalPosition.z / gridSize) * gridSize
        );
    }


    private List<Vector3> GetValidDragPositions()
    {
        // TODO: Gå igenom dragGhostInstances, returnera positionerna för de som är valida
        List<Vector3> validPositions = new List<Vector3>();
        Debug.LogWarning("GetValidDragPositions() needs implementation!");
        return validPositions;
    }

    private bool CanPlaceAt(Vector3 position)
    {
        // TODO: Implementera din riktiga valideringslogik här!
        // Exempel (pseudo-kod):
        // Vector3 boxSize = buildingToPlaceData.size; // Kräver att BuildableData har storlek
        // if (Physics.CheckBox(position + Vector3.up * boxSize.y / 2f, boxSize / 2f, Quaternion.identity, collisionCheckLayers)) { return false; } // Kollar kollision
        // if (!IsOnValidTerrain(position)) { return false; } // Kollar terräng/NavMesh
        // if (!IsWithinPlacementZone(position)) { return false; } // Kollar tillåtet område
        // if (localPlayer.credits < buildingToPlaceData.creditCost) { return false; } // Kolla resurser (för ghost-färg)

        Debug.LogWarning("CanPlaceAt() needs implementation!");
        return true; // Tillfällig returvärde
    }

    private void SetGhostColor(GameObject ghost, bool canPlace)
    {
        var renderer = ghost.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            // TODO: Använd MaterialPropertyBlock för bättre prestanda vid många ghosts
            renderer.material.color = canPlace ? Color.green : Color.red;
        }
    }
}