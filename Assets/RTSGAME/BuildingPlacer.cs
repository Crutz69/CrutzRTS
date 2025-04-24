using UnityEngine;
using Mirror; // För NetworkPlayer referens och Cmd

public class BuildingPlacer : MonoBehaviour
{
    private bool isPlacing = false;
    private UnitData buildingToPlaceData; // Data för byggnaden (prefab, ghost, size etc.)
    private GameObject currentGhostInstance;
    private NetworkPlayer localPlayer; // Referens till den lokala spelarens script

    private bool isShiftDragging = false;
    private Vector3 dragStartPositionWorld;
    private List<GameObject> dragGhostInstances = new List<GameObject>(); // För att visa flera ghosts vid drag

    void Start()
    {
        // Hitta den lokala spelaren (kan göras på bättre sätt)
        localPlayer = NetworkClient.localPlayer?.GetComponent<NetworkPlayer>();
    }

    void Update()
    {
        if (!isPlacing || buildingToPlaceData == null || localPlayer == null)
        {
            // Städa upp eventuella ghosts om vi inte placerar längre
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
        Vector3 targetPosition = GetMouseWorldPosition(); // Din funktion för detta

        // Uppdatera ghost(s) position och validitet
        UpdateGhostVisuals(targetPosition);

        // Hantera Input
        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        // Starta Shift+Dra
        if (shiftHeld && Input.GetMouseButtonDown(0) && CanPlaceAt(targetPosition)) // Kolla validitet vid start
        {
            isShiftDragging = true;
            dragStartPositionWorld = targetPosition; // Spara startposition
            // Initialt, lägg bara till en ghost vid start? Eller vänta tills drag?
        }

        // Uppdatera Shift+Dra
        if (isShiftDragging && Input.GetMouseButton(0) && shiftHeld)
        {
            // Logik för att räkna ut grid/line-positioner mellan dragStartPositionWorld och targetPosition
            // Uppdatera dragGhostInstances listan och deras positioner/validitet
        }

        // Släpp Shift+Dra
        if (isShiftDragging && Input.GetMouseButtonUp(0)) // Kolla om Shift *var* nere när vi släppte
        {
            isShiftDragging = false;
            // Hämta alla valida positioner från dragGhostInstances
            List<Vector3> validPlacementPositions = GetValidDragPositions();
            // Skicka kommando för varje position
            foreach (Vector3 pos in validPlacementPositions)
            {
                localPlayer.CmdPlaceBuilding(buildingToPlaceData.unitId, pos, Quaternion.identity); // Anpassa rotation
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
            if (CanPlaceAt(targetPosition))
            {
                localPlayer.CmdPlaceBuilding(buildingToPlaceData.unitId, targetPosition, Quaternion.identity); // Anpassa rotation

                // Om Shift INTE hålls nere, avsluta placeringsläget
                if (!shiftHeld)
                {
                    CancelPlacement();
                }
                // Om Shift hålls nere, gör ingenting (stanna kvar i läget för nästa klick)
            }
        }
    }

    // --- Hjälpfunktioner ---

    public void StartPlacement(UnitData buildingData)
    {
        if (buildingData == null) return;
        buildingToPlaceData = buildingData;
        isPlacing = true;
        // Skapa den första ghosten (om den inte finns)
        if (currentGhostInstance == null && buildingData.ghostPrefab != null) // Antag att UnitData har ghostPrefab
        {
            currentGhostInstance = Instantiate(buildingData.ghostPrefab);
        }
        Debug.Log($"Started placing: {buildingData.unitName}");
    }

    public void CancelPlacement()
    {
        isPlacing = false;
        isShiftDragging = false;
        buildingToPlaceData = null;
        CleanupGhosts();
        Debug.Log("Placement cancelled.");
    }

    private void CleanupGhosts()
    {
        if (currentGhostInstance != null) Destroy(currentGhostInstance);
        CleanupDragGhosts();
    }
    private void CleanupDragGhosts()
    {
        foreach (var ghost in dragGhostInstances) Destroy(ghost);
        dragGhostInstances.Clear();
    }


    private Vector3 GetMouseWorldPosition()
    {
        // Din kod för att raycasta från musen till markplanet (LayerMask etc.)
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, LayerMask.GetMask("Ground"))) // Exempel Layer "Ground"
        {
            return hit.point;
        }
        return Vector3.zero; // Eller annan felhantering
    }

    private void UpdateGhostVisuals(Vector3 targetPosition)
    {
        if (isShiftDragging)
        {
            // Uppdatera position/validitet för alla ghosts i dragGhostInstances
            // och currentGhostInstance kanske döljs eller är den första i draget.
            if (currentGhostInstance) currentGhostInstance.SetActive(false); // Dölj singel-ghost vid drag
            // ... logik för att uppdatera dragGhostInstances ...
        }
        else if (currentGhostInstance != null)
        {
            // Uppdatera position för singel-ghosten
            currentGhostInstance.SetActive(true);
            currentGhostInstance.transform.position = targetPosition; // Lägg till ev. grid snapping här
            // Ändra ghostens färg/material baserat på CanPlaceAt(targetPosition)
            bool canPlace = CanPlaceAt(targetPosition);
            SetGhostColor(currentGhostInstance, canPlace);
        }
    }

    private List<Vector3> GetValidDragPositions()
    {
        // Gå igenom dragGhostInstances, returnera positionerna för de som är valida
        List<Vector3> validPositions = new List<Vector3>();
        // ... logik ...
        return validPositions;
    }

    private bool CanPlaceAt(Vector3 position)
    {
        // Din kod för att kolla om positionen är giltig
        // - Kollision med andra byggnader/enheter (Physics.CheckBox eller OverlapBox)
        // - Terrängtyp (kolla terränglager, NavMesh?)
        // - Närhet till andra byggnader?
        // - Inom "ägt" område?
        return true; // Ersätt med riktig logik
    }

    private void SetGhostColor(GameObject ghost, bool canPlace)
    {
        // Ändra materialets färg på ghosten (t.ex. grön för OK, röd för blockerad)
        var renderer = ghost.GetComponentInChildren<Renderer>(); // Eller annan metod
        if (renderer != null)
        {
            renderer.material.color = canPlace ? Color.green : Color.red; // Kräver ett material som stödjer färgändring
        }
    }
}