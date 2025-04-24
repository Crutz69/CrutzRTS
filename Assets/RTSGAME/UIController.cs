using UnityEngine;
// using UnityEngine.UI; // Behövs ej om du bara använder TMP för text, men behåll för Slider, Button etc.
using System.Collections.Generic;
using TMPro; // För TextMeshPro
using Mirror; // För NetworkClient etc.

// Namespace börjar här
namespace RTSGAME
{
    public class UIController : MonoBehaviour
    {
        [Header("Data References")]
        public BuildableDatabase buildableDatabase; // Dra ditt Database-Asset hit!

        [Header("UI Panel References")]
        public GameObject buildCategoryPanel; // Panelen med kategoriknappar
        public GameObject buildablesPanel;    // Panelen med bygg-ikoner/knappar
        public GameObject buildingCountPanel; // Panelen som visar ikoner för ägda byggnader av vald typ
        // Lägg till referens till ManaBar, Credits Text etc.

        [Header("Prefab & Container References")]
        public GameObject buildableItemButtonPrefab; // Dra din BuildableItem_Prefab hit!
        public Transform slotsContainer;           // Dra din SlotsContainer (barn till BuildablesPanel) hit!
        public GameObject buildingIconButtonPrefab;  // Dra din BuildingIconButton_Prefab hit!
        public Transform buildingCountPanelContainer; // Dra din BuildingCountPanel (den som har Horiz Layout Group) hit!

        [Header("Component References")]
        public TextMeshProUGUI creditsText; // Koppla din Credit-text här
        // Koppla referenser till ManaBarController etc.

        [Header("External System References")]
        public BuildingPlacer buildingPlacer; // Dra objektet med BuildingPlacer-scriptet hit

        // --- Intern State ---
        private BuildingType selectedCategoryType = BuildingType.None;
        private Building selectedBuildingInstance = null; // Den specifika byggnadsinstansen som är vald (om count > 1)
        private NetworkPlayer localPlayer; // Den lokala spelarens NetworkPlayer script

        // Hållare för knappar för att kunna hantera highlights etc.
        private Dictionary<BuildingType, Button> categoryButtons = new Dictionary<BuildingType, Button>(); // TODO: Koppla dessa!
        private BuildingIconButtonHandler highlightedIconButton = null; // För building count display

        void Start()
        {
            // Försök hitta lokala spelaren
            // Detta kan behöva göras via en manager eller när spelaren spawnar
            // localPlayer = NetworkClient.localPlayer?.GetComponent<NetworkPlayer>();

            // Göm paneler från start
            buildCategoryPanel?.SetActive(false);
            buildablesPanel?.SetActive(false);
            buildingCountPanel?.SetActive(false);

            // TODO: Initiera categoryButtons-dictionaryn genom att koppla knapparna
            // (Antingen via Inspector-referenser eller Find/GetComponentInChildren)
        }

        void Update()
        {
            // Uppdatera Credits, Mana Bar etc. baserat på data från ResourceManager/Player
            // UpdateCreditsDisplay();
            // UpdateManaBar();
        }

        // --- Kategori Val ---

        // Anropas av CategoryButtonHelper när en kategoriknapp klickas
        public void SelectCategory(BuildingType category)
        {
            if (buildCategoryPanel == null || buildablesPanel == null) return;

            selectedCategoryType = category;
            selectedBuildingInstance = null; // Nollställ specifik instans
            RemoveHighlightFromBuildingIcon(); // Nollställ highlight i övre raden

            // Visa/Dölj paneler (om de inte redan är synliga via HammerButton)
            // Detta antar att HammerButton visar BÅDE kategori och buildables
            buildCategoryPanel.SetActive(true); // Se till att den är synlig
            buildablesPanel.SetActive(true);    // Visa bygg-panelen

            // TODO: Highlighta den klickade kategoriknappen och avmarkera andra
            HighlightCategoryButton(category);

            // Uppdatera båda panelerna baserat på den nya KATEGORIN
            UpdateBuildingCountDisplay(GetCurrentBuildingData()); // Uppdatera ikonraden
            UpdateBuildablesPanel(selectedCategoryType);          // Fyll bygg-panelen
        }

        private void HighlightCategoryButton(BuildingType category)
        {
            // Loopa igenom categoryButtons dictionary
            // Sätt "selected" state på knappen för 'category'
            // Återställ state för alla andra
            Debug.Log($"Highlighting category: {category}");
        }

        // --- Uppdatering av Paneler ---

        public void UpdateBuildablesPanel(BuildingType category)
        {
            // Säkerhetskollar - Avbryt om nödvändiga referenser saknas
            if (slotsContainer == null) { Debug.LogError("Slots Container reference is not set!"); return; }
            if (buildableItemButtonPrefab == null) { Debug.LogError("Buildable Item Prefab reference is not set!"); return; }
            if (buildableDatabase == null) { Debug.LogError("Buildable Database reference is not set!"); return; }

            // 1. Rensa gamla ikoner/knappar
            foreach (Transform child in slotsContainer)
            {
                Destroy(child.gameObject);
            }

            // 2. Hämta listan med byggbara saker
            List<BuildableData> itemsToShow = buildableDatabase.GetBuildablesForCategory(category);
            // TODO: Filtrera bort låsta items

            Debug.Log($"Found {itemsToShow.Count} items for category {category}. Populating panel...");

            // 3. Loopa och skapa knappar
            foreach (BuildableData data in itemsToShow)
            {
                GameObject itemGO = Instantiate(buildableItemButtonPrefab, slotsContainer);

                // Försök hämta knappens UI-script för mer avancerad setup
                BuildableItemButtonUI buttonUI = itemGO.GetComponent<BuildableItemButtonUI>();
                if (buttonUI != null)
                {
                    buttonUI.Initialize(data, this);
                    Building activeBuilding = GetActiveBuildingForQueue();
                    buttonUI.UpdateState(activeBuilding);
                }
                else
                {
                    // Fallback om scriptet saknas: Sätt bara ikon
                    Debug.LogWarning($"BuildableItemButtonUI script not found on prefab instance for {data.buildableName}. Setting icon only.");
                    Image iconImage = itemGO.transform.Find("Icon")?.GetComponent<Image>();
                    if (iconImage != null && data.icon != null) iconImage.sprite = data.icon;

                    // Lägg till enkel OnClick fallback om Button finns men inte scriptet?
                    Button itemButton = itemGO.GetComponent<Button>();
                    if (itemButton)
                    {
                        BuildableData currentData = data;
                        itemButton.onClick.RemoveAllListeners(); // Rensa ev. gamla
                        itemButton.onClick.AddListener(() => OnBuildableItemClicked(currentData)); // Använd huvudfunktionen
                    }
                }
            }
        }


        public void UpdateBuildingCountDisplay(Dictionary<BuildingType, List<Building>> buildingsOwned)
        {
            // Hela implementationen från tidigare...
            if (buildingCountPanelContainer == null || buildingIconButtonPrefab == null || buildingCountPanel == null) return;
            foreach (Transform child in buildingCountPanelContainer) Destroy(child.gameObject);
            highlightedIconButton = null;

            List<Building> buildingsOfType = null;
            if (selectedCategoryType != BuildingType.None && buildingsOwned != null && buildingsOwned.TryGetValue(selectedCategoryType, out buildingsOfType))
            {
                int count = buildingsOfType.Count;
                bool showPanel = count > 1;
                buildingCountPanel.gameObject.SetActive(showPanel);

                if (showPanel)
                {
                    for (int i = 0; i < count; i++)
                    {
                        GameObject iconGO = Instantiate(buildingIconButtonPrefab, buildingCountPanelContainer);
                        BuildingIconButtonHandler iconHandler = iconGO.GetComponent<BuildingIconButtonHandler>();

                        if (iconHandler != null)
                        {
                            iconHandler.AssociatedBuilding = buildingsOfType[i];
                            Image iconImage = iconGO.transform.Find("Icon")?.GetComponent<Image>();
                            if (iconImage) iconImage.sprite = GetSpriteForBuildingType(selectedCategoryType);

                            if (selectedBuildingInstance == buildingsOfType[i]) { HighlightIconButton(iconHandler); }
                        }
                        else { /* Fallback */ }
                    }
                    // ScaleIconsToFit();
                }
                else if (count == 1) { selectedBuildingInstance = buildingsOfType[0]; }
                else { selectedBuildingInstance = null; }
            }
            else
            {
                buildingCountPanel.gameObject.SetActive(false);
                selectedBuildingInstance = null;
            }
        }


        // --- Klickhantering (anropas av knapparnas scripts) ---

        public void OnBuildableItemClicked(BuildableData itemData)
        {
            // Hela implementationen från tidigare...
            Debug.Log($"UIController received click for: {itemData.buildableName}");
            if (localPlayer == null) localPlayer = NetworkClient.localPlayer?.GetComponent<NetworkPlayer>();
            if (localPlayer == null) { Debug.LogError("Local player not found!"); return; }

            if (itemData.itemType == BuildableItemType.Building)
            {
                if (buildingPlacer != null) { buildingPlacer.StartPlacement(itemData); /*CloseBuildMenus();*/ }
                else { Debug.LogError("BuildingPlacer reference not set!"); }
            }
            else if (itemData.itemType == BuildableItemType.Unit || itemData.itemType == BuildableItemType.Upgrade)
            {
                Building targetBuilding = GetActiveBuildingForQueue();
                if (targetBuilding != null)
                {
                    localPlayer.CmdQueueItem(targetBuilding.netId, itemData.buildableId, 1);
                }
                else { Debug.LogWarning("No building selected/available to queue the item at!"); }
            }
        }

        public void SelectBuildingInstance(Building instance, BuildingIconButtonHandler clickedHandler)
        {
            // Hela implementationen från tidigare...
            if (selectedBuildingInstance == instance) return;
            RemoveHighlightFromBuildingIcon();
            selectedBuildingInstance = instance;
            HighlightIconButton(clickedHandler);
            Debug.Log($"UIController selected specific building instance: {instance.name}");
        }


        // --- Hjälpfunktioner (Behöver implementation/anpassning) ---
        private void HighlightIconButton(BuildingIconButtonHandler handlerToHighlight) { highlightedIconButton = handlerToHighlight; highlightedIconButton?.SetHighlightActive(true); }
        private void RemoveHighlightFromBuildingIcon() { highlightedIconButton?.SetHighlightActive(false); highlightedIconButton = null; }
        private Dictionary<BuildingType, List<Building>> GetCurrentBuildingData() { Debug.LogWarning("GetCurrentBuildingData() needs implementation!"); return new Dictionary<BuildingType, List<Building>>(); }
        private Building GetActiveBuildingForQueue() { Debug.LogWarning("GetActiveBuildingForQueue() needs implementation! Returning selected instance for now."); return selectedBuildingInstance; }
        private bool CanQueueItems(BuildingType type) { return type == BuildingType.House || type == BuildingType.Shield; } // Anpassa!
        private Sprite GetSpriteForBuildingType(BuildingType type) { Debug.LogWarning("GetSpriteForBuildingType() needs implementation!"); return null; }
        // private void ScaleIconsToFit() { /* ... */ }

        // --- Funktioner för att öppna/stänga menyer (anropas av t.ex. HammerButton) ---
        public void ToggleBuildMenu()
        {
            // Hela implementationen från tidigare...
            bool isActive = buildCategoryPanel.activeSelf;
            buildCategoryPanel.SetActive(!isActive);
            buildablesPanel.SetActive(!isActive);
            bool showCountPanel = !isActive && selectedCategoryType != BuildingType.None && GetCurrentBuildingData().ContainsKey(selectedCategoryType) && GetCurrentBuildingData()[selectedCategoryType].Count > 1;
            buildingCountPanel.SetActive(showCountPanel);

            if (!isActive)
            {
                if (selectedCategoryType == BuildingType.None) { SelectCategory(BuildingType.House); }
                else { SelectCategory(selectedCategoryType); } // Uppdatera vid öppning
            }
            else
            {
                RemoveHighlightFromBuildingIcon();
            }
        }
        public void CloseBuildMenus()
        {
            buildCategoryPanel.SetActive(false);
            buildablesPanel.SetActive(false);
            buildingCountPanel.SetActive(false);
            selectedCategoryType = BuildingType.None; // Nollställ vid stängning?
            RemoveHighlightFromBuildingIcon();
        }

    } // Slut på klassen UIController

} // Slut på namespace RTSGAME