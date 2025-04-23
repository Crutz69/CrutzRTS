// Assets/RTSGAME/Scripts/Managers/SelectionManager.cs
using System.Collections.Generic;
using UnityEngine;
using Mirror; // Behövs för NetworkIdentity
using System.Linq; // För Select i GetSelectedUnitsNetworkIdentities

namespace RTSGAME
{
    public class SelectionManager : MonoBehaviour
    {
        public static SelectionManager Instance { get; private set; }

        // Lista som håller de för närvarande valda objekten (privat för kontroll)
        private readonly List<GameObject> selectedObjects = new List<GameObject>();

        // Event för när selektionen ändras (bra för UI att lyssna på)
        public event System.Action OnSelectionChanged;

        // TODO: Lägg till variabler för box selection visuals (t.ex. en UI Image för rektangeln)
        // private RectTransform selectionBoxVisual;
        // private Vector2 startDragPos;

        void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
            // if (selectionBoxVisual != null) selectionBoxVisual.gameObject.SetActive(false); // Dölj från start
        }

        void Update()
        {
            // TODO: Hantera logik för att rita/uppdatera box selection rectangle om musknapp hålls nere
            // Exempel på box-logik (kräver Input System eller gammal Input):
            /*
            if (Input.GetMouseButtonDown(0)) { // Vänsterklick ner
                 // Ignorera om över UI
                 if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
                 startDragPos = Input.mousePosition;
                 // selectionBoxVisual?.gameObject.SetActive(true); // Visa rektangeln
            }
            if (Input.GetMouseButton(0)) { // Vänsterklick hålls nere
                 if (selectionBoxVisual == null || !selectionBoxVisual.gameObject.activeSelf) return; // Om inte startat korrekt

                 // Uppdatera rektangelns storlek/position
                  Vector2 currentMousePos = Input.mousePosition;
                  Vector2 min = Vector2.Min(startDragPos, currentMousePos);
                  Vector2 max = Vector2.Max(startDragPos, currentMousePos);
                  // selectionBoxVisual.position = min;
                  // selectionBoxVisual.sizeDelta = max - min;
            }
            if (Input.GetMouseButtonUp(0)) { // Vänsterklick upp
                 if (selectionBoxVisual == null || !selectionBoxVisual.gameObject.activeSelf) return; // Om inte startat korrekt
                 // selectionBoxVisual.gameObject.SetActive(false); // Dölj rektangeln

                 // Om boxen är väldigt liten, behandla som klick
                  float minDragDist = 5f; // Minsta drag för att räknas som box
                 if (Vector2.Distance(startDragPos, Input.mousePosition) < minDragDist) {
                      // Simulera klick vid musens position (redan hanterat av InputManager?)
                      // InputManager kanske ska hantera om det är klick eller box färdig?
                 } else {
                      // Utför box selection
                      Rect screenRect = new Rect(selectionBoxVisual.position.x, selectionBoxVisual.position.y, selectionBoxVisual.sizeDelta.x, selectionBoxVisual.sizeDelta.y);
                      HandleBoxSelection(screenRect);
                 }
            }
            */
        }

        public void HandleClickSelection(GameObject clickedObject, bool additive)
        {
            NetworkIdentity identity = clickedObject.GetComponentInParent<NetworkIdentity>();
            if (identity == null)
            {
                if (!additive) ClearSelection();
                return;
            }

            GameObject objectToSelect = identity.gameObject;

            // TODO: Lägg till filter här? Får denna enhet/byggnad väljas av den här spelaren?
            // Unit unit = objectToSelect.GetComponent<Unit>();
            // Building building = objectToSelect.GetComponent<Building>();
            // if (unit != null && !IsUnitSelectableByLocalPlayer(unit)) return;
            // if (building != null && !IsBuildingSelectableByLocalPlayer(building)) return;


            if (!additive)
            {
                ClearSelection();
                AddObjectToSelection(objectToSelect);
            }
            else
            {
                if (selectedObjects.Contains(objectToSelect))
                {
                    RemoveObjectFromSelection(objectToSelect);
                }
                else
                {
                    AddObjectToSelection(objectToSelect);
                }
            }
            OnSelectionChanged?.Invoke(); // Meddela att valet ändrats
        }

        public void HandleBoxSelection(Rect selectionBox) // Antag att Rect är i skärmkoordinater
        {
            // Rensa inte nödvändigtvis här om vi ska kunna box-selecta additivt? (Kräver Shift-koll)
            ClearSelection(); // Rensa för nuvarande implementation

            // TODO: Hitta alla valbara objekt inom selectionBox
            List<GameObject> newlySelected = new List<GameObject>();

            // Gammal rad:
            // Unit[] allUnits = FindObjectsOfType<Unit>();

            // Ny, rekommenderad rad:
            Unit[] allUnits = FindObjectsByType<Unit>(FindObjectsSortMode.None); // <-- Byt till denna

            foreach (Unit unit in allUnits)
            {
                // ... (resten av din logik för att kolla ägarskap och om enheten är inom selectionBox) ...
                // if (IsUnitSelectableByLocalPlayer(unit)) { // Behöver IsUnitSelectable... metod
                //     Vector3 screenPos = Camera.main.WorldToScreenPoint(unit.transform.position);
                //     if(screenPos.z > 0 && selectionBox.Contains(screenPos)) {
                //         newlySelected.Add(unit.gameObject);
                //     }
                // }
            }

            // Lägg till alla hittade objekt
            foreach (var obj in newlySelected)
            {
                AddObjectToSelection(obj);
            }

            if (newlySelected.Count > 0)
            {
                OnSelectionChanged?.Invoke(); // Meddela bara om något faktiskt valdes
            }
        }


        public void AddObjectToSelection(GameObject obj)
        {
            if (obj != null && !selectedObjects.Contains(obj))
            {
                selectedObjects.Add(obj);
                obj.GetComponent<Building>()?.Select();
                obj.GetComponent<Unit>()?.Select();
                // OnSelectionChanged anropas efter hela operationen (klick/box)
            }
        }

        public void RemoveObjectFromSelection(GameObject obj)
        {
            if (obj != null && selectedObjects.Contains(obj))
            {
                obj.GetComponent<Building>()?.Deselect();
                obj.GetComponent<Unit>()?.Deselect();
                selectedObjects.Remove(obj);
                // OnSelectionChanged anropas efter hela operationen
            }
        }


        public void ClearSelection()
        {
            // Anropa Deselect() på alla nuvarande valda objekt
            for (int i = selectedObjects.Count - 1; i >= 0; i--) // Iterera baklänges säkrare vid borttagning (även om vi använder Clear)
            {
                if (selectedObjects[i] != null) // Kan vara null om objekt förstörts medan valt
                {
                    selectedObjects[i].GetComponent<Building>()?.Deselect();
                    selectedObjects[i].GetComponent<Unit>()?.Deselect();
                }
            }
            selectedObjects.Clear();
            OnSelectionChanged?.Invoke(); // Meddela att valet ändrats (till tomt)
        }

        // --- Getters för andra script ---

        /// <summary>
        /// Returns a new list containing the currently selected GameObjects.
        /// </summary>
        public List<GameObject> GetSelectedObjects()
        {
            // Returnera en Kopia för att förhindra extern modifiering av originallistan
            return new List<GameObject>(selectedObjects);
        }

        /// <summary>
        /// Returns a list of NetworkIdentity components from all selected Units.
        /// </summary>
        public List<NetworkIdentity> GetSelectedUnitsNetworkIdentities()
        {
            List<NetworkIdentity> units = new List<NetworkIdentity>();
            foreach (var obj in selectedObjects)
            {
                // Använd TryGetComponent för säkerhet och prestanda
                if (obj != null && obj.TryGetComponent<Unit>(out _) && obj.TryGetComponent<NetworkIdentity>(out var id))
                {
                    units.Add(id);
                }
            }
            return units;
        }

        /// <summary>
        /// Returns the first selected GameObject, or null if none are selected.
        /// </summary>
        public GameObject GetFirstSelectedObject()
        {
            // Använd Linq för enklare kod (lägg till using System.Linq; högst upp)
            return selectedObjects.FirstOrDefault();
            // Eller traditionell:
            // return selectedObjects.Count > 0 ? selectedObjects[0] : null;
        }

        // ---- HÄR ÄR DEN TILLAGDA METODEN ----
        /// <summary>
        /// Checks if a specific GameObject is currently in the selection list.
        /// </summary>
        /// <param name="obj">The GameObject to check.</param>
        /// <returns>True if the object is selected, false otherwise.</returns>
        public bool IsSelected(GameObject obj)
        {
            if (obj == null) return false;
            return selectedObjects.Contains(obj);
        }
        // ---- SLUT PÅ TILLAGD METOD ----


        // TODO: Fler getters efter behov (t.ex. GetFirstSelectedWorkerIdentity, GetFirstSelectedHarvesterIdentity etc.)
        /*
        public NetworkIdentity GetFirstSelectedHarvesterIdentity() {
             foreach (var obj in selectedObjects) {
                  if (obj != null && obj.TryGetComponent<HarvesterUnit>(out _) && obj.TryGetComponent<NetworkIdentity>(out var id)) {
                       return id;
                  }
             }
             return null;
        }
        */

    } // End class SelectionManager
} // End namespace RTSGAME