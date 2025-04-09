// Assets/RTSGAME/Scripts/Managers/SelectionManager.cs
using System.Collections.Generic;
using UnityEngine;
using Mirror; // Behövs för NetworkIdentity

namespace RTSGAME
{
    public class SelectionManager : MonoBehaviour
    {
        public static SelectionManager Instance { get; private set; }

        private List<GameObject> selectedObjects = new List<GameObject>();

        // Event för när selektionen ändras (bra för UI att lyssna på)
        public event System.Action OnSelectionChanged;

        // TODO: Lägg till variabler för box selection visuals

        void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        void Update()
        {
            // TODO: Hantera logik för att rita/uppdatera box selection rectangle om musknapp hålls nere
        }

        public void HandleClickSelection(GameObject clickedObject, bool additive)
        {
            // Försök hämta NetworkIdentity för att se om det är ett valbart objekt
            NetworkIdentity identity = clickedObject.GetComponentInParent<NetworkIdentity>(); // Sök uppåt ifall man klickade på en sub-collider
            if (identity == null)
            { // Inte ett nätverksobjekt? Kanske terräng eller icke-valbart.
                if (!additive) ClearSelection();
                return;
            }

            GameObject objectToSelect = identity.gameObject;

            if (!additive) // Om inte shift hålls nere
            {
                ClearSelection(); // Rensa tidigare val
                AddObjectToSelection(objectToSelect);
            }
            else // Om shift hålls nere (additive)
            {
                if (selectedObjects.Contains(objectToSelect))
                {
                    RemoveObjectFromSelection(objectToSelect); // Avmarkera om redan vald
                }
                else
                {
                    AddObjectToSelection(objectToSelect); // Markera om inte vald
                }
            }
            OnSelectionChanged?.Invoke(); // Meddela att valet ändrats
        }

        public void HandleBoxSelection(Rect selectionBox) // Antag att Rect är i skärmkoordinater
        {
            ClearSelection();
            // TODO: Hitta alla valbara objekt inom selectionBox
            // Exempel (kräver att valbara objekt har en specifik tag eller layer):
            // Collider[] hits = Physics.OverlapBox(...) // Eller iterera genom alla enheter/byggnader
            // foreach(var obj in AllSelectableObjects) {
            //      Vector3 screenPos = Camera.main.WorldToScreenPoint(obj.transform.position);
            //      if(selectionBox.Contains(screenPos) && IsSelectable(obj)) {
            //           AddObjectToSelection(obj);
            //      }
            // }
            OnSelectionChanged?.Invoke();
        }


        public void AddObjectToSelection(GameObject obj)
        {
            if (!selectedObjects.Contains(obj))
            {
                selectedObjects.Add(obj);
                // Anropa Select() på objektets script för visuell feedback?
                obj.GetComponent<Building>()?.Select(); // För byggnader
                obj.GetComponent<Unit>()?.Select();     // För enheter
            }
        }

        public void RemoveObjectFromSelection(GameObject obj)
        {
            if (selectedObjects.Contains(obj))
            {
                // Anropa Deselect()
                obj.GetComponent<Building>()?.Deselect();
                obj.GetComponent<Unit>()?.Deselect();
                selectedObjects.Remove(obj);
            }
        }


        public void ClearSelection()
        {
            // Anropa Deselect() på alla nuvarande valda objekt
            foreach (var obj in selectedObjects)
            {
                obj.GetComponent<Building>()?.Deselect();
                obj.GetComponent<Unit>()?.Deselect();
            }
            selectedObjects.Clear();
            OnSelectionChanged?.Invoke(); // Meddela att valet ändrats
        }

        // --- Getters för andra script ---
        public List<GameObject> GetSelectedObjects()
        {
            return new List<GameObject>(selectedObjects); // Returnera kopia
        }

        public List<NetworkIdentity> GetSelectedUnitsNetworkIdentities()
        {
            List<NetworkIdentity> units = new List<NetworkIdentity>();
            foreach (var obj in selectedObjects)
            {
                if (obj.TryGetComponent<Unit>(out var unit) && unit.TryGetComponent<NetworkIdentity>(out var id))
                { // Exempel: Kolla om det är en Unit
                    units.Add(id);
                }
            }
            return units;
        }

        public GameObject GetFirstSelectedObject()
        {
            return selectedObjects.Count > 0 ? selectedObjects[0] : null;
        }

        // TODO: Fler getters efter behov (t.ex. GetFirstSelectedWorkerIdentity)

    }
}