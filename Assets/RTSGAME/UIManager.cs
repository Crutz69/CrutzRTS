// Assets/RTSGAME/Scripts/Managers/UIManager.cs
using UnityEngine;
using UnityEngine.UI;         // Behålls för Slider, Button, RawImage, Image etc.
using TMPro;                  // NYTT: För TextMeshPro
using System.Collections.Generic;
using Mirror;

namespace RTSGAME
{
    public class UIManager : MonoBehaviour
    {
        // --- Singleton ---
        public static UIManager Instance { get; private set; }

        // --- Referenser ---
        private NetworkPlayer localPlayer; // Referens till den lokala spelaren

        [Header("Resource Display")]
        [SerializeField] private TextMeshProUGUI creditsText; // TextMeshPro: Ändrad typ
        // [SerializeField] private TextMeshProUGUI manaText; // TextMeshPro: Bortkommenterad, Mana Bar är separat
        [SerializeField] private ManaBarController manaBarController; // NYTT: Antag att du har denna controller

        [Header("Selection Panel")]
        [SerializeField] private GameObject selectionPanel;
        [SerializeField] private TextMeshProUGUI selectionNameText; // TextMeshPro: Ändrad typ
        [SerializeField] private Slider selectionHealthSlider;
        [SerializeField] private Slider selectionProgressBar;
        [SerializeField] private GameObject productionQueuePanel; // Panel för produktionskö
        [SerializeField] private Transform productionQueueSlotsContainer; // NYTT: Container för kö-ikoner
        [SerializeField] private GameObject queueItemIconPrefab; // NYTT: Prefab för kö-ikon

        [Header("Minimap")]
        [SerializeField] private RawImage minimapImage;
        // TODO: Minimap logik

        [Header("Notifications")]
        [SerializeField] private TextMeshProUGUI notificationText; // TextMeshPro: Ändrad typ
        // TODO: Notifikationslogik

        // --- MERGED: Referenser från UIController för Byggmeny ---
        [Header("Build Menu System")]
        [SerializeField] private BuildableDatabase buildableDatabase; // Dra ditt Database-Asset hit!
        [SerializeField] private GameObject buildCategoryPanel;    // Panelen med kategoriknappar
        [SerializeField] private GameObject buildablesPanel;       // Panelen med bygg-ikoner/knappar
        [SerializeField] private Transform slotsContainer;          // Dra din SlotsContainer (barn till BuildablesPanel) hit!
        [SerializeField] private GameObject buildableItemButtonPrefab; // Dra din BuildableItem_Prefab hit!
        [SerializeField] private GameObject buildingCountPanel;    // Panelen som visar ikoner för ägda byggnader
        [SerializeField] private Transform buildingCountPanelContainer; // Dra din BuildingCountPanel (den som har Horiz Layout Group) hit!
        [SerializeField] private GameObject buildingIconButtonPrefab; // Dra din BuildingIconButton_Prefab hit!
        [SerializeField] private BuildingPlacer buildingPlacer;     // Dra objektet med BuildingPlacer-scriptet hit (när det finns)
        // Hållare för kategoriknappar (kopplas via kod eller Inspector)
        // [SerializeField] private List<Button> categoryButtons; // Exempel om du vill koppla i Inspector

        // --- Intern State (Från UIController) ---
        private BuildingType selectedCategoryType = BuildingType.None;
        private Building selectedBuildingInstance = null; // Den specifika byggnadsinstansen som är vald
        private Dictionary<BuildingType, Button> categoryButtonsDict = new Dictionary<BuildingType, Button>(); // För att highlighta
        private BuildingIconButtonHandler highlightedIconButton = null; // För building count display

        // --- Unity Metoder ---
        void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; } // Förstör dubblett

            // Göm paneler från start
            if (selectionPanel) selectionPanel.SetActive(false);
            // MERGED: Göm byggpaneler
            if (buildCategoryPanel) buildCategoryPanel.SetActive(false);
            if (buildablesPanel) buildablesPanel.SetActive(false);
            if (buildingCountPanel) buildingCountPanel.SetActive(false);
            // etc.
        }

        void Start()
        {
            // Prenumerera på events
            if (SelectionManager.Instance != null)
            {
                SelectionManager.Instance.OnSelectionChanged += UpdateSelectionPanel;
            }
            else { Debug.LogWarning("SelectionManager not found during UIManager Start."); }

            // TODO: Initiera categoryButtonsDict om du inte kopplar i Inspector
        }

        void OnDestroy()
        {
            // Avprenumerera på events
            if (SelectionManager.Instance != null)
            {
                SelectionManager.Instance.OnSelectionChanged -= UpdateSelectionPanel;
            }
            if (Instance == this) Instance = null; // Rensa Singleton
        }

        // Anropas av NetworkPlayer.OnStartLocalPlayer
        public void SetLocalPlayer(NetworkPlayer player)
        {
            localPlayer = player;
            // Initial UI-uppdatering triggas nu av hooks i NetworkPlayer när SyncVars får sina värden
        }

        // --- Uppdateringsmetoder (anropas av NetworkPlayer hooks eller andra managers) ---

        public void UpdateCreditsDisplay(int amount)
        {
            // TextMeshPro: Använder .text precis som förut
            if (creditsText != null) creditsText.text = $"{amount}"; // Ta bort "Credits: "? Ikonen visar nog vad det är.
        }

        // ÄNDRAD: Uppdaterar ManaBarController istället för text
        public void UpdateManaRelatedUI(int generation, int upkeep, bool hasPower)
        {
            if (manaBarController != null)
            {
                manaBarController.UpdateGeneration(generation);
                manaBarController.UpdateUpkeep(upkeep);
                manaBarController.UpdatePowerStatus(hasPower);
            }
        }

        public void UpdateSelectionPanel()
        {
            if (selectionPanel == null || SelectionManager.Instance == null) return;
            List<GameObject> selection = SelectionManager.Instance.GetSelectedObjects();

            if (selection.Count == 1)
            {
                GameObject selectedObj = selection[0];
                selectionPanel.SetActive(true);
                string displayName = "Unknown";
                float currentHealth = 0, maxHealth = 1;
                float progress = -1;
                bool showProduction = false;
                List<string> queueToShow = null; // NYTT: För kön
                string buildingItemId = null; // NYTT
                float buildingProgress = 0f; // NYTT
                Building.BuildPauseState pauseState = Building.BuildPauseState.None; // NYTT

                if (selectedObj.TryGetComponent<Building>(out Building building))
                {
                    displayName = building.BuildingName;
                    currentHealth = building.CurrentHealth;
                    maxHealth = building.MaxHealth;
                    if (building.CurrentState == BuildingState.Constructing) progress = building.ConstructionProgress;
                    else if (building.CurrentState == BuildingState.BeingCaptured) progress = building.CaptureProgress;

                    // MERGED: Hämta produktionskö och status om byggnaden kan producera
                    if (building.CanQueueItems()) // Antag att Building har denna metod
                    {
                        showProduction = true;
                        queueToShow = new List<string>(building.syncBuildQueueIds); // Hämta synkad kö
                        buildingItemId = building.syncCurrentlyBuildingId; // Hämta synkad aktiv
                        buildingProgress = building.syncCurrentBuildProgress; // Hämta synkad progress
                        pauseState = building.syncCurrentPauseState; // Hämta synkad paus-status
                    }
                }
                // else if (selectedObj.TryGetComponent<Unit>(out Unit unit)) { ... }

                if (selectionNameText) selectionNameText.text = displayName;
                if (selectionHealthSlider) { selectionHealthSlider.gameObject.SetActive(maxHealth > 0); if (maxHealth > 0) selectionHealthSlider.value = currentHealth / maxHealth; }
                if (selectionProgressBar) { selectionProgressBar.gameObject.SetActive(progress >= 0); if (progress >= 0) selectionProgressBar.value = progress; }

                // NYTT: Uppdatera produktionskö-panelen
                UpdateProductionQueuePanel(showProduction, queueToShow, buildingItemId, buildingProgress, pauseState);

            }
            else if (selection.Count > 1)
            {
                selectionPanel.SetActive(true);
                if (selectionNameText) selectionNameText.text = $"{selection.Count} Objects Selected";
                if (selectionHealthSlider) selectionHealthSlider.gameObject.SetActive(false);
                if (selectionProgressBar) selectionProgressBar.gameObject.SetActive(false);
                if (productionQueuePanel) productionQueuePanel.SetActive(false); // Göm kön vid multiselect
            }
            else
            {
                selectionPanel.SetActive(false);
            }
        }

        // NYTT: Funktion för att uppdatera produktionskö-UI
        private void UpdateProductionQueuePanel(bool show, List<string> queueIds, string currentlyBuildingId, float progress, Building.BuildPauseState pauseState)
        {
            if (productionQueuePanel == null || productionQueueSlotsContainer == null || queueItemIconPrefab == null) return;

            productionQueuePanel.SetActive(show);
            if (!show) return;

            // Rensa gamla ikoner
            foreach (Transform child in productionQueueSlotsContainer) Destroy(child.gameObject);

            // Visa ikon för det som byggs just nu (om något)
            if (!string.IsNullOrEmpty(currentlyBuildingId))
            {
                BuildableData data = buildableDatabase?.GetDataById(currentlyBuildingId);
                if (data != null)
                {
                    GameObject iconGO = Instantiate(queueItemIconPrefab, productionQueueSlotsContainer);
                    // TODO: Konfigurera ikonen, visa progress/paus-status på denna ikon
                    Image img = iconGO.GetComponentInChildren<Image>(); // Antag enkel ikon
                    if (img) img.sprite = data.icon;
                    // Lägg till logik för progressbar på denna kö-ikon
                }
            }

            // Visa ikoner för resten av kön
            if (queueIds != null)
            {
                foreach (string id in queueIds)
                {
                    BuildableData data = buildableDatabase?.GetDataById(id);
                    if (data != null)
                    {
                        GameObject iconGO = Instantiate(queueItemIconPrefab, productionQueueSlotsContainer);
                        // TODO: Konfigurera ikonen
                        Image img = iconGO.GetComponentInChildren<Image>();
                        if (img) img.sprite = data.icon;
                        // Lägg till högerklicksfunktion för att avbryta?
                    }
                }
            }
        }


        public void ShowNotification(string message)
        {
            // TODO: Implementera logik för att visa och tona ut notiser
            if (notificationText) notificationText.text = message;
            Debug.Log($"UI Notification: {message}");
        }


        // --- MERGED: Funktioner från UIController för Byggmeny ---

        // Anropas av CategoryButtonHelper när en kategoriknapp klickas
        public void SelectCategory(BuildingType category)
        {
            Debug.Log($"Category selected: {category}");
            selectedCategoryType = category;
            selectedBuildingInstance = null;
            RemoveHighlightFromBuildingIcon();

            if (buildablesPanel != null) buildablesPanel.SetActive(true); else { Debug.LogError("Buildables Panel ref missing!"); return; }
            if (buildCategoryPanel != null) buildCategoryPanel.SetActive(true); // Se till att båda är synliga?

            HighlightCategoryButton(category);
            UpdateBuildablesPanel(selectedCategoryType);
            UpdateBuildingCountDisplay(GetCurrentBuildingData()); // Kräver implementation av GetCurrentBuildingData
        }

        // Fyller panelen med byggbara alternativ
        public void UpdateBuildablesPanel(BuildingType category)
        {
            if (slotsContainer == null || buildableItemButtonPrefab == null || buildableDatabase == null) return;
            foreach (Transform child in slotsContainer) Destroy(child.gameObject);
            List<BuildableData> itemsToShow = buildableDatabase.GetBuildablesForCategory(category);
            // TODO: Filtrera itemsToShow baserat på isUnlocked/prerequisites

            foreach (BuildableData data in itemsToShow)
            {
                GameObject itemGO = Instantiate(buildableItemButtonPrefab, slotsContainer);
                BuildableItemButtonUI buttonUI = itemGO.GetComponent<BuildableItemButtonUI>();
                if (buttonUI != null)
                {
                    buttonUI.Initialize(data, this); // Skicka med referens till UIManager
                    Building activeBuilding = GetActiveBuildingForQueue(); // Behöver implementeras
                    buttonUI.UpdateState(activeBuilding); // Uppdatera knappens visuella status
                }
                // ... (Fallback om script saknas) ...
            }
        }

        // Uppdaterar raden med ägda byggnader av vald typ
        public void UpdateBuildingCountDisplay(Dictionary<BuildingType, List<Building>> buildingsOwned)
        {
            if (buildingCountPanelContainer == null || buildingIconButtonPrefab == null || buildingCountPanel == null) return;
            foreach (Transform child in buildingCountPanelContainer) Destroy(child.gameObject);
            highlightedIconButton = null;

            List<Building> buildingsOfType = null;
            if (selectedCategoryType != BuildingType.None && buildingsOwned != null && buildingsOwned.TryGetValue(selectedCategoryType, out buildingsOfType))
            {
                int count = buildingsOfType.Count;
                bool showPanel = count > 1; // Visa bara om fler än 1
                buildingCountPanel.SetActive(showPanel);

                if (showPanel)
                {
                    for (int i = 0; i < count; i++)
                    {
                        GameObject iconGO = Instantiate(buildingIconButtonPrefab, buildingCountPanelContainer);
                        BuildingIconButtonHandler iconHandler = iconGO.GetComponent<BuildingIconButtonHandler>(); // Använder denna om du implementerat dubbelklick

                        if (iconHandler != null)
                        {
                            iconHandler.AssociatedBuilding = buildingsOfType[i];
                            // Sätt ikon
                            Image iconImage = iconGO.transform.Find("Icon")?.GetComponent<Image>();
                            if (iconImage) iconImage.sprite = GetSpriteForBuildingType(selectedCategoryType); // Behöver implementeras

                            // Hantera highlight/selection
                            if (selectedBuildingInstance == buildingsOfType[i]) { HighlightIconButton(iconHandler); }
                        }
                        else { /* Fallback om du använder vanlig Button */ }
                    }
                    // ScaleIconsToFit();
                }
                else if (count == 1) { selectedBuildingInstance = buildingsOfType[0]; }
                else { selectedBuildingInstance = null; }
            }
            else
            {
                buildingCountPanel.SetActive(false);
                selectedBuildingInstance = null;
            }
        }

        // Anropas av BuildableItemButtonUI när en bygg-knapp klickas
        public void OnBuildableItemClicked(BuildableData itemData)
        {
            if (localPlayer == null) { Debug.LogError("Local player not found!"); return; }

            if (itemData.itemType == BuildableItemType.Building)
            {
                if (buildingPlacer != null) { buildingPlacer.StartPlacement(itemData); CloseBuildMenus(); }
                else { Debug.LogError("BuildingPlacer reference not set!"); }
            }
            else if (itemData.itemType == BuildableItemType.Unit || itemData.itemType == BuildableItemType.Upgrade)
            {
                Building targetBuilding = GetActiveBuildingForQueue(); // Behöver implementeras
                if (targetBuilding != null)
                {
                    localPlayer.CmdQueueItem(targetBuilding.netId, itemData.buildableId, 1);
                }
                else { Debug.LogWarning("No building selected/available to queue the item at!"); /* Visa feedback */ }
            }
        }

        // Anropas av BuildingIconButtonHandler vid enkelklick
        public void SelectBuildingInstance(Building instance, BuildingIconButtonHandler clickedHandler)
        {
            if (selectedBuildingInstance == instance) return;
            RemoveHighlightFromBuildingIcon();
            selectedBuildingInstance = instance;
            HighlightIconButton(clickedHandler);
            // Uppdatera ev. annan UI som visar info om SPECIFIK byggnad?
        }


        // --- Byggmeny Toggle (Ersätter gamla) ---
        public void ToggleBuildMenu()
        {
            // Antag att HammerButton anropar denna
            bool shouldBeActive = !buildCategoryPanel.activeSelf; // Om en är inaktiv, ska vi aktivera
            buildCategoryPanel.SetActive(shouldBeActive);
            buildablesPanel.SetActive(shouldBeActive);

            if (shouldBeActive)
            {
                // Om menyn öppnas, välj en default-kategori om ingen är vald?
                if (selectedCategoryType == BuildingType.None)
                {
                    SelectCategory(BuildingType.House); // Välj Hus som default? Anpassa!
                }
                else
                {
                    // Uppdatera panelerna ifall något ändrats sedan sist
                    SelectCategory(selectedCategoryType); // Uppdatera med nuvarande kategori
                }
            }
            else
            {
                // Om menyn stängs
                buildingCountPanel.SetActive(false); // Göm alltid count display när huvudmenyn stängs
                // selectedCategoryType = BuildingType.None; // Nollställ vald kategori? Eller behåll? Designval.
                RemoveHighlightFromBuildingIcon();
            }
        }

        public void CloseBuildMenus()
        {
            buildCategoryPanel.SetActive(false);
            buildablesPanel.SetActive(false);
            buildingCountPanel.SetActive(false);
            // selectedCategoryType = BuildingType.None; // Nollställ?
            RemoveHighlightFromBuildingIcon();
        }

        // --- Diverse Hjälpfunktioner (Behöver implementation/anpassning) ---
        private void HighlightCategoryButton(BuildingType category) { /* ... */ Debug.Log($"Highlighting category: {category}"); }
        private void HighlightIconButton(BuildingIconButtonHandler handlerToHighlight) { highlightedIconButton = handlerToHighlight; highlightedIconButton?.SetHighlightActive(true); }
        private void RemoveHighlightFromBuildingIcon() { highlightedIconButton?.SetHighlightActive(false); highlightedIconButton = null; }
        private Dictionary<BuildingType, List<Building>> GetCurrentBuildingData() { Debug.LogWarning("GetCurrentBuildingData() needs implementation!"); return new Dictionary<BuildingType, List<Building>>(); }
        private Building GetActiveBuildingForQueue() { Debug.LogWarning("GetActiveBuildingForQueue() needs implementation! Returning selected instance for now."); return selectedBuildingInstance; } // Placeholder
        private Sprite GetSpriteForBuildingType(BuildingType type) { Debug.LogWarning("GetSpriteForBuildingType() needs implementation!"); return null; }
        // private void ScaleIconsToFit() { /* ... */ }

    } // End class UIManager
} // End namespace RTSGAME