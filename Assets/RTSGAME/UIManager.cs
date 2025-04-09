// Assets/RTSGAME/Scripts/Managers/UIManager.cs
using UnityEngine;
using UnityEngine.UI; // För Text, Button, Slider etc.
using System.Collections.Generic; // För List
using Mirror; // För NetworkIdentity

namespace RTSGAME
{
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        private NetworkPlayer localPlayer; // Referens till den lokala spelaren

        [Header("Resource Display")]
        [SerializeField] private Text creditsText;
        [SerializeField] private Text manaText;

        [Header("Selection Panel")]
        [SerializeField] private GameObject selectionPanel; // Panelen som visar info
        [SerializeField] private Text selectionNameText;
        [SerializeField] private Slider selectionHealthSlider;
        [SerializeField] private Slider selectionProgressBar; // För bygg/capture/produktion
        [SerializeField] private GameObject productionQueuePanel; // Panel för produktionskö
        // TODO: Lägg till element för att visa produktionskö-ikoner etc.

        [Header("Minimap")]
        [SerializeField] private RawImage minimapImage;
        // TODO: Lägg till logik för minimap

        [Header("Notifications")]
        [SerializeField] private Text notificationText;
        // TODO: Lägg till logik för att visa/dölja notiser

        [Header("Build Menu")]
        [SerializeField] private GameObject buildMenuPanel;
        // TODO: Lägg till knappar för byggnader

        void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            // Göm paneler från start
            if (selectionPanel) selectionPanel.SetActive(false);
            if (buildMenuPanel) buildMenuPanel.SetActive(false);
            // etc.
        }

        void Start()
        {
            // Prenumerera på events
            if (SelectionManager.Instance != null)
            {
                SelectionManager.Instance.OnSelectionChanged += UpdateSelectionPanel;
            }
        }

        void OnDestroy()
        {
            // Avprenumerera på events
            if (SelectionManager.Instance != null)
            {
                SelectionManager.Instance.OnSelectionChanged -= UpdateSelectionPanel;
            }
        }


        // Anropas av NetworkPlayer.OnStartLocalPlayer
        public void SetLocalPlayer(NetworkPlayer player)
        {
            localPlayer = player;
        }

        // --- Uppdateringsmetoder (anropas av NetworkPlayer hooks eller andra managers) ---

        public void UpdateCreditsDisplay(int amount)
        {
            if (creditsText != null) creditsText.text = $"Credits: {amount}";
        }

        public void UpdateManaDisplay(int current, int max)
        {
            if (manaText != null) manaText.text = $"Mana: {current} / {max}";
        }

        public void UpdateSelectionPanel()
        {
            if (selectionPanel == null || SelectionManager.Instance == null) return;

            List<GameObject> selection = SelectionManager.Instance.GetSelectedObjects();

            if (selection.Count == 1) // Visa detaljer om ett objekt är valt
            {
                GameObject selectedObj = selection[0];
                selectionPanel.SetActive(true);

                string displayName = "Unknown";
                float currentHealth = 0;
                float maxHealth = 1;
                float progress = -1; // Negativt = visa inte progress bar
                bool showProduction = false;

                // Försök hämta data från Building eller Unit
                if (selectedObj.TryGetComponent<Building>(out Building building))
                {
                    displayName = building.BuildingName;
                    currentHealth = building.CurrentHealth;
                    maxHealth = building.MaxHealth;
                    if (building.CurrentState == BuildingState.Constructing) progress = building.ConstructionProgress;
                    else if (building.CurrentState == BuildingState.BeingCaptured) progress = building.CaptureProgress;
                    // TODO: Kolla om det är en produktionsbyggnad och hämta produktionsprogress/-kö
                    // if (building is Barracks barracks) { showProduction = true; /* hämta ködata */ }

                }
                else if (selectedObj.TryGetComponent<Unit>(out Unit unit))
                {
                    //displayName = unit.UnitName; // Antag att Unit har ett namn
                    // currentHealth = unit.CurrentHealth; // Antag att Unit använder Health.cs
                    // maxHealth = unit.MaxHealth;
                    // TODO: Hämta relevant unit data
                }

                // Uppdatera UI-elementen
                if (selectionNameText) selectionNameText.text = displayName;
                if (selectionHealthSlider)
                {
                    selectionHealthSlider.gameObject.SetActive(maxHealth > 0);
                    if (maxHealth > 0) selectionHealthSlider.value = currentHealth / maxHealth;
                }
                if (selectionProgressBar)
                {
                    selectionProgressBar.gameObject.SetActive(progress >= 0);
                    if (progress >= 0) selectionProgressBar.value = progress;
                }
                if (productionQueuePanel) productionQueuePanel.SetActive(showProduction);
                // TODO: Uppdatera produktionskö-UI om showProduction är true

            }
            else if (selection.Count > 1) // Visa generell info för flera valda
            {
                selectionPanel.SetActive(true);
                if (selectionNameText) selectionNameText.text = $"{selection.Count} Objects Selected";
                // Göm/återställ detaljerade fält
                if (selectionHealthSlider) selectionHealthSlider.gameObject.SetActive(false);
                if (selectionProgressBar) selectionProgressBar.gameObject.SetActive(false);
                if (productionQueuePanel) productionQueuePanel.SetActive(false);
                // TODO: Visa kanske ikoner för de valda enheterna?
            }
            else // Inget valt
            {
                selectionPanel.SetActive(false);
            }
        }

        public void ShowNotification(string message)
        {
            // TODO: Implementera logik för att visa och tona ut notiser
            if (notificationText) notificationText.text = message;
            Debug.Log($"UI Notification: {message}");
        }

        public void ToggleBuildMenu()
        {
            if (buildMenuPanel) buildMenuPanel.SetActive(!buildMenuPanel.activeSelf);
        }

        // --- Metoder som anropas av UI-knappar ---

        public void OnBuildBuildingButtonClicked(int buildingTypeId)
        {
            // TODO: Tala om för InputManager att gå in i "placera byggnad"-läge
            // med det valda buildingTypeId. InputManager hanterar sedan klicket.
            Debug.Log($"UI requested build of type {buildingTypeId}");
            // InputManager.Instance.EnterPlacementMode(buildingTypeId);
            ToggleBuildMenu(); // Stäng menyn?
        }

        public void OnQueueUnitButtonClicked(int unitTypeId)
        {
            // Hitta den valda byggnaden
            GameObject selectedObj = SelectionManager.Instance.GetFirstSelectedObject();
            if (localPlayer != null && selectedObj != null && selectedObj.TryGetComponent<NetworkIdentity>(out var buildingId))
            {
                // Skicka Command via NetworkPlayer
                localPlayer.ProcessQueueUnitRequest(buildingId, unitTypeId);
            }
        }

        // TODO: Fler knapp-handlers (Sell, Cancel Production, etc.)

    }
}