// Filnamn: UIManager.cs
using UnityEngine;
using UnityEngine.UI;           // Behålls för Slider, Button, RawImage, Image, Toggle etc.
using TMPro;                    // För TextMeshPro
using System.Collections.Generic;
using System.Linq;              // För FirstOrDefault
using Mirror;                   // För NetworkClient etc.

namespace RTSGAME
{
    public class UIManager : MonoBehaviour
    {
        // --- Singleton ---
        public static UIManager Instance { get; private set; }

        // --- Referenser ---
        private NetworkPlayer localPlayer; // Referens till den lokala spelaren

        [Header("Data References")]
        [SerializeField] private BuildableDatabase buildableDatabase; // Dra ditt Database-Asset hit!

        [Header("Resource Display")]
        [SerializeField] private TextMeshProUGUI creditsText;
        [SerializeField] private ManaBarController manaBarController; // Referens till ManaBarController

        [Header("Selection Panel")]
        [SerializeField] private GameObject selectionPanel;
        [SerializeField] private TextMeshProUGUI selectionNameText;
        [SerializeField] private Slider selectionHealthSlider;
        [SerializeField] private Slider selectionProgressBar;
        [SerializeField] private GameObject productionQueuePanel;
        [SerializeField] private Transform productionQueueSlotsContainer;
        [SerializeField] private GameObject queueItemIconPrefab;

        [Header("Minimap")]
        [SerializeField] private RawImage minimapImage;
        // TODO: Minimap logik

        [Header("Notifications")]
        [SerializeField] private TextMeshProUGUI notificationText;
        // TODO: Notifikationslogik
        [Header("Status Indicators")]
        [Tooltip("UI-element (t.ex. en ikon eller text) som visas vid låg Mana/Power.")]
        [SerializeField] private GameObject powerWarningIndicator;
        [Tooltip("Panel som visas när spelaren har förlorat.")]
        [SerializeField] private GameObject defeatPanel;
        [Tooltip("Panel som visas när spelaren har vunnit.")]
        [SerializeField] private GameObject victoryPanel;

        [Header("Build Menu System")]
        [SerializeField] private GameObject buildCategoryPanel;
        [SerializeField] private GameObject buildablesPanel;
        [SerializeField] private Transform slotsContainer;
        [SerializeField] private GameObject buildableItemButtonPrefab;
        [SerializeField] private GameObject buildingCountPanel;
        [SerializeField] private Transform buildingCountPanelContainer;
        [SerializeField] private GameObject buildingIconButtonPrefab;

        [Header("UI Buttons")]
        [SerializeField] private Toggle buildMenuToggle; // Huvud-Togglen för hela menyn

        [Header("External System References")]
        [SerializeField] private BuildingPlacer buildingPlacer;

        // --- Intern State för Byggmeny ---
        private BuildingType selectedCategoryType = BuildingType.None;
        private ProductionBuilding selectedProductionBuildingInstance = null;
        private Building selectedBuildingInstance = null;
        // *** ÄNDRAD: Lagrar nu Toggle istället för Button ***
        private Dictionary<BuildingType, Toggle> categoryButtonsDict = new Dictionary<BuildingType, Toggle>();
        private BuildingIconButtonHandler highlightedIconButton = null;

        // --- Unity Metoder ---
        void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        void Start()
        {
            if (SelectionManager.Instance != null) { SelectionManager.Instance.OnSelectionChanged += UpdateSelectionPanel; }
            else { Debug.LogWarning("SelectionManager not found during UIManager Start."); }
            StartCoroutine(FindLocalPlayerRoutine());
            InitializeCategoryButtons(); // Anropar den uppdaterade metoden nedan

            // Koppla huvud-Togglen
            if (buildMenuToggle != null)
            {
                buildMenuToggle.onValueChanged.AddListener(SetBuildMenuVisibility);
                SetBuildMenuVisibility(buildMenuToggle.isOn); // Sätt initialt state
            }
            else
            {
                Debug.LogWarning("BuildMenuToggle reference not set in UIManager!");
                // Dölj menyerna manuellt om ingen huvud-Toggle finns
                buildCategoryPanel?.SetActive(false);
                buildablesPanel?.SetActive(false);
                buildingCountPanel?.SetActive(false);
            }
        }

        void OnDestroy()
        {
            if (SelectionManager.Instance != null) { SelectionManager.Instance.OnSelectionChanged -= UpdateSelectionPanel; }
            if (buildMenuToggle != null) { buildMenuToggle.onValueChanged.RemoveListener(SetBuildMenuVisibility); }
            if (Instance == this) Instance = null;
        }

        private System.Collections.IEnumerator FindLocalPlayerRoutine()
        {
            yield return null;
            localPlayer = NetworkClient.localPlayer?.GetComponent<NetworkPlayer>();
            if (localPlayer)
            {
                Debug.Log("UIManager found Local Player.");
                UpdateCreditsDisplay(localPlayer.credits);
                UpdateManaRelatedUI(localPlayer.manaGeneration, localPlayer.manaUpkeep, localPlayer.hasSufficientPower);
                // Kör en initial UI-uppdatering för byggnader när spelaren hittats
                UpdateOwnedBuildingUI();
            }
            else if (NetworkClient.active) { Debug.LogWarning("UIManager could not find Local Player after waiting."); }
        }

        // --- Metoder för att sätta referenser och uppdatera UI ---
        public void SetLocalPlayer(NetworkPlayer player)
        {
            localPlayer = player;
            if (localPlayer != null)
            {
                UpdateCreditsDisplay(localPlayer.credits);
                UpdateManaRelatedUI(localPlayer.manaGeneration, localPlayer.manaUpkeep, localPlayer.hasSufficientPower);
                UpdateOwnedBuildingUI(); // Uppdatera byggnads-UI när spelaren kopplas
            }
        }
        public void UpdateCreditsDisplay(int amount) { if (creditsText != null) creditsText.text = $"{amount}"; }
        public void UpdateManaRelatedUI(int generation, int upkeep, bool hasPower) { manaBarController?.UpdateGeneration(generation); manaBarController?.UpdateUpkeep(upkeep); manaBarController?.UpdatePowerStatus(hasPower); }

        public void UpdateSelectionPanel()
        {
            if (selectionPanel == null || SelectionManager.Instance == null) return;
            List<GameObject> selection = SelectionManager.Instance.GetSelectedObjects();

            if (selection.Count == 1)
            {
                GameObject selectedObj = selection[0];
                selectionPanel.SetActive(true);

                string nameToShow = "Selected Object";
                float currentHealth = 0f, maxHealth = 1f;
                float progressToShow = 0f;
                bool showProgress = false;
                List<string> queueToShow = null;
                string buildingItemId = null;
                // float buildingProgress = 0f; // Täcks av progressToShow
                BuildPauseState pauseState = BuildPauseState.None;
                bool showProduction = false;

                // Återställ valda instanser (UI-val)
                selectedBuildingInstance = null;
                selectedProductionBuildingInstance = null;

                if (selectedObj.TryGetComponent<ProductionBuilding>(out ProductionBuilding prodBuilding))
                {
                    selectedBuildingInstance = prodBuilding;
                    selectedProductionBuildingInstance = prodBuilding;
                    nameToShow = prodBuilding.BuildingName;
                    if (prodBuilding.healthComponent != null) { currentHealth = prodBuilding.CurrentHealth; maxHealth = prodBuilding.MaxHealth; }

                    if (prodBuilding.CurrentState == BuildingState.Constructing) { progressToShow = prodBuilding.ConstructionProgress; showProgress = true; }
                    else if (!string.IsNullOrEmpty(prodBuilding.syncCurrentlyBuildingId)) { progressToShow = prodBuilding.syncCurrentBuildProgress; showProgress = true; }
                    else { showProgress = false; }

                    if (prodBuilding.CanQueueItems())
                    {
                        showProduction = true;
                        queueToShow = new List<string>(prodBuilding.syncBuildQueueIds);
                        buildingItemId = prodBuilding.syncCurrentlyBuildingId;
                        pauseState = prodBuilding.syncCurrentPauseState;
                    }
                }
                else if (selectedObj.TryGetComponent<Building>(out Building building))
                {
                    selectedBuildingInstance = building; // Spara referens
                    nameToShow = building.BuildingName;
                    if (building.healthComponent != null) { currentHealth = building.CurrentHealth; maxHealth = building.MaxHealth; }
                    if (building.CurrentState == BuildingState.Constructing) { progressToShow = building.ConstructionProgress; showProgress = true; } else { showProgress = false; }
                    showProduction = false;
                }
                else if (selectedObj.TryGetComponent<Unit>(out Unit unit))
                {
                    nameToShow = unit.UnitDisplayName;
                    currentHealth = unit.CurrentHealth;
                    maxHealth = unit.MaxHealth;
                    showProduction = false;
                    showProgress = false;
                }
                else // Okänt objekt
                {
                    nameToShow = selectedObj.name; // Fallback
                    if (selectedObj.TryGetComponent<Health>(out Health health)) { currentHealth = health.CurrentHealth; maxHealth = health.MaxHealth; }
                    showProduction = false;
                    showProgress = false;
                }

                selectionNameText.text = nameToShow;
                selectionHealthSlider.value = (maxHealth > 0) ? (currentHealth / maxHealth) : 0;
                selectionHealthSlider.gameObject.SetActive(maxHealth > 0);
                selectionProgressBar.gameObject.SetActive(showProgress);
                if (showProgress) selectionProgressBar.value = progressToShow;

                UpdateProductionQueuePanel(showProduction, queueToShow, buildingItemId, progressToShow, pauseState);
            }
            else if (selection.Count > 1)
            {
                selectionPanel.SetActive(true);
                selectionNameText.text = $"{selection.Count} Objects Selected";
                selectionHealthSlider.gameObject.SetActive(false);
                selectionProgressBar.gameObject.SetActive(false);
                UpdateProductionQueuePanel(false, null, null, 0f, BuildPauseState.None);
                selectedBuildingInstance = null;
                selectedProductionBuildingInstance = null;
            }
            else // Inget valt
            {
                selectionPanel.SetActive(false);
                selectedBuildingInstance = null;
                selectedProductionBuildingInstance = null;
            }
        }

        private void UpdateProductionQueuePanel(bool show, List<string> queueIds, string currentlyBuildingId, float progress, BuildPauseState pauseState)
        {
            if (productionQueuePanel == null || productionQueueSlotsContainer == null || queueItemIconPrefab == null || buildableDatabase == null) return;

            productionQueuePanel.SetActive(show);
            if (!show) return;

            foreach (Transform child in productionQueueSlotsContainer) Destroy(child.gameObject);

            if (!string.IsNullOrEmpty(currentlyBuildingId))
            {
                BuildableData data = buildableDatabase.GetDataById(currentlyBuildingId);
                if (data != null)
                {
                    GameObject iconGO = Instantiate(queueItemIconPrefab, productionQueueSlotsContainer);
                    Image img = iconGO.GetComponentInChildren<Image>(); if (img) img.sprite = data.icon;
                    // TODO: Hantera progress/paus-visualisering för den aktiva ikonen
                }
            }

            if (queueIds != null)
            {
                foreach (string id in queueIds)
                {
                    BuildableData data = buildableDatabase.GetDataById(id);
                    if (data != null)
                    {
                        GameObject iconGO = Instantiate(queueItemIconPrefab, productionQueueSlotsContainer);
                        Image img = iconGO.GetComponentInChildren<Image>(); if (img) img.sprite = data.icon;
                        // TODO: Lägg till logik för att kunna högerklicka och avbryta
                    }
                }
            }
        }

        public void ShowNotification(string message) { /* ... (som innan) ... */ }

        // Styr synligheten baserat på huvud-Toggle
        public void SetBuildMenuVisibility(bool isVisible)
        {
            // Debug.Log($"Setting Build Menu Visibility: {isVisible}");
            if (buildCategoryPanel) buildCategoryPanel.SetActive(isVisible);
            if (buildablesPanel) buildablesPanel.SetActive(isVisible);
            if (!isVisible && buildingCountPanel) buildingCountPanel.SetActive(false); // Dölj alltid count när menyn stängs

            if (isVisible)
            {
                // Välj en default-kategori eller återvälj senast valda
                if (selectedCategoryType == BuildingType.None)
                {
                    SelectCategory(BuildingType.Building); // Anpassa startkategori
                }
                else
                {
                    SelectCategory(selectedCategoryType); // Uppdatera med aktuell data
                }
            }
            else
            {
                RemoveHighlightFromBuildingIcon();
            }
        }

        // --- Byggmeny Funktioner ---

        // Anropas när en kategori-Toggle klickas (och blir true)
        public void SelectCategory(BuildingType category)
        {
            if (buildCategoryPanel == null || buildablesPanel == null) { return; } // Tidig exit om paneler saknas

            // Paneler bör redan vara synliga om denna anropas via en klickad Toggle i en synlig meny

            // 1. Sätt ny kategori och återställ val
            selectedCategoryType = category;
            selectedBuildingInstance = null;
            selectedProductionBuildingInstance = null;
            RemoveHighlightFromBuildingIcon();

            // 2. Highlighta kategori-knappen (behövs inte om Toggle-grafiken sköter det)
            // HighlightCategoryButton(category); // Kan tas bort

            // 3. Anropa central UI-uppdatering
            UpdateOwnedBuildingUI();
        }

        // Fyller på panelen med byggbara objekt för vald kategori
        public void UpdateBuildablesPanel(BuildingType category)
        {
            if (slotsContainer == null || buildableItemButtonPrefab == null || buildableDatabase == null) return;
            foreach (Transform child in slotsContainer) Destroy(child.gameObject);

            List<BuildableData> itemsToShow = buildableDatabase.GetBuildablesForCategory(category);
            // TODO: Filtrera itemsToShow baserat på forskning/krav

            foreach (BuildableData data in itemsToShow)
            {
                GameObject itemGO = Instantiate(buildableItemButtonPrefab, slotsContainer);
                BuildableItemButtonUI buttonUI = itemGO.GetComponent<BuildableItemButtonUI>();
                if (buttonUI != null)
                {
                    buttonUI.Initialize(data, this);
                    buttonUI.UpdateState(GetActiveBuildingForQueue()); // Uppdatera direkt
                }
            }
        }

        // Uppdaterar status för alla knappar i buildablesPanel baserat på vald byggnad
        private void UpdateBuildablesButtonStates()
        {
            if (slotsContainer == null) return;
            ProductionBuilding activeBuilding = GetActiveBuildingForQueue();
            foreach (Transform child in slotsContainer)
            {
                BuildableItemButtonUI buttonUI = child.GetComponent<BuildableItemButtonUI>();
                buttonUI?.UpdateState(activeBuilding);
            }
        }

        // Visar ikoner för ägda byggnader av vald kategori (om fler än 1)
        public void UpdateBuildingCountDisplay(Dictionary<BuildingType, List<Building>> buildingsOwned)
        {
            if (buildingCountPanelContainer == null || buildingIconButtonPrefab == null || buildingCountPanel == null) return;
            foreach (Transform child in buildingCountPanelContainer) Destroy(child.gameObject);
            highlightedIconButton = null;
            List<Building> buildingsOfType = null;

            selectedBuildingInstance = null; // Nollställ alltid innan ny koll
            selectedProductionBuildingInstance = null;

            bool categoryHasOwnedBuildings = selectedCategoryType != BuildingType.None
                                             && buildingsOwned != null
                                             && buildingsOwned.TryGetValue(selectedCategoryType, out buildingsOfType)
                                             && buildingsOfType.Count > 0;

            if (categoryHasOwnedBuildings)
            {
                int count = buildingsOfType.Count;
                bool showPanel = count > 1;
                buildingCountPanel.SetActive(showPanel);

                if (showPanel)
                {
                    for (int i = 0; i < count; i++)
                    {
                        GameObject iconGO = Instantiate(buildingIconButtonPrefab, buildingCountPanelContainer);
                        BuildingIconButtonHandler iconHandler = iconGO.GetComponent<BuildingIconButtonHandler>();
                        if (iconHandler != null && buildingsOfType[i] != null)
                        {
                            iconHandler.AssociatedBuilding = buildingsOfType[i];
                            Image iconImage = iconGO.transform.Find("Icon")?.GetComponent<Image>();
                            if (iconImage) { iconImage.sprite = GetSpriteForBuilding(buildingsOfType[i]); }
                        }
                        else if (buildingsOfType[i] == null) { Destroy(iconGO); } // Ignorera null byggnader
                        else { Destroy(iconGO); } // Ignorera om prefab saknar script
                    }
                    // Ingen förvald byggnad när panelen visas
                }
                else // count == 1
                {
                    selectedBuildingInstance = buildingsOfType[0];
                    selectedProductionBuildingInstance = buildingsOfType[0] as ProductionBuilding;
                }
            }
            else
            {
                buildingCountPanel.SetActive(false);
            }
        }

        // Anropas när en knapp i buildablesPanel klickas
        public void OnBuildableItemClicked(BuildableData itemData)
        {
            if (localPlayer == null) { Debug.LogError("Local player not found!"); return; }

            if (itemData.itemType == BuildableItemType.Building)
            {
                if (buildingPlacer != null)
                {
                    CloseBuildMenus(); // Stäng menyn innan placering
                    buildingPlacer.StartPlacement(itemData);
                }
                else { Debug.LogError("BuildingPlacer reference not set!"); }
            }
            else if (itemData.itemType == BuildableItemType.Unit || itemData.itemType == BuildableItemType.Upgrade)
            {
                ProductionBuilding targetBuilding = GetActiveBuildingForQueue();
                if (targetBuilding != null)
                {
                    localPlayer.CmdQueueItem(targetBuilding.netId, itemData.buildableId, 1);
                }
                else { ShowNotification("No suitable production building selected!"); }
            }
        }

        // Anropas när en ikon i buildingCountPanel klickas
        public void SelectBuildingInstance(Building instance, BuildingIconButtonHandler clickedHandler)
        {
            if (selectedBuildingInstance == instance) return; // Ingen ändring
            RemoveHighlightFromBuildingIcon(); // Ta bort gammal highlight

            selectedBuildingInstance = instance;
            selectedProductionBuildingInstance = instance as ProductionBuilding;
            highlightedIconButton = clickedHandler;
            highlightedIconButton?.SetHighlightActive(true); // Sätt ny highlight

            UpdateBuildablesButtonStates(); // Uppdatera byggknappar
        }

        // Anropas av hotkeys etc.
        public void ToggleBuildMenu()
        {
            if (buildMenuToggle != null)
            {
                buildMenuToggle.isOn = !buildMenuToggle.isOn; // Låt Toggle-eventet sköta resten
            }
            else
            {
                bool currentVisibility = buildCategoryPanel != null && buildCategoryPanel.activeSelf;
                SetBuildMenuVisibility(!currentVisibility); // Fallback
            }
        }

        // Stänger menyerna
        public void CloseBuildMenus()
        {
            SetBuildMenuVisibility(false);
            if (buildMenuToggle != null && buildMenuToggle.isOn)
            {
                buildMenuToggle.SetIsOnWithoutNotify(false);
            }
        }

        // --- Diverse Hjälpfunktioner ---

        // *** ÄNDRAD: Hanterar nu Toggle istället för Button ***
        private void InitializeCategoryButtons()
        {
            if (buildCategoryPanel == null)
            {
                Debug.LogError("BuildCategoryPanel reference not set in UIManager!");
                return;
            }

            // Använder nu Dictionary<BuildingType, Toggle>
            categoryButtonsDict.Clear();

            foreach (CategoryButtonHelper helper in buildCategoryPanel.GetComponentsInChildren<CategoryButtonHelper>())
            {
                Toggle toggle = helper.GetComponent<Toggle>(); // Hämta Toggle
                BuildingType category = helper.categoryToSet;

                if (toggle != null && category != BuildingType.None)
                {
                    toggle.onValueChanged.RemoveAllListeners(); // Rensa gamla
                    // Lägg till lyssnare som anropar SelectCategory ENDAST när toggle slås PÅ
                    toggle.onValueChanged.AddListener((isOn) => {
                        if (isOn)
                        {
                            SelectCategory(category);
                        }
                    });
                    categoryButtonsDict[category] = toggle; // Spara Toggle i dictionaryn
                }
                else { Debug.LogWarning($"CategoryButtonHelper on {helper.gameObject.name} missing Toggle or Category is None."); }
            }
        }

        // *** UPPDATERAD: Tar nu Dictionary som argument ***
        private void UpdateCategoryButtonStates(Dictionary<BuildingType, List<Building>> buildingsOwned)
        {
            if (categoryButtonsDict == null) return;

            foreach (var kvp in categoryButtonsDict)
            {
                BuildingType category = kvp.Key;
                Toggle toggle = kvp.Value; // Hämta Toggle istället för Button
                if (toggle == null) continue;

                bool isActive = buildingsOwned.ContainsKey(category) && buildingsOwned[category].Count > 0;

                toggle.interactable = isActive; // Sätt interaktivitet på Toggle

                // Uppdatera visuellt (t.ex. Target Graphic)
                Image img = toggle.targetGraphic as Image; // Toggle har targetGraphic direkt
                if (img != null)
                {
                    img.color = isActive ? Color.white : Color.grey; // Enkel färgändring
                }
                // Notera: Den visuella skillnaden mellan på/av hanteras av Togglen's
                // "Graphic" (under Toggle Transition) och dess inställningar i Inspektorn.
            }
        }

        // *** NY: Central UI-uppdateringsmetod ***
        public void UpdateOwnedBuildingUI()
        {
            var buildingsOwned = GetCurrentBuildingData(); // Hämtar och grupperar
            UpdateCategoryButtonStates(buildingsOwned);   // Uppdaterar kategoriknappars state
            UpdateBuildingCountDisplay(buildingsOwned);   // Uppdaterar listan med byggnadsikoner
            UpdateBuildablesButtonStates();               // Uppdaterar knapparna i buildablesPanel
        }


        // *** BORTKOMMENTERAD/ERSATT: Behövs inte för grundläggande highlightning med Toggles ***
        // private void HighlightCategoryButton(BuildingType category)
        // {
        //     // Denna logik sköts nu primärt av Toggle-komponentens visuella inställningar
        //     // (Target Graphic, Graphic, Selected Color etc.) som sätts i Inspektorn.
        //     // Man kan lägga till extra logik här om man vill göra mer avancerade saker.
        // }

        private void RemoveHighlightFromBuildingIcon()
        {
            highlightedIconButton?.SetHighlightActive(false);
            highlightedIconButton = null;
        }

        // *** KRITISK FUNKTION ATT IMPLEMENTERA ***
        private Dictionary<BuildingType, List<Building>> GetCurrentBuildingData()
        {
            var groupedBuildings = new Dictionary<BuildingType, List<Building>>();
            if (localPlayer == null) return groupedBuildings;

            List<Building> ownedBuildings = localPlayer.GetOwnedBuildings(); // Anropar metoden på NetworkPlayer

            if (ownedBuildings == null) return groupedBuildings;

            foreach (Building building in ownedBuildings)
            {
                if (building == null) continue;
                BuildingType category = GetCategoryForBuilding(building); // Anropa hjälpmetod
                if (category != BuildingType.None)
                {
                    if (!groupedBuildings.ContainsKey(category))
                    {
                        groupedBuildings[category] = new List<Building>();
                    }
                    groupedBuildings[category].Add(building);
                }
            }
            return groupedBuildings;
        }

        // *** KRITISK FUNKTION ATT IMPLEMENTERA ***
        private BuildingType GetCategoryForBuilding(Building building)
        {
            if (building == null) return BuildingType.None;
            // --- IMPLEMENTERA DIN LOGIK HÄR ---
            // Alternativ 1: Hämta från Building-objektet direkt
            // return building.Category; // Om Building har en Category-property

            // Alternativ 2: Hämta från BuildableData via ID
            // string buildableId = building.buildableId; // Om Building har ett buildableId
            // BuildableData data = buildableDatabase?.GetDataById(buildableId);
            // return data?.category ?? BuildingType.None;

            // Fallback/Placeholder: Gissa baserat på klassnamn
            if (building is ProductionBuilding)
            {
                if (building.GetType().Name.Contains("Barracks")) return BuildingType.Infantry;
                if (building.GetType().Name.Contains("Stable")) return BuildingType.Cavalry;
                if (building.GetType().Name.Contains("Airfield")) return BuildingType.Flying; // Exempel
                                                                                              // Fler...
                return BuildingType.Building; // Generell produktionsbyggnad?
            }
            if (building.GetType().Name.Contains("Tower") || building.GetType().Name.Contains("Wall")) return BuildingType.Defence; // Exempel
            if (building.GetType().Name.Contains("Townhall")) return BuildingType.Building; // Exempel
                                                                                            // Fler...
            Debug.LogWarning($"Could not determine category for building: {building.name}");
            return BuildingType.Building; // Generell fallback
        }

        public ProductionBuilding GetActiveBuildingForQueue()
        {
            if (selectedProductionBuildingInstance != null) { return selectedProductionBuildingInstance; }
            var buildingsOwned = GetCurrentBuildingData();
            if (buildingsOwned != null && buildingsOwned.TryGetValue(selectedCategoryType, out var buildingsOfType))
            {
                if (buildingsOfType.Count == 1 && buildingsOfType[0] is ProductionBuilding singleProdBuilding)
                {
                    return singleProdBuilding;
                }
            }
            return null;
        }

        private Sprite GetSpriteForBuilding(Building building)
        {
            if (building == null || buildableDatabase == null) return null;
            // --- IMPLEMENTERA DIN LOGIK HÄR ---
            // Försök hämta via buildableId på byggnaden
            string idToLookup = building.buildableId; // Antagande att fältet finns
            if (!string.IsNullOrEmpty(idToLookup))
            {
                BuildableData data = buildableDatabase.GetDataById(idToLookup);
                if (data != null) return data.icon;
            }
            // Fallback (eller returnera null/default)
            return null;
        }

        public void HandlePlayerStatusChange(PlayerStatus newStatus) { /* ... (som innan) ... */ }
        public void ShowPowerWarning(bool show) { /* ... (som innan) ... */ }
        public void UpdatePlayerList() { /* ... (som innan, behöver implementation) ... */ }
        public void ShowError(string errorMessage) { /* ... (som innan) ... */ }
        private System.Collections.IEnumerator ClearNotificationAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay); // Vänta

            if (notificationText != null) // Om textfältet finns kvar...
            {
                if (notificationText.text.StartsWith("<color=red>")) // ...och texten är ett felmeddelande...
                {
                    notificationText.text = ""; // ...rensa texten.
                }
                // Vad händer HÄR om texten INTE börjar med <color=red>? Metoden slutar utan yield.
            }
            // Vad händer HÄR om notificationText är null? Metoden slutar utan yield.
        } // <--- Kompilatorn klagar här

    } // End class UIManager
} // End namespace RTSGAME