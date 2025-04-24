using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro; // Om du använder TextMeshPro

public class BuildableItemButtonUI : MonoBehaviour, IPointerClickHandler
{
    [Header("UI References")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private Image progressBarImage;
    [SerializeField] private TextMeshProUGUI queueCountText;
    [SerializeField] private Image lockedOverlayImage; // En overlay som visas om låst

    // Data för denna knapp
    private BuildableData buildableData;
    private UIController uiController; // Referens för att anropa OnClick-logik

    // Funktion som anropas av UIController när knappen skapas/uppdateras
    public void Initialize(BuildableData data, UIController controller)
    {
        buildableData = data;
        uiController = controller;

        // Sätt grundläggande utseende
        if (iconImage != null && buildableData.icon != null)
        {
            iconImage.sprite = buildableData.icon;
        }
        if (costText != null)
        {
            costText.text = $"{buildableData.creditCost}"; // Visa bara credit? Lägg till ikon?
        }

        // Dölj dynamiska element från start
        if (progressBarImage != null) progressBarImage.gameObject.SetActive(false);
        if (queueCountText != null) queueCountText.gameObject.SetActive(false);
        if (lockedOverlayImage != null) lockedOverlayImage.gameObject.SetActive(false);
    }

    // Funktion som anropas av UIController för att uppdatera knappens status
    // Kräver att UIController skickar med status för den relevanta byggnaden
    public void UpdateState(Building buildingState) // buildingState kan vara null om ingen byggnad är vald
    {
        if (buildableData == null) return;

        // 1. Låst Status (Baserat på data + ev. spelar-progression)
        bool isLocked = !IsRequirementMet(buildableData); // Din logik för att kolla krav
        GetComponent<Button>().interactable = !isLocked; // Gör knappen klickbar/ej klickbar
        if (lockedOverlayImage != null) lockedOverlayImage.gameObject.SetActive(isLocked);
        if (iconImage != null) iconImage.color = isLocked ? Color.grey : Color.white; // Gör grå om låst

        if (isLocked)
        {
            // Om låst, dölj resten och avsluta
            if (progressBarImage != null) progressBarImage.gameObject.SetActive(false);
            if (queueCountText != null) queueCountText.gameObject.SetActive(false);
            return;
        }

        // Om inte låst, kolla kö och progress (kräver att 'buildingState' är den korrekta byggnaden)
        int queueCount = 0;
        bool isBuildingNow = false;
        float progress = 0f;
        Building.BuildPauseState pauseState = Building.BuildPauseState.None; // Antag att enum finns i Building

        if (buildingState != null)
        {
            // Räkna i kön (använd synkad lista från buildingState)
            foreach (string id in buildingState.syncBuildQueueIds)
            { // Antag att Building har denna SyncList
                if (id == buildableData.buildableId)
                {
                    queueCount++;
                }
            }
            // Kolla om denna byggs just nu
            if (buildingState.syncCurrentlyBuildingId == buildableData.buildableId)
            { // Antag SyncVar finns
                isBuildingNow = true;
                progress = buildingState.syncCurrentBuildProgress; // Antag SyncVar finns
                pauseState = buildingState.syncCurrentPauseState; // Antag SyncVar finns
            }
        }

        // 2. Kö-antal
        if (queueCountText != null)
        {
            if (queueCount > 0)
            {
                queueCountText.text = queueCount.ToString();
                queueCountText.gameObject.SetActive(true);
            }
            else
            {
                queueCountText.gameObject.SetActive(false);
            }
        }

        // 3. Progress Bar
        if (progressBarImage != null)
        {
            if (isBuildingNow)
            {
                progressBarImage.gameObject.SetActive(true);
                progressBarImage.fillAmount = progress;
                // Sätt färg baserat på paus-status
                switch (pauseState)
                {
                    case Building.BuildPauseState.None: progressBarImage.color = Color.green; break; // Normal
                    case Building.BuildPauseState.Resource: progressBarImage.color = Color.yellow; break; // Resursbrist
                    case Building.BuildPauseState.Manual: progressBarImage.color = Color.blue; break; // Manuellt pausad
                }
            }
            else
            {
                progressBarImage.gameObject.SetActive(false);
            }
        }
    }

    // Hantera klick (både vänster och höger)
    public void OnPointerClick(PointerEventData eventData)
    {
        if (buildableData == null || uiController == null || !GetComponent<Button>().interactable) return; // Gör inget om ej klickbar

        if (eventData.button == PointerEventData.InputButton.Left)
        {
            // Shift+Click logik
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            int amount = shiftHeld ? buildableData.queueBatchAmount : 1;

            // Anropa UIController eller direkt NetworkPlayer command
            uiController.OnBuildableItemClicked(buildableData); // Eller mer specifik funktion
                                                                // Eller: localPlayer.CmdQueueItem(activeBuilding.netId, buildableData.buildableId, amount);

            Debug.Log($"Left Click on {buildableData.buildableName}, Amount: {amount}");

        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            // Högerklick logik för paus/avbryt
            Debug.Log($"Right Click on {buildableData.buildableName}");

            Building activeBuilding = uiController.GetActiveBuildingForQueue(); // Hämta rätt byggnad
            if (activeBuilding == null) return;

            NetworkPlayer localPlayer = NetworkClient.localPlayer?.GetComponent<NetworkPlayer>();
            if (localPlayer == null) return;

            // Kolla om denna enhet byggs aktivt
            if (activeBuilding.syncCurrentlyBuildingId == buildableData.buildableId)
            {
                localPlayer.CmdHandleRightClickBuild(activeBuilding.netId, -1); // -1 för aktiv
            }
            else
            {
                // Hitta första förekomsten av denna unitID i kön
                int index = -1;
                for (int i = 0; i < activeBuilding.syncBuildQueueIds.Count; i++)
                {
                    if (activeBuilding.syncBuildQueueIds[i] == buildableData.buildableId)
                    {
                        index = i;
                        break;
                    }
                }
                if (index != -1)
                {
                    localPlayer.CmdHandleRightClickBuild(activeBuilding.netId, index); // Skicka kö-index
                }
            }
        }
    }

    // Exempel på krav-check (behöver implementeras)
    private bool IsRequirementMet(BuildableData data)
    {
        // TODO: Kolla om data.prerequisites är uppfyllda, om data.requiresTechTier är uppnådd etc.
        // Kräver tillgång till spelardata.
        return data.isUnlockedInitially; // Temporär placeholder
    }
}