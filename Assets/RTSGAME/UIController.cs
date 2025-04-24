using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro; // Om du använder TextMeshPro
using Mirror; // För NetworkClient etc.

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
        if (slotsContainer == null || buildableItemButtonPrefab == null || buildableDatabase == null) return;

        // 1. Rensa gamla ikoner/knappar
        foreach (Transform child in slotsContainer)
        {
            Destroy(child.gameObject);
        }

        // 2. Hämta listan med byggbara saker för den valda kategorin
        List<BuildableData> itemsToShow = buildableDatabase.GetBuildablesForCategory(category);
        // TODO: Filtrera bort saker spelaren inte har låst upp (baserat på prerequisites/isUnlockedInitially och spelardata)

        // 3. Loopa igenom listan och skapa knappar
        foreach (BuildableData data in itemsToShow)
        {
            GameObject itemGO = Instantiate(buildableItemButtonPrefab, slotsContainer);
            BuildableItemButtonUI buttonUI = itemGO.GetComponent<BuildableItemButtonUI>(); // Hämta scriptet

            if (buttonUI != null)
            {
                // Skicka med datan och låt knappens script sätta upp sig själv
                buttonUI.Initialize(data, this); // Skicka med UIController som referens? Eller NetworkPlayer?
                                                 // Uppdatera knappens state (låst, kö-antal, progress)
                                                 // Detta kräver att vi vet status för den byggnad som är associerad med denna panel
                Building activeBuilding = GetActiveBuildingForQueue(); // Hämta den byggnad vars kö/status vi ska visa
                buttonUI.UpdateState(activeBuilding); // Uppdatera knappens visuella status
            }
            else
            {
                Debug.LogError("BuildableItemButtonUI script not found on prefab instance!");
                // Fallback: Sätt ikon etc. direkt här som i förra exemplet
                Image icon = itemGO.transform.Find("Icon")?.GetComponent<Image>();
                if (icon) icon.sprite = data.icon;
                // ... sätt text etc ...
                Button btn = itemGO.GetComponent<Button>();
                if (btn)
                {
                    BuildableData currentData = data;
                    btn.onClick.AddListener(() => OnBuildableItemClicked_Fallback(currentData));
                }
            }
        }
    }

    public void UpdateBuildingCountDisplay(Dictionary<BuildingType, List<Building>> buildingsOwned)
    {
        if (buildingCountPanelContainer == null || buildingIconButtonPrefab == null) return;

        // Rensa gamla ikoner
        foreach (Transform child in buildingCountPanelContainer) Destroy(child.gameObject);
        // currentBuildingIcons.Clear(); // Om du lagrar dem
        highlightedIconButton = null; // Byt namn till highlightedIconHandler

        // Hitta byggnaderna för den valda kategorin
        List<Building> buildingsOfType = null;
        if (selectedCategoryType != BuildingType.None && buildingsOwned != null && buildingsOwned.TryGetValue(selectedCategoryType, out buildingsOfType))
        {
            int count = buildingsOfType.Count;
            bool showPanel = count > 1; // Visa bara om fler än 1
            buildingCountPanel.gameObject.SetActive(showPanel); // Aktivera/avaktivera hela panelen

            if (showPanel)
            {
                for (int i = 0; i < count; i++)
                {
                    GameObject iconGO = Instantiate(buildingIconButtonPrefab, buildingCountPanelContainer);
                    BuildingIconButtonHandler iconHandler = iconGO.GetComponent<BuildingIconButtonHandler>(); // Hämta ditt handler-script

                    if (iconHandler != null)
                    {
                        iconHandler.AssociatedBuilding = buildingsOfType[i]; // Sätt rätt byggnad
                        // Sätt ikonen (kräver GetSpriteForBuildingType eller liknande)
                        Image iconImage = iconGO.transform.Find("Icon")?.GetComponent<Image>();
                        if (iconImage) iconImage.sprite = GetSpriteForBuildingType(selectedCategoryType); // Antag att denna funktion finns

                        // Hantera auto-highlight/selection (om nödvändigt)
                        if (selectedBuildingInstance == buildingsOfType[i])
                        {
                            HighlightIconButton(iconHandler); // Highlighta om den var vald innan
                        }
                        else if (selectedBuildingInstance == null && i == 0)
                        {
                            //SelectBuildingInstance(buildingsOfType[i], iconHandler); // Välj första om ingen är vald?
                        }
                    }
                }
                // ScaleIconsToFit(); // Eventuell skalningslogik
            }
            else if (count == 1)
            {
                selectedBuildingInstance = buildingsOfType[0]; // Välj den enda automatiskt (internt)
            }
            else
            {
                selectedBuildingInstance = null;
            }
        }
        else
        {
            buildingCountPanel.gameObject.SetActive(false);
            selectedBuildingInstance = null;
        }
    }


    // --- Klickhantering (anropas av knapparnas scripts) ---

    // Anropas av BuildableItemButtonUI
    public void OnBuildableItemClicked(BuildableData itemData)
    {
        Debug.Log($"UIController received click for: {itemData.buildableName}");
        if (localPlayer == null) localPlayer = NetworkClient.localPlayer?.GetComponent<NetworkPlayer>(); // Försök igen
        if (localPlayer == null) { Debug.LogError("Local player not found!"); return; }


        if (itemData.itemType == BuildableItemType.Building)
        {
            if (buildingPlacer != null)
            {
                buildingPlacer.StartPlacement(itemData);
                // CloseBuildMenus(); // Överväg att stänga menyerna
            }
            else { Debug.LogError("BuildingPlacer reference not set!"); }
        }
        else if (itemData.itemType == BuildableItemType.Unit || itemData.itemType == BuildableItemType.Upgrade)
        {
            Building targetBuilding = GetActiveBuildingForQueue(); // Hitta den relevanta byggnaden
            if (targetBuilding != null)
            {
                // Köa enhet eller uppgradering
                // Antag att CmdQueueItem hanterar både Unit och Upgrade baserat på ID
                localPlayer.CmdQueueItem(targetBuilding.netId, itemData.buildableId, 1); // Köa 1
            }
            else
            {
                Debug.LogWarning("No building selected/available to queue the item at!");
                // Visa feedback?
            }
        }
    }

    // Anropas av BuildingIconButtonHandler vid enkelklick (när count > 1)
    public void SelectBuildingInstance(Building instance, BuildingIconButtonHandler clickedHandler)
    {
        if (selectedBuildingInstance == instance) return;

        RemoveHighlightFromBuildingIcon();
        selectedBuildingInstance = instance;
        HighlightIconButton(clickedHandler);

        Debug.Log($"UIController selected specific building instance: {instance.name}");

        // Ska BuildablesPanel uppdateras baserat på SPECIFIK instans?
        // Oftast räcker det att den baseras på KATEGORIN.
        // Om inte, anropa UpdateBuildablesPanel här igen med info från 'instance'.
    }


    // --- Hjälpfunktioner (Behöver implementeras/anpassas) ---

    private void HighlightIconButton(BuildingIconButtonHandler handlerToHighlight)
    {
        highlightedIconButton = handlerToHighlight;
        highlightedIconButton?.SetHighlightActive(true);
    }
    private void RemoveHighlightFromBuildingIcon()
    {
        highlightedIconButton?.SetHighlightActive(false);
        highlightedIconButton = null;
    }

    private Dictionary<BuildingType, List<Building>> GetCurrentBuildingData()
    {
        // TODO: Hämta data från din spelar-manager eller ResourceManager
        // Returnera en dictionary där Key är BuildingType och Value är en lista
        // av alla byggnads-objekt av den typen som den lokala spelaren äger.
        Debug.LogWarning("GetCurrentBuildingData() needs implementation!");
        return new Dictionary<BuildingType, List<Building>>();
    }

    private Building GetActiveBuildingForQueue()
    {
        // TODO: Bestäm vilken byggnad som är "aktiv" för att köa saker.
        // Är det den 'selectedBuildingInstance'? Eller alltid Townhall? Eller baserat på kategori?
        // För köande av Units/Upgrades behöver vi veta _var_ de ska köas.
        if (selectedBuildingInstance != null && CanQueueItems(selectedBuildingInstance.GetComponent<BuildingTypeComponent>()?.type ?? BuildingType.None)) // Kanske kolla om typen matchar kategorin?
        {
            return selectedBuildingInstance;
        }
        // Fallback? Försök hitta en byggnad av 'selectedCategoryType'?
        Debug.LogWarning("GetActiveBuildingForQueue() needs implementation!");
        return null;
    }

    // Kollar om en viss byggnadstyp KAN köa units/upgrades
    private bool CanQueueItems(BuildingType type)
    {
        // Exempel: Endast vissa byggnader kan ha köer
        return type == BuildingType.House || type == BuildingType.Shield; // Anpassa!
    }


    private Sprite GetSpriteForBuildingType(BuildingType type)
    {
        // TODO: Hämta rätt ikon för en byggnadstyp (inte en buildable item)
        // Detta behövs för BuildingCountPanel
        Debug.LogWarning("GetSpriteForBuildingType() needs implementation!");
        return null;
    }

    private void ScaleIconsToFit()
    {
        // TODO: Implementera skalningslogiken för BuildingCountPanel om nödvändigt
    }

    // --- Funktioner för att öppna/stänga menyer (anropas av t.ex. HammerButton) ---
    public void ToggleBuildMenu()
    {
        bool isActive = buildCategoryPanel.activeSelf; // Kolla om en av dem är aktiv
        buildCategoryPanel.SetActive(!isActive);
        buildablesPanel.SetActive(!isActive);
        buildingCountPanel.SetActive(!isActive && selectedCategoryType != BuildingType.None && /* count > 1 */ ); // Visa bara om menyn aktiveras och count > 1

        if (!isActive)
        {
            // Om menyn öppnas, välj ev. en default-kategori?
            // SelectCategory(BuildingType.House); // Exempel
        }
        else
        {
            // Om menyn stängs, rensa vald kategori?
            selectedCategoryType = BuildingType.None;
            RemoveHighlightFromBuildingIcon();
        }
    }
    public void CloseBuildMenus()
    {
        buildCategoryPanel.SetActive(false);
        buildablesPanel.SetActive(false);
        buildingCountPanel.SetActive(false);
        selectedCategoryType = BuildingType.None;
        RemoveHighlightFromBuildingIcon();
    }

}