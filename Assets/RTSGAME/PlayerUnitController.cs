// PlayerUnitController.cs
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.EventSystems;

public class PlayerUnitController : MonoBehaviour
{
    [Header("References")]
    public Camera playerCamera;
    public RectTransform selectionBoxVisual;

    [Header("Layer Masks")]
    public LayerMask unitLayerMask;   // Både spelarens OCH fiendens enheter bör ligga på detta lager för att kunna klickas
    public LayerMask groundLayerMask;
    public LayerMask enemyLayerMask; // *** NY: Ett lager specifikt för fiender (eller använd Tags/TeamID)

    // Internal State
    private List<Unit> selectedUnits = new List<Unit>();
    private Vector2 startDragPosition;

    void Awake()
    {
        if (playerCamera == null)
        {
            Debug.LogWarning("Player Camera not assigned, attempting to use Camera.main.", this);
            playerCamera = Camera.main;
        }
        if (selectionBoxVisual != null)
            selectionBoxVisual.gameObject.SetActive(false);
        else
            Debug.LogWarning("Selection Box Visual is not assigned.", this);
    }

    void Update()
    {
        HandleMouseInput();
    }

    void HandleMouseInput()
    {
        // --- Vänster Musknapp Ner ---
        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current.IsPointerOverGameObject()) return; // Blocka UI-klick
            startDragPosition = Input.mousePosition;
        }

        // --- Vänster Musknapp Hålls ---
        if (Input.GetMouseButton(0) && !EventSystem.current.IsPointerOverGameObject())
        {
            if (Vector2.Distance(startDragPosition, Input.mousePosition) > 5f)
            {
                UpdateSelectionBox(Input.mousePosition);
            }
        }

        // --- Vänster Musknapp Släppt ---
        if (Input.GetMouseButtonUp(0))
        {
            if (EventSystem.current.IsPointerOverGameObject())
            {
                if (selectionBoxVisual != null) selectionBoxVisual.gameObject.SetActive(false);
                return; // Blocka UI-klick
            }

            if (selectionBoxVisual != null && selectionBoxVisual.gameObject.activeInHierarchy)
            {
                ReleaseSelectionBox(); // Avsluta drag-markering
            }
            else
            {
                if (Vector2.Distance(startDragPosition, Input.mousePosition) <= 5f)
                {
                    HandleSingleClickSelection(); // Hantera enkelklick
                }
                else
                {
                    if (selectionBoxVisual != null) selectionBoxVisual.gameObject.SetActive(false);
                }
            }
        }

        // --- Höger Musknapp Ner ---
        if (Input.GetMouseButtonDown(1))
        {
            if (EventSystem.current.IsPointerOverGameObject()) return; // Blocka UI-klick
            IssueCommand(); // *** Byt namn från MoveSelectedUnits
        }
    }


    void HandleSingleClickSelection()
    {
        if (selectionBoxVisual != null) selectionBoxVisual.gameObject.SetActive(false);

        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        Debug.DrawRay(ray.origin, ray.direction * 1000f, Color.yellow, 2.0f);

        if (Physics.Raycast(ray, out hit, 1000f, unitLayerMask)) // Raycasta mot Unit Layer
        {
            Debug.Log("Raycast Hit: " + hit.collider.name + " on layer " + LayerMask.LayerToName(hit.collider.gameObject.layer));
            Unit unit = hit.collider.GetComponent<Unit>();
            if (unit != null && unit.teamID == 0) // *** Kolla om vi hittade en Unit OCH att den tillhör spelaren (teamID 0)
            {
                Debug.Log("Found Player Unit component on " + hit.collider.name);
                bool isShiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (!isShiftHeld) { ClearSelection(); }
                ToggleUnitSelection(unit); // Använd Toggle för både add/remove med Shift
            }
            else if (unit != null && unit.teamID != 0)
            {
                Debug.Log("Clicked on an Enemy unit - ignoring for selection.");
                // Klick på fiende vid enkelklick gör inget (högerklick ger attackorder)
                bool isShiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (!isShiftHeld) ClearSelection(); // Avmarkera om vi klickar på fiende utan shift
            }
            else
            {
                Debug.LogError("Raycast hit object '" + hit.collider.name + "' on Unit layer, but GetComponent<Unit>() failed!", hit.collider.gameObject);
                bool isShiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (!isShiftHeld) ClearSelection();
            }
        }
        else
        {
            Debug.Log("Raycast did NOT hit anything on the Unit layer.");
            bool isShiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!isShiftHeld) { ClearSelection(); } // Avmarkera vid klick på tom yta
        }
    }

    void ReleaseSelectionBox()
    {
        if (selectionBoxVisual == null) return;
        selectionBoxVisual.gameObject.SetActive(false);

        Rect selectionRect = new Rect(
            Mathf.Min(startDragPosition.x, Input.mousePosition.x),
            Mathf.Min(startDragPosition.y, Input.mousePosition.y),
            Mathf.Abs(Input.mousePosition.x - startDragPosition.x),
            Mathf.Abs(Input.mousePosition.y - startDragPosition.y)
        );

        bool isShiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (!isShiftHeld) { ClearSelection(); }

        Unit[] allUnits = FindObjectsOfType<Unit>(); // *** OBS: Ineffektivt för många enheter!
        Debug.Log($"Found {allUnits.Length} units in scene to check.");

        foreach (Unit unit in allUnits)
        {
            if (unit == null || unit.teamID != 0) continue; // Hoppa över fiender eller null units vid box select

            Vector3 screenPos = playerCamera.WorldToScreenPoint(unit.transform.position);
            bool isInRect = selectionRect.Contains(screenPos, true);
            bool isInFront = screenPos.z > 0;
            // Debug.Log($"Checking Unit: {unit.gameObject.name} at screen pos {screenPos}. InFront: {isInFront}, InRect: {isInRect}. Rect was: {selectionRect}");

            if (isInFront && isInRect)
            {
                SelectUnit(unit); // SelectUnit hanterar redan Contains-check
            }
        }
        Debug.Log("Box selection finished. Selected count: " + selectedUnits.Count);
    }


    // *** Ny metod för att ge kommandon baserat på högerklick ***
    void IssueCommand()
    {
        if (selectedUnits.Count == 0) return; // Inga enheter valda

        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        // Prioritera att träffa en fiende först
        if (Physics.Raycast(ray, out hit, 2000f, enemyLayerMask)) // *** Använd ENEMY Layer Mask här! ***
        {
            Unit targetUnit = hit.collider.GetComponent<Unit>();
            // Kolla om det verkligen är en fiende (ifall enemyLayerMask även innehåller annat)
            if (targetUnit != null && targetUnit.teamID != 0) // Antag att teamID 0 är spelaren
            {
                Debug.Log($"Attack command issued to {selectedUnits.Count} units. Target: {hit.collider.name}");
                foreach (Unit unit in selectedUnits)
                {
                    if (unit != null) unit.OrderAttackTarget(hit.transform); // Ge attackorder
                }
                return; // Kommandot hanterat
            }
            // Om vi träffade något på Enemy layer som inte var en giltig fiende-unit, fortsätt nedåt...
        }

        // Om vi inte träffade en fiende, kolla om vi träffade marken
        if (Physics.Raycast(ray, out hit, 2000f, groundLayerMask))
        {
            Vector3 destination = hit.point;
            Debug.Log($"Move command issued to {selectedUnits.Count} units. Destination: {destination}");
            foreach (Unit unit in selectedUnits)
            {
                if (unit != null) unit.OrderMoveTo(destination); // Ge flyttorder
            }
            return; // Kommandot hanterat
        }

        // Om vi varken träffade fiende eller mark
        Debug.Log("Right-click command ignored: Raycast did not hit enemy or ground layer.");
    }


    // --- Helper Methods --- (SelectUnit, ToggleUnitSelection, ClearSelection, UpdateSelectionBox)

    void UpdateSelectionBox(Vector2 currentMousePosition) // Oförändrad
    {
        if (selectionBoxVisual == null) return;
        if (!selectionBoxVisual.gameObject.activeInHierarchy) selectionBoxVisual.gameObject.SetActive(true);
        float width = currentMousePosition.x - startDragPosition.x;
        float height = currentMousePosition.y - startDragPosition.y;
        selectionBoxVisual.pivot = new Vector2(width < 0 ? 1 : 0, height < 0 ? 1 : 0);
        selectionBoxVisual.anchoredPosition = startDragPosition;
        selectionBoxVisual.sizeDelta = new Vector2(Mathf.Abs(width), Mathf.Abs(height));
    }

    void SelectUnit(Unit unit) // Oförändrad (nästan, lade till null check)
    {
        if (unit != null && unit.teamID == 0 && !selectedUnits.Contains(unit)) // Se till att vi bara väljer spelarens enheter
        {
            selectedUnits.Add(unit);
            unit.Select();
            // Debug.Log($"SelectedUnits count: {selectedUnits.Count}");
        }
    }

    void ToggleUnitSelection(Unit unit) // Oförändrad (nästan, lade till null check och team check)
    {
        if (unit == null || unit.teamID != 0) return; // Kan bara toggle spelarens enheter

        if (selectedUnits.Contains(unit))
        {
            selectedUnits.Remove(unit);
            unit.Deselect();
        }
        else
        {
            selectedUnits.Add(unit);
            unit.Select();
        }
        // Debug.Log($"SelectedUnits count: {selectedUnits.Count}");
    }

    void ClearSelection() // Oförändrad
    {
        if (selectedUnits.Count > 0)
        {
            // Debug.Log("Clearing selection. Count was: " + selectedUnits.Count);
            foreach (Unit unit in selectedUnits)
            {
                if (unit != null) unit.Deselect();
            }
            selectedUnits.Clear();
        }
    }
    // MoveSelectedUnits borttagen, ersatt av IssueCommand
}