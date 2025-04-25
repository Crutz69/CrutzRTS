// Assets/RTSGAME/Scripts/Managers/SelectionManager.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem; // För att kolla Shift/musknappar i Update
using UnityEngine.EventSystems; // För att kolla om pekare är över UI
using Mirror; // Behövs för NetworkIdentity
using System.Linq; // För Select och FirstOrDefault, Any, OfType, AddRange

namespace RTSGAME
{
    // OBS: ISelectable interface ska INTE definieras här längre!
    // Den ska ligga i en egen fil: ISelectable.cs

    public class SelectionManager : MonoBehaviour
    {
        public static SelectionManager Instance { get; private set; }

        [Header("Selection Settings")]
        [SerializeField] private LayerMask selectableLayerMask = Physics.DefaultRaycastLayers; // Vilka lager ska raycast träffa för val?
        public LayerMask SelectableLayerMask => selectableLayerMask; // Publik property
        [SerializeField] private float minDragDistanceForBox = 10f; // Hur långt måste man dra för att det ska bli en box?
        [SerializeField] private int maxUnitSelection = 50; // Max antal enheter som kan väljas samtidigt (0 = obegränsat)

        [Header("Visuals")]
        [Tooltip("Koppla en UI Image/Panel som har en RectTransform här för att visa markeringsrutan.")]
        [SerializeField] private RectTransform selectionBoxVisual; // Kopplas i Inspector

        // Lista som håller de för närvarande valda objekten (privat för kontroll)
        private readonly List<GameObject> selectedObjects = new List<GameObject>();
        private NetworkPlayer localPlayer; // Referens behövs för att kolla ägarskap

        // För box selection
        private Vector2 startDragPos;
        private bool isDragging = false;
        public bool IsDragging => isDragging; // Publik property


        // Event för när selektionen ändras (bra för UI att lyssna på)
        public event System.Action OnSelectionChanged;

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            if (selectionBoxVisual != null)
            {
                selectionBoxVisual.gameObject.SetActive(false); // Dölj från start
            }
            else
            {
                Debug.LogWarning("SelectionManager: Ingen 'selectionBoxVisual' RectTransform är kopplad i Inspectorn. Box selection kommer inte synas.");
            }
        }

        // Metod för att sätta den lokala spelaren (anropas från InputManager eller NetworkPlayer)
        public void SetLocalPlayer(NetworkPlayer player)
        {
            localPlayer = player;
        }

        void Update()
        {
            // Hantera box selection logic här
            HandleBoxSelectionInput();
        }

        private void HandleBoxSelectionInput()
        {
            if (Mouse.current == null) return;

            if (Mouse.current.leftButton.wasPressedThisFrame && !IsPointerOverUIObject())
            {
                startDragPos = Mouse.current.position.ReadValue();
                isDragging = true;
            }

            if (isDragging && Mouse.current.leftButton.isPressed)
            {
                Vector2 currentMousePos = Mouse.current.position.ReadValue();
                float distance = Vector2.Distance(startDragPos, currentMousePos);

                if (distance >= minDragDistanceForBox)
                {
                    if (selectionBoxVisual != null && !selectionBoxVisual.gameObject.activeSelf)
                    {
                        selectionBoxVisual.gameObject.SetActive(true);
                    }
                    if (selectionBoxVisual != null)
                    {
                        Vector2 min = Vector2.Min(startDragPos, currentMousePos);
                        Vector2 max = Vector2.Max(startDragPos, currentMousePos);
                        selectionBoxVisual.position = min;
                        selectionBoxVisual.sizeDelta = max - min;
                    }
                }
                else
                {
                    if (selectionBoxVisual != null && selectionBoxVisual.gameObject.activeSelf)
                    {
                        selectionBoxVisual.gameObject.SetActive(false);
                    }
                }
            }

            if (isDragging && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                isDragging = false;

                if (selectionBoxVisual != null && selectionBoxVisual.gameObject.activeSelf)
                {
                    selectionBoxVisual.gameObject.SetActive(false);
                    Rect screenRect = new Rect(selectionBoxVisual.position.x, selectionBoxVisual.position.y, selectionBoxVisual.sizeDelta.x, selectionBoxVisual.sizeDelta.y);
                    PerformBoxSelection(screenRect);
                }
                // Klick hanteras av InputManager när isDragging är false vid wasPressedThisFrame
            }
        }


        public void HandleClickSelection(GameObject clickedObject, bool additive)
        {
            if (clickedObject == null)
            {
                if (!additive) ClearSelection();
                return;
            }

            NetworkIdentity identity = clickedObject.GetComponentInParent<NetworkIdentity>();
            ISelectable selectable = clickedObject.GetComponentInParent<ISelectable>();

            if (identity == null || selectable == null)
            {
                if (!additive) ClearSelection();
                return;
            }

            GameObject objectToSelect = identity.gameObject;

            if (!IsSelectableByLocalPlayer(selectable))
            {
                // Debug.Log($"Cannot select object '{objectToSelect.name}' - not owned by local player.");
                if (!additive) ClearSelection();
                return;
            }


            if (!additive)
            {
                ClearSelection();
                if (CanAddObjectToSelection(objectToSelect))
                {
                    AddObjectToSelection(objectToSelect);
                }
            }
            else
            {
                if (selectedObjects.Contains(objectToSelect))
                {
                    RemoveObjectFromSelection(objectToSelect);
                }
                else
                {
                    if (CanAddObjectToSelection(objectToSelect))
                    {
                        AddObjectToSelection(objectToSelect);
                    }
                    else
                    {
                        // Debug.Log($"Cannot add '{objectToSelect.name}' to current selection (limit reached or type mismatch).");
                    }
                }
            }
            OnSelectionChanged?.Invoke();
        }

        // Metod för att utföra själva box-selekteringen
        private void PerformBoxSelection(Rect selectionBox)
        {
            bool additive = Keyboard.current.shiftKey.isPressed; // Kolla om Shift hålls nere

            // Rensa nuvarande val om inte additivt
            if (!additive)
            {
                ClearSelection();
            }

            List<GameObject> newlySelectedInBox = new List<GameObject>();

            // *** KORRIGERAD KOD FÖR ATT HITTA VALBARA OBJEKT (ALTERNATIV 2) ***
            // Skapa en tom lista för att samla alla ISelectable objekt
            List<ISelectable> selectablesList = new List<ISelectable>();
            // Hitta alla Units i scenen, filtrera för de som är ISelectable, lägg till i listan
            selectablesList.AddRange(FindObjectsByType<Unit>(FindObjectsSortMode.None).OfType<ISelectable>());
            // Hitta alla Buildings i scenen, filtrera för de som är ISelectable, lägg till i listan
            selectablesList.AddRange(FindObjectsByType<Building>(FindObjectsSortMode.None).OfType<ISelectable>());
            // Gör om den kombinerade listan till en array som resten av koden kan använda
            ISelectable[] allSelectables = selectablesList.ToArray();
            // *** SLUT PÅ KORRIGERING ***

            foreach (ISelectable selectable in allSelectables)
            {
                MonoBehaviour selectableComp = selectable as MonoBehaviour;
                if (selectableComp == null) continue;

                // Kolla ägarskap först
                if (!IsSelectableByLocalPlayer(selectable)) continue;

                // Kolla om det är en Unit (eller annan typ du vill kunna box-selecta)
                if (!(selectable is Unit)) continue; // Tillåt bara box-select på Units för nu?

                // Kolla om objektets position på skärmen är inom boxen
                Vector3 screenPos = Camera.main.WorldToScreenPoint(selectableComp.transform.position);
                if (screenPos.z > 0 && selectionBox.Contains(screenPos))
                {
                    newlySelectedInBox.Add(selectableComp.gameObject);
                }
            }

            // Gå igenom de objekt som hittades i boxen
            bool selectionActuallyChanged = false;
            foreach (var objToAdd in newlySelectedInBox)
            {
                bool couldAdd = false;
                if (additive)
                {
                    if (!selectedObjects.Contains(objToAdd) && CanAddObjectToSelection(objToAdd))
                    {
                        AddObjectToSelection(objToAdd);
                        couldAdd = true;
                    }
                    // Om additiv och redan vald -> gör inget (eller ta bort? nu gör inget)
                }
                else // Inte additiv
                {
                    if (CanAddObjectToSelection(objToAdd)) // Redan rensat, så bara kolla om det FÅR läggas till
                    {
                        AddObjectToSelection(objToAdd);
                        couldAdd = true;
                    }
                }
                if (couldAdd) selectionActuallyChanged = true;
            }

            // Meddela bara om något faktiskt lades till/ändrades
            if (selectionActuallyChanged)
            {
                OnSelectionChanged?.Invoke();
            }
        }


        public void AddObjectToSelection(GameObject obj)
        {
            if (obj != null && !selectedObjects.Contains(obj) && CanAddObjectToSelection(obj))
            {
                selectedObjects.Add(obj);
                obj.GetComponent<ISelectable>()?.Select(); // Använder Interface
            }
        }

        public void RemoveObjectFromSelection(GameObject obj)
        {
            if (obj != null && selectedObjects.Contains(obj))
            {
                obj.GetComponent<ISelectable>()?.Deselect(); // Använder Interface
                selectedObjects.Remove(obj);
            }
        }


        public void ClearSelection()
        {
            if (selectedObjects.Count == 0) return;

            for (int i = selectedObjects.Count - 1; i >= 0; i--)
            {
                if (selectedObjects[i] != null)
                {
                    selectedObjects[i].GetComponent<ISelectable>()?.Deselect();
                }
            }
            selectedObjects.Clear();
            OnSelectionChanged?.Invoke();
        }

        // --- Getters för andra script ---

        public List<GameObject> GetSelectedObjects()
        {
            return new List<GameObject>(selectedObjects); // Returnera kopia
        }

        // Behålls om UI behöver den
        public List<NetworkIdentity> GetSelectedUnitsNetworkIdentities()
        {
            return selectedObjects
                .Where(obj => obj != null && obj.TryGetComponent<Unit>(out _))
                .Select(obj => obj.GetComponent<NetworkIdentity>())
                .Where(id => id != null)
                .ToList();
        }

        // Används av InputManager
        public List<uint> GetSelectedUnitNetIds()
        {
            return selectedObjects
                .Where(obj => obj != null && obj.TryGetComponent<Unit>(out _))
                .Select(obj => obj.GetComponent<NetworkIdentity>())
                .Where(id => id != null)
                .Select(id => id.netId)
                .ToList();
        }

        public GameObject GetFirstSelectedObject()
        {
            return selectedObjects.FirstOrDefault();
        }

        public bool IsSelected(GameObject obj)
        {
            if (obj == null) return false;
            return selectedObjects.Contains(obj);
        }

        public void SelectSingleObject(GameObject objectToSelect)
        {
            if (objectToSelect == null) { ClearSelection(); return; }

            NetworkIdentity identity = objectToSelect.GetComponentInParent<NetworkIdentity>();
            ISelectable selectable = objectToSelect.GetComponentInParent<ISelectable>();

            if (identity == null || selectable == null || !IsSelectableByLocalPlayer(selectable))
            {
                ClearSelection();
                return;
            }

            ClearSelection();
            if (CanAddObjectToSelection(identity.gameObject))
            {
                AddObjectToSelection(identity.gameObject);
                OnSelectionChanged?.Invoke();
                // Debug.Log($"Selected single object: {identity.gameObject.name}");
            }
        }

        public NetworkIdentity GetPrimarySelectedBuildingIdentity()
        {
            if (selectedObjects.Count == 1 &&
                selectedObjects[0] != null &&
                selectedObjects[0].TryGetComponent<Building>(out _) &&
                selectedObjects[0].TryGetComponent<NetworkIdentity>(out var id))
            {
                return id;
            }
            return null;
        }


        // --- Hjälpmetoder ---

        private bool IsSelectableByLocalPlayer(ISelectable selectable)
        {
            if (selectable == null || localPlayer == null) return false;
            uint ownerNetId = selectable.GetOwnerNetId();
            // Tillåt val av egna objekt och neutrala (owner 0)
            return ownerNetId == localPlayer.netId || ownerNetId == 0;
            // TODO: Ersätt med PlayerManager.Instance.IsFriendlyOrNeutral(ownerNetId) för team-spel?
        }

        private bool CanAddObjectToSelection(GameObject objToAdd)
        {
            if (objToAdd == null) return false;
            bool addingBuilding = objToAdd.TryGetComponent<Building>(out _);
            bool addingUnit = objToAdd.TryGetComponent<Unit>(out _);
            if (!addingBuilding && !addingUnit) return false; // Okänd typ

            if (selectedObjects.Count == 0) // Första objektet
            {
                if (addingUnit && maxUnitSelection > 0 && 1 > maxUnitSelection) return false; // Kolla max direkt
                return true;
            }

            // Jämför med första objektet i nuvarande urval
            GameObject firstSelected = selectedObjects[0];
            bool selectionContainsBuilding = firstSelected.TryGetComponent<Building>(out _);

            // Regel: Inte blanda Byggnad och Enhet
            if (addingBuilding && !selectionContainsBuilding) return false;
            if (addingUnit && selectionContainsBuilding) return false;

            // Regel: Max en Byggnad
            if (addingBuilding && selectionContainsBuilding) return false;

            // Regel: Max antal enheter
            if (addingUnit && maxUnitSelection > 0 && selectedObjects.Count >= maxUnitSelection)
            {
                // Debug.Log($"Selection limit reached ({maxUnitSelection} units).");
                return false;
            }

            return true;
        }

        private bool IsPointerOverUIObject()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

    } // End class SelectionManager
} // End namespace RTSGAME