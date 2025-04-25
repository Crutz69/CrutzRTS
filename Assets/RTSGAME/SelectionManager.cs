// Assets/RTSGAME/Scripts/Managers/SelectionManager.cs
using System.Collections.Generic;
using UnityEngine;
using Mirror; // Behövs för NetworkIdentity
using System.Linq; // För Select och FirstOrDefault

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
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject); // Singleton bör ofta överleva scenbyten
            }
            else
            {
                Destroy(gameObject);
                return; // Avbryt om en instans redan finns
            }
            // if (selectionBoxVisual != null) selectionBoxVisual.gameObject.SetActive(false); // Dölj från start
        }

        void Update()
        {
            // TODO: Hantera logik för att rita/uppdatera box selection rectangle om musknapp hålls nere
            // Se tidigare kod för exempel
        }

        public void HandleClickSelection(GameObject clickedObject, bool additive)
        {
            if (clickedObject == null) // Säkerhetskoll
            {
                if (!additive) ClearSelection();
                return;
            }

            // Försök hitta NetworkIdentity på det klickade objektet eller dess förälder
            NetworkIdentity identity = clickedObject.GetComponentInParent<NetworkIdentity>();
            if (identity == null)
            {
                // Klickade på något icke-nätverksanslutet (terräng, dekoration?)
                if (!additive) ClearSelection();
                return;
            }

            GameObject objectToSelect = identity.gameObject;

            // TODO: Lägg till filter här? Får denna enhet/byggnad väljas av den här spelaren?
            // Behöver access till localPlayer NetId, t.ex. via InputManager.Instance.GetLocalPlayerNetId() eller PlayerManager
            // if (!IsSelectableByLocalPlayer(objectToSelect)) return;

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
                    // TODO: Begränsa max antal valda enheter? (t.ex. max 1 byggnad, max X enheter)
                    // if (CanAddObjectToSelection(objectToSelect)) {
                    AddObjectToSelection(objectToSelect);
                    // }
                }
            }
            OnSelectionChanged?.Invoke(); // Meddela att valet ändrats
        }

        public void HandleBoxSelection(Rect selectionBox) // Antag att Rect är i skärmkoordinater
        {
            bool additive = Keyboard.current.shiftKey.isPressed; // Stöd för Shift-box?

            // Rensa bara om vi INTE kör additivt
            if (!additive)
            {
                ClearSelection();
            }

            List<GameObject> newlySelected = new List<GameObject>();

            // Hitta alla Unit i scenen (effektivare alternativ kan vara att ha en central lista i t.ex. PlayerManager)
            // Viktigt: Använd FindObjectsByType, FindObjectsOfType är föråldrad
            Unit[] allUnits = FindObjectsByType<Unit>(FindObjectsSortMode.None);

            foreach (Unit unit in allUnits)
            {
                // TODO: Implementera IsSelectableByLocalPlayer-check
                // if (IsSelectableByLocalPlayer(unit.gameObject))
                // {
                Vector3 screenPos = Camera.main.WorldToScreenPoint(unit.transform.position);
                // Kolla om inom skärmen (z > 0) och inom rektangeln
                if (screenPos.z > 0 && selectionBox.Contains(screenPos))
                {
                    newlySelected.Add(unit.gameObject);
                }
                // }
            }

            // TODO: Lägg till logik för att välja byggnader med box också?
            // Building[] allBuildings = FindObjectsByType<Building>(FindObjectsSortMode.None);
            // foreach (Building building in allBuildings) { ... }


            // Lägg till alla hittade objekt (med additiv logik om Shift hölls nere)
            foreach (var obj in newlySelected)
            {
                if (additive)
                {
                    if (selectedObjects.Contains(obj)) RemoveObjectFromSelection(obj); // Om additiv och redan vald -> ta bort
                    else AddObjectToSelection(obj); // Om additiv och inte vald -> lägg till
                }
                else
                {
                    AddObjectToSelection(obj); // Om inte additiv, lägg bara till (rensades tidigare)
                }
            }

            // Meddela bara om något faktiskt ändrades (nya lades till/togs bort)
            if (newlySelected.Count > 0) // Kan förfinas för att kolla om _faktisk_ ändring skett
            {
                OnSelectionChanged?.Invoke();
            }
        }


        public void AddObjectToSelection(GameObject obj)
        {
            if (obj != null && !selectedObjects.Contains(obj))
            {
                // TODO: Implementera begränsningar här igen?
                // if (!CanAddObjectToSelection(obj)) return;

                selectedObjects.Add(obj);
                // Anropa Select-metoden på objektets relevanta komponent (Unit eller Building)
                // för att t.ex. visa selection circle/highlight
                obj.GetComponent<ISelectable>()?.Select(); // Om du använder ett Interface
                // Eller specifikt:
                // obj.GetComponent<Building>()?.Select();
                // obj.GetComponent<Unit>()?.Select();
            }
        }

        public void RemoveObjectFromSelection(GameObject obj)
        {
            if (obj != null && selectedObjects.Contains(obj))
            {
                // Anropa Deselect på samma sätt som Select
                obj.GetComponent<ISelectable>()?.Deselect();
                // obj.GetComponent<Building>()?.Deselect();
                // obj.GetComponent<Unit>()?.Deselect();
                selectedObjects.Remove(obj);
            }
        }


        public void ClearSelection()
        {
            // Anropa Deselect() på alla nuvarande valda objekt
            for (int i = selectedObjects.Count - 1; i >= 0; i--) // Iterera baklänges säkrare
            {
                if (selectedObjects[i] != null) // Kan vara null om objekt förstörts medan valt
                {
                    selectedObjects[i].GetComponent<ISelectable>()?.Deselect();
                    // selectedObjects[i].GetComponent<Building>()?.Deselect();
                    // selectedObjects[i].GetComponent<Unit>()?.Deselect();
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
            return selectedObjects
                .Where(obj => obj != null && obj.TryGetComponent<Unit>(out _)) // Filtrera för Units
                .Select(obj => obj.GetComponent<NetworkIdentity>()) // Plocka ut NetworkIdentity
                .Where(id => id != null) // Säkerställ att NetworkIdentity finns
                .ToList(); // Skapa listan
        }

        // ---- NY METOD TILLAGD HÄR ----
        /// <summary>
        /// Returns a list of Network IDs (uint) from all selected GameObjects that have both a Unit and a NetworkIdentity component.
        /// Denna metod behövs av InputManager för att skicka kommandon som CmdAttackTarget och CmdMoveUnits.
        /// </summary>
        public List<uint> GetSelectedUnitNetIds()
        {
            List<uint> unitNetIds = new List<uint>();
            foreach (var obj in selectedObjects)
            {
                // Objektet måste finnas, ha en Unit-komponent och en NetworkIdentity-komponent
                if (obj != null &&
                    obj.TryGetComponent<Unit>(out _) &&
                    obj.TryGetComponent<NetworkIdentity>(out var netIdentity))
                {
                    unitNetIds.Add(netIdentity.netId); // Lägg till Network ID (uint) i listan
                }
            }
            return unitNetIds;

            // Alternativ med LINQ:
            // return selectedObjects
            //     .Where(obj => obj != null && obj.TryGetComponent<Unit>(out _))
            //     .Select(obj => obj.GetComponent<NetworkIdentity>())
            //     .Where(id => id != null)
            //     .Select(id => id.netId)
            //     .ToList();
        }
        // ---- SLUT PÅ NY METOD ----


        /// <summary>
        /// Returns the first selected GameObject, or null if none are selected.
        /// </summary>
        public GameObject GetFirstSelectedObject()
        {
            return selectedObjects.FirstOrDefault();
        }

        /// <summary>
        /// Checks if a specific GameObject is currently in the selection list.
        /// </summary>
        public bool IsSelected(GameObject obj)
        {
            if (obj == null) return false;
            return selectedObjects.Contains(obj);
        }

        /// <summary>
        /// Clears the current selection and selects a single specified GameObject.
        /// </summary>
        public void SelectSingleObject(GameObject objectToSelect)
        {
            if (objectToSelect == null)
            {
                ClearSelection();
                return;
            }

            NetworkIdentity identity = objectToSelect.GetComponentInParent<NetworkIdentity>();
            if (identity == null)
            {
                ClearSelection();
                return;
            }

            // TODO: Lägg till samma filter här som i HandleClickSelection?
            // if (!IsSelectableByLocalPlayer(identity.gameObject)) { ClearSelection(); return; }

            ClearSelection();
            AddObjectToSelection(identity.gameObject);
            OnSelectionChanged?.Invoke();
            Debug.Log($"Selected single object: {identity.gameObject.name}");
        }

        // TODO: Fler getters efter behov (t.ex. GetFirstSelectedBuilding, GetPrimarySelectedBuildingIdentity)
        /*
        public NetworkIdentity GetPrimarySelectedBuildingIdentity()
        {
             GameObject firstSelected = GetFirstSelectedObject();
             if(firstSelected != null && firstSelected.TryGetComponent<Building>(out _) && firstSelected.TryGetComponent<NetworkIdentity>(out var id))
             {
                 return id;
             }
             return null;
        }
        */

        // TODO: Hjälpmetod för att kolla om ett objekt får väljas av den lokala spelaren
        // private bool IsSelectableByLocalPlayer(GameObject obj)
        // {
        //     if (obj == null) return false;
        //     // Hämta localPlayerNetId
        //     uint? localPlayerNetId = InputManager.Instance?.GetLocalPlayer()?.netId;
        //     if (localPlayerNetId == null) return false; // Kan inte avgöra utan lokal spelare

        //     if (obj.TryGetComponent<Unit>(out Unit unit))
        //     {
        //         return unit.ownerNetId == localPlayerNetId; // Kan bara välja egna enheter?
        //     }
        //     if (obj.TryGetComponent<Building>(out Building building))
        //     {
        //         return building.ownerNetId == localPlayerNetId; // Kan bara välja egna byggnader?
        //     }
        //     // Andra typer av valbara objekt? Resurser? Neutrala?
        //     return false; // Default: ej valbar
        // }

        // TODO: Hjälpmetod för att kolla om ett objekt kan läggas till i nuvarande selektion
        // private bool CanAddObjectToSelection(GameObject objToAdd)
        // {
        //     if (selectedObjects.Count == 0) return true; // Alltid ok att lägga till det första

        //     bool alreadyHasBuilding = selectedObjects.Any(obj => obj != null && obj.TryGetComponent<Building>(out _));
        //     bool addingBuilding = objToAdd.TryGetComponent<Building>(out _);

        //     if(alreadyHasBuilding && addingBuilding) return false; // Kan inte välja flera byggnader
        //     if(alreadyHasBuilding && !addingBuilding) return false; // Kan inte blanda byggnad och enheter? (Designval)
        //     if(!alreadyHasBuilding && addingBuilding) return false; // Kan inte blanda enheter och byggnad? (Designval)

        //     // TODO: Max antal enheter?
        //     // if (!addingBuilding && selectedObjects.Count >= MAX_UNIT_SELECTION) return false;

        //     return true; // Ok att lägga till (enhet till enheter)
        // }

    } // End class SelectionManager
} // End namespace RTSGAME