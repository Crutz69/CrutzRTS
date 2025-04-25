// Filnamn: UIManager.cs
using UnityEngine;
using UnityEngine.UI;            // Behålls för Slider, Button, RawImage, Image etc.
using TMPro;                     // För TextMeshPro
using System.Collections.Generic;
using System.Linq;               // För FirstOrDefault
using Mirror;                    // För NetworkClient etc.

namespace RTSGAME
{
    public class UIManager : MonoBehaviour // Klassen heter nu UIManager
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

        [SyncVar] // Behåll SyncVar om den ska synkas
        public uint capturingWorkerNetId = 0; // <-- VIKTIGT: Den måste vara 'public'

        [Header("Selection Panel")]
        [SerializeField] private GameObject selectionPanel;
        [SerializeField] private TextMeshProUGUI selectionNameText;
        [SerializeField] private Slider selectionHealthSlider;
        [SerializeField] private Slider selectionProgressBar; // Kan behöva separeras för konstruktion vs produktion?
        [SerializeField] private GameObject productionQueuePanel; // Panel för produktionskö
        [SerializeField] private Transform productionQueueSlotsContainer; // Container för kö-ikoner
        [SerializeField] private GameObject queueItemIconPrefab; // Prefab för kö-ikon

        [Header("Minimap")]
        [SerializeField] private RawImage minimapImage;
        // TODO: Minimap logik

        [Header("Notifications")]
        [SerializeField] private TextMeshProUGUI notificationText;
        // TODO: Notifikationslogik

        [Header("Build Menu System")]
        [SerializeField] private GameObject buildCategoryPanel;
        [SerializeField] private GameObject buildablesPanel;
        [SerializeField] private Transform slotsContainer;
        [SerializeField] private GameObject buildableItemButtonPrefab;
        [SerializeField] private GameObject buildingCountPanel;
        [SerializeField] private Transform buildingCountPanelContainer;
        [SerializeField] private GameObject buildingIconButtonPrefab;

        [Header("External System References")]
        [SerializeField] private BuildingPlacer buildingPlacer;

        // --- Intern State för Byggmeny ---
        private BuildingType selectedCategoryType = BuildingType.None;
        // ÄNDRAD: Håller nu koll på en ProductionBuilding specifikt om det är en sådan som är aktiv för kön
        private ProductionBuilding selectedProductionBuildingInstance = null;
        private Building selectedBuildingInstance = null; // Generell vald byggnad (kan vara samma som ovan)
        private Dictionary<BuildingType, Button> categoryButtonsDict = new Dictionary<BuildingType, Button>();
        private BuildingIconButtonHandler highlightedIconButton = null;

        // --- Unity Metoder ---
        void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }

            if (selectionPanel) selectionPanel.SetActive(false);
            if (buildCategoryPanel) buildCategoryPanel.SetActive(false);
            if (buildablesPanel) buildablesPanel.SetActive(false);
            if (buildingCountPanel) buildingCountPanel.SetActive(false);
            if (productionQueuePanel) productionQueuePanel.SetActive(false);
        }

        void Start()
        {
            if (SelectionManager.Instance != null) { SelectionManager.Instance.OnSelectionChanged += UpdateSelectionPanel; }
            else { Debug.LogWarning("SelectionManager not found during UIManager Start."); }
            StartCoroutine(FindLocalPlayerRoutine());
            InitializeCategoryButtons();
        }

        void OnDestroy()
        {
            if (SelectionManager.Instance != null) { SelectionManager.Instance.OnSelectionChanged -= UpdateSelectionPanel; }
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
            }
            else if (NetworkClient.active) { Debug.LogWarning("UIManager could not find Local Player."); }
        }

        // --- Metoder för att sätta referenser och uppdatera UI ---

        public void SetLocalPlayer(NetworkPlayer player) { localPlayer = player; }
        public void UpdateCreditsDisplay(int amount) { if (creditsText != null) creditsText.text = $"{amount}"; }
        public void UpdateManaRelatedUI(int generation, int upkeep, bool hasPower) { manaBarController?.UpdateGeneration(generation); manaBarController?.UpdateUpkeep(upkeep); manaBarController?.UpdatePowerStatus(hasPower); }

        // *** VIKTIG ÄNDRING HÄR ***
        public void UpdateSelectionPanel()
        {
            if (selectionPanel == null || SelectionManager.Instance == null) return;
            List<GameObject> selection = SelectionManager.Instance.GetSelectedObjects();

            if (selection.Count == 1)
            {
                GameObject selectedObj = selection[0];
                selectionPanel.SetActive(true);

                // Återställ / standardvärden
                string nameToShow = "Selected Object";
                float currentHealth = 0f, maxHealth = 1f;
                float progressToShow = 0f; // För progress bar (konstruktion/produktion)
                bool showProgress = false;
                // För produktionskö
                List<string> queueToShow = null;
                string buildingItemId = null;
                float buildingProgress = 0f;
                BuildPauseState pauseState = BuildPauseState.None; // ANVÄNDER ENUM FRÅN Enums.cs
                bool showProduction = false;

                // *** NY LOGIK: Kolla först om det är en produktionsbyggnad ***
                if (selectedObj.TryGetComponent<ProductionBuilding>(out ProductionBuilding prodBuilding))
                {
                    selectedBuildingInstance = prodBuilding; // Spara som generell byggnad också
                    selectedProductionBuildingInstance = prodBuilding; // Spara specifikt som produktionsbyggnad

                    // Hämta grundinfo från Building/ProductionBuilding
                    nameToShow = prodBuilding.BuildingName;
                    if(prodBuilding.healthComponent != null)
                    {
                        currentHealth = prodBuilding.CurrentHealth;
                        maxHealth = prodBuilding.MaxHealth;
                    }

                    // Visa progress bar för konstruktion ELLER produktion? Behöver mer logik här.
                    if (prodBuilding.CurrentState == BuildingState.Constructing)
                    {
                        progressToShow = prodBuilding.ConstructionProgress;
                        showProgress = true;
                    }
                    else if (!string.IsNullOrEmpty(prodBuilding.syncCurrentlyBuildingId)) // Visa produktionsprogress om något byggs
                    {
                        progressToShow = prodBuilding.syncCurrentBuildProgress;
                        showProgress = true;
                    }
                    else { showProgress = false; }


                    // Hämta produktionskö-info direkt från prodBuilding
                    if (prodBuilding.CanQueueItems()) // Metoden finns nu här
                    {
                        showProduction = true;
                        queueToShow = new List<string>(prodBuilding.syncBuildQueueIds);
                        buildingItemId = prodBuilding.syncCurrentlyBuildingId;
                        buildingProgress = prodBuilding.syncCurrentBuildProgress; // Redundant med progressToShow? Se ovan.
                        pauseState = prodBuilding.syncCurrentPauseState; // Läs SyncVar
                    }
                }
                // *** Om inte ProductionBuilding, kolla om det är en vanlig Building ***
                else if (selectedObj.TryGetComponent<Building>(out Building building))
                {
                    selectedBuildingInstance = building;
                    selectedProductionBuildingInstance = null; // Inte en produktionsbyggnad

                    nameToShow = building.BuildingName;
                     if(building.healthComponent != null)
                    {
                        currentHealth = building.CurrentHealth;
                        maxHealth = building.MaxHealth;
                    }

                    // Visa progress bar endast för konstruktion
                    if (building.CurrentState == BuildingState.Constructing)
                    {
                         progressToShow = building.ConstructionProgress;
                         showProgress = true;
                    } else { showProgress = false; }


                    showProduction = false; // Vanliga byggnader kan inte producera
                }
                // *** Kolla efter enhet eller annat? ***
                else if (selectedObj.TryGetComponent<Unit>(out Unit unit)) // Antag att du har en Unit-klass
                {
                    selectedBuildingInstance = null;
                    selectedProductionBuildingInstance = null;

                    nameToShow = unit.UnitDisplayName; // Antag att Unit har UnitName property
                    // Hämta hälsa från Unit...
                    showProduction = false;
                    showProgress = false;
                }
                else // Okänt objekt valt
                {
                     selectedBuildingInstance = null;
                    selectedProductionBuildingInstance = null;
                    showProduction = false;
                    showProgress = false;
                }

                // Uppdatera UI-element
                selectionNameText.text = nameToShow;
                selectionHealthSlider.value = (maxHealth > 0) ? (currentHealth / maxHealth) : 0;
                selectionProgressBar.gameObject.SetActive(showProgress);
                if(showProgress) selectionProgressBar.value = progressToShow;

                UpdateProductionQueuePanel(showProduction, queueToShow, buildingItemId, buildingProgress, pauseState);
            }
            else if (selection.Count > 1) // Flera objekt valda
            {
                selectionPanel.SetActive(true);
                selectionNameText.text = $"{selection.Count} Objects Selected";
                selectionHealthSlider.value = 1; // Eller visa genomsnitt/lägsta?
                 selectionProgressBar.gameObject.SetActive(false);
                UpdateProductionQueuePanel(false, null, null, 0f, BuildPauseState.None); // Göm kön
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

        // *** ANVÄNDER BuildPauseState från Enums.cs ***
        private void UpdateProductionQueuePanel(bool show, List<string> queueIds, string currentlyBuildingId, float progress, BuildPauseState pauseState)
        {
            if (productionQueuePanel == null || productionQueueSlotsContainer == null || queueItemIconPrefab == null || buildableDatabase == null) return;

            productionQueuePanel.SetActive(show);
            if (!show) return;

            // Rensa gamla ikoner
            foreach (Transform child in productionQueueSlotsContainer) Destroy(child.gameObject);

            // Visa ikon för det som byggs aktivt
            if (!string.IsNullOrEmpty(currentlyBuildingId))
            {
                BuildableData data = buildableDatabase.GetDataById(currentlyBuildingId);
                if (data != null)
                {
                    GameObject iconGO = Instantiate(queueItemIconPrefab, productionQueueSlotsContainer);
                    // TODO: Konfigurera ikon för aktiv + progress/paus
                    Image img = iconGO.GetComponentInChildren<Image>(); if (img) img.sprite = data.icon;
                    // Exempel: Lägg till en Slider eller fyllnads-Image för progress
                    // Exempel: Ändra färg/lägg till ikon om pauseState != BuildPauseState.None
                }
            }

            // Visa ikoner för det som är i kö
            if (queueIds != null)
            {
                foreach (string id in queueIds)
                {
                    BuildableData data = buildableDatabase.GetDataById(id);
                    if (data != null)
                    {
                        GameObject iconGO = Instantiate(queueItemIconPrefab, productionQueueSlotsContainer);
                        // TODO: Konfigurera ikon för köad + ev. högerklick för cancel
                        Image img = iconGO.GetComponentInChildren<Image>(); if (img) img.sprite = data.icon;
                        // Lägg till knapp-komponent och event för att avbryta? Needs Command på spelaren.
                    }
                }
            }
        }

        public void ShowNotification(string message)
        {
            if (notificationText) notificationText.text = message; // TODO: Mer avancerad hantering
            Debug.Log($"UI Notification: {message}");
        }

        // --- Byggmeny Funktioner ---

        public void SelectCategory(BuildingType category)
        {
            if (buildCategoryPanel == null || buildablesPanel == null) return;
            selectedCategoryType = category;
            selectedBuildingInstance = null; // Nollställ vald instans när kategori byts
            selectedProductionBuildingInstance = null;
            RemoveHighlightFromBuildingIcon();

            buildCategoryPanel.SetActive(true);
            buildablesPanel.SetActive(true);
            HighlightCategoryButton(category);
            UpdateBuildablesPanel(selectedCategoryType);

            // Hämta byggnadsdata och uppdatera count display
            var buildingsOwned = GetCurrentBuildingData(); // TODO: Implementera denna!
            UpdateBuildingCountDisplay(buildingsOwned); // Denna sätter selectedBuildingInstance/selectedProductionBuildingInstance om bara en finns

            // Uppdatera byggknapparna igen baserat på *vilken specifik byggnad* som nu är aktiv (om någon)
            UpdateBuildablesButtonStates();
        }


        public void UpdateBuildablesPanel(BuildingType category)
        {
            if (slotsContainer == null || buildableItemButtonPrefab == null || buildableDatabase == null) return;
            foreach (Transform child in slotsContainer) Destroy(child.gameObject);

            List<BuildableData> itemsToShow = buildableDatabase.GetBuildablesForCategory(category);
            // TODO: Filtrera itemsToShow baserat på unlocks/prerequisites

            foreach (BuildableData data in itemsToShow)
            {
                GameObject itemGO = Instantiate(buildableItemButtonPrefab, slotsContainer);
                BuildableItemButtonUI buttonUI = itemGO.GetComponent<BuildableItemButtonUI>();
                if (buttonUI != null)
                {
                    buttonUI.Initialize(data, this);
                    // Uppdatera direkt vid skapande baserat på aktuell aktiv byggnad
                    buttonUI.UpdateState(GetActiveBuildingForQueue()); // Använder nu metoden som hittar ProductionBuilding
                }
                else { Debug.LogWarning($"BuildableItemButtonUI script missing on {itemGO.name}"); }
            }
        }

        // Hjälpmetod för att uppdatera alla byggknappars state
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

        public void UpdateBuildingCountDisplay(Dictionary<BuildingType, List<Building>> buildingsOwned)
        {
            if (buildingCountPanelContainer == null || buildingIconButtonPrefab == null || buildingCountPanel == null) return;
            foreach (Transform child in buildingCountPanelContainer) Destroy(child.gameObject);
            highlightedIconButton = null;
            List<Building> buildingsOfType = null;

            // Nollställ innan vi ev. sätter dem
            selectedBuildingInstance = null;
            selectedProductionBuildingInstance = null;

            if (selectedCategoryType != BuildingType.None && buildingsOwned != null && buildingsOwned.TryGetValue(selectedCategoryType, out buildingsOfType) && buildingsOfType.Count > 0)
            {
                 int count = buildingsOfType.Count;
                 bool showPanel = count > 1; // Visa bara panelen om det finns FLER än en att välja mellan
                 buildingCountPanel.SetActive(showPanel);

                 if(showPanel)
                 {
                      for (int i = 0; i < count; i++)
                      {
                           GameObject iconGO = Instantiate(buildingIconButtonPrefab, buildingCountPanelContainer);
                           BuildingIconButtonHandler iconHandler = iconGO.GetComponent<BuildingIconButtonHandler>();
                           if (iconHandler != null)
                           {
                                iconHandler.AssociatedBuilding = buildingsOfType[i];
                                // Försök sätta ikon från byggnadens data om möjligt
                                Image iconImage = iconGO.transform.Find("Icon")?.GetComponent<Image>();
                                if (iconImage) {
                                    // Försök få data från en BuildableData via ett ID på byggnaden? Kräver mer info.
                                    // För nu, använd kategorispriten.
                                    iconImage.sprite = GetSpriteForBuildingType(selectedCategoryType); // TODO: Implementera bättre?
                                }
                                // Ingen highlightning här, det sker när man klickar
                           }
                      }
                      // Om panelen visas, välj den FÖRSTA i listan som standard? Eller ingen?
                      // selectedBuildingInstance = buildingsOfType[0]; // Designval
                      // selectedProductionBuildingInstance = buildingsOfType[0] as ProductionBuilding; // Designval
                 }
                 else // count == 1
                 {
                     // Om det bara finns en, välj den automatiskt
                     selectedBuildingInstance = buildingsOfType[0];
                     selectedProductionBuildingInstance = buildingsOfType[0] as ProductionBuilding; // Försök casta, blir null om det inte är en prod.byggnad
                 }
            }
            else // Ingen byggnad av vald typ ägs
            {
                buildingCountPanel.SetActive(false);
            }
             // Uppdatera byggknapparnas state baserat på den ev. autovalda byggnaden
             //UpdateBuildablesButtonStates(); // Görs nu från SelectCategory efter denna körts
        }

        // *** ÄNDRAD: Försöker nu hitta ProductionBuilding ***
        public void OnBuildableItemClicked(BuildableData itemData)
        {
            if (localPlayer == null) { FindLocalPlayerRoutine(); if (localPlayer == null) { Debug.LogError("Local player not found!"); return; } }

            if (itemData.itemType == BuildableItemType.Building)
            {
                if (buildingPlacer != null) { buildingPlacer.StartPlacement(itemData); CloseBuildMenus(); }
                else { Debug.LogError("BuildingPlacer reference not set!"); }
            }
            else if (itemData.itemType == BuildableItemType.Unit || itemData.itemType == BuildableItemType.Upgrade)
            {
                // Försök hitta en aktiv produktionsbyggnad
                ProductionBuilding targetBuilding = GetActiveBuildingForQueue();
                if (targetBuilding != null)
                {
                    // Skicka Command till spelaren att köa objektet vid den specifika byggnaden
                    localPlayer.CmdQueueItem(targetBuilding.netId, itemData.buildableId, 1); // Antag CmdQueueItem finns på NetworkPlayer
                    // Ge omedelbar feedback till spelaren? (Kanske lägg till i UI innan server svarar?)
                }
                else { ShowNotification("No suitable production building selected!"); Debug.LogWarning("No production building selected/available to queue the item at!"); }
            }
        }

        // Anropas av BuildingIconButtonHandler vid enkelklick
        public void SelectBuildingInstance(Building instance, BuildingIconButtonHandler clickedHandler)
        {
            if (selectedBuildingInstance == instance) return; // Ingen ändring

            RemoveHighlightFromBuildingIcon(); // Ta bort gammal highlight

            selectedBuildingInstance = instance;
            selectedProductionBuildingInstance = instance as ProductionBuilding; // Försök casta
            highlightedIconButton = clickedHandler;
            highlightedIconButton?.SetHighlightActive(true); // Sätt ny highlight

            // Uppdatera byggknapparna så de reflekterar den nya valda byggnadens status/kö
            UpdateBuildablesButtonStates();
        }

        public void ToggleBuildMenu()
        {
            bool shouldBeActive = !(buildCategoryPanel?.activeSelf ?? false);
            buildCategoryPanel?.SetActive(shouldBeActive);
            buildablesPanel?.SetActive(shouldBeActive);
            // buildingCountPanel aktiveras/avaktiveras av UpdateBuildingCountDisplay

            if (shouldBeActive)
            {
                // Välj en default-kategori om ingen är vald, annars återvälj senaste
                if (selectedCategoryType == BuildingType.None) { SelectCategory(BuildingType.Building); } // Välj en rimlig startkategori
                else { SelectCategory(selectedCategoryType); } // Uppdatera med nuvarande data
            }
            else
            {
                CloseBuildMenus();
            }
        }

        public void CloseBuildMenus()
        {
            buildCategoryPanel?.SetActive(false);
            buildablesPanel?.SetActive(false);
            buildingCountPanel?.SetActive(false);
            RemoveHighlightFromBuildingIcon();
            // Behåll selectedCategoryType? Ja, troligen bäst.
        }

        // --- Diverse Hjälpfunktioner ---
        private void InitializeCategoryButtons() { /* TODO */ Debug.LogWarning("InitializeCategoryButtons needs implementation!"); }
        private void HighlightCategoryButton(BuildingType category) { /* TODO */ Debug.Log($"Highlighting category: {category}"); }
        private void HighlightIconButton(BuildingIconButtonHandler handlerToHighlight) { highlightedIconButton = handlerToHighlight; highlightedIconButton?.SetHighlightActive(true); }
        private void RemoveHighlightFromBuildingIcon() { highlightedIconButton?.SetHighlightActive(false); highlightedIconButton = null; }

        // *** KRITISKA FUNKTIONER ATT IMPLEMENTERA ***
        private Dictionary<BuildingType, List<Building>> GetCurrentBuildingData()
        {
            if (localPlayer == null) return new Dictionary<BuildingType, List<Building>>();
            // TODO: HÄMTA SPELARENS BYGGNADER HÄR!
            // Antag att NetworkPlayer har en lista: List<Building> ownedBuildings;
            // Exempel: return GroupBuildingsByType(localPlayer.ownedBuildings);
            Debug.LogWarning("GetCurrentBuildingData() needs real implementation!");
            return new Dictionary<BuildingType, List<Building>>(); // Returnera tom dict tills implementerad
        }

        // *** ÄNDRAD: Returnerar nu ProductionBuilding ***
        public ProductionBuilding GetActiveBuildingForQueue()
        {
            // Prioritera den specifikt valda ProductionBuilding-instansen
            if (selectedProductionBuildingInstance != null)
            {
                return selectedProductionBuildingInstance;
            }

            // Fallback: Om ingen specifik är vald via ikonerna, men EN produktionsbyggnad av vald kategori är vald via SelectionManager?
            // Detta är lite oklart hur det ska fungera. Kanske räcker det med selectedProductionBuildingInstance?
            // Eller om bara EN byggnad av rätt typ ägs totalt?
            var buildingsOwned = GetCurrentBuildingData();
            if (buildingsOwned != null && buildingsOwned.TryGetValue(selectedCategoryType, out var buildingsOfType))
            {
                 // Försök hitta den första byggnaden i listan som är en ProductionBuilding
                 foreach(Building b in buildingsOfType)
                 {
                     if (b is ProductionBuilding pb) return pb; // Returnera första träffen
                 }
            }

            // Ingen lämplig byggnad hittades
            // Debug.LogWarning("GetActiveBuildingForQueue() could not find a suitable ProductionBuilding!");
            return null;
        }

        // Behövs inte längre här om vi castar och kollar CanQueueItems() på ProductionBuilding
        // private bool CanQueueItems(BuildingType category, Building buildingInstance) { ... }

        private Sprite GetSpriteForBuildingType(BuildingType type)
        {
            // Försök hitta första byggnaden i databasen av den typen för att få en ikon
            BuildableData data = buildableDatabase?.allBuildables.FirstOrDefault(b => b.category == type && b.itemType == BuildableItemType.Building);
            return data?.icon;
        }
    }
}