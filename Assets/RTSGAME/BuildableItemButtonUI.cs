// Filnamn: BuildableItemButtonUI.cs
// Uppdaterad för att använda UIManager och BuildPauseState från RTSGAME namespace
// Uppdaterad för att förvänta sig ProductionBuilding i UpdateState

using UnityEngine;
using UnityEngine.UI;           // För Button, Image
using UnityEngine.EventSystems; // För IPointerClickHandler
using TMPro;                    // För TextMeshProUGUI
using Mirror;                   // För NetworkClient, NetworkPlayer
using RTSGAME;                  // *** För UIManager, BuildableData, Enums etc. ***

// Lägg detta script på prefaben för dina bygg-knappar (som ligger i BuildablesPanel)
public class BuildableItemButtonUI : MonoBehaviour, IPointerClickHandler
{
    [Header("UI References")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private Image progressBarImage; // För att visa progress för aktiv produktion
    [SerializeField] private TextMeshProUGUI queueCountText; // För att visa antal i kön
    [SerializeField] private Image lockedOverlayImage; // En overlay som visas om låst/ej uppfyllda krav

    // Data för denna knapp
    private BuildableData buildableData;
    // *** ÄNDRING: Variabeltyp och namn ***
    private UIManager uiManager; // Referens för att anropa OnClick-logik

    // *** ÄNDRING: Parameter typ och namn ***
    // Funktion som anropas av UIManager när knappen skapas/uppdateras
    public void Initialize(BuildableData data, UIManager manager)
    {
        buildableData = data;
        // *** ÄNDRING: Variabel tilldelning ***
        uiManager = manager;

        // Sätt grundläggande utseende
        if (iconImage != null && buildableData.icon != null) { iconImage.sprite = buildableData.icon; }
        if (costText != null) { costText.text = $"{buildableData.creditCost}"; }

        // Dölj dynamiska element från start
        if (progressBarImage != null) progressBarImage.gameObject.SetActive(false);
        if (queueCountText != null) queueCountText.gameObject.SetActive(false);
        if (lockedOverlayImage != null) lockedOverlayImage.gameObject.SetActive(false);
    }

    // Funktion som anropas av UIManager för att uppdatera knappens status
    // Kräver att UIManager skickar med status för den relevanta *produktions*-byggnaden
    // *** ÄNDRING: Parameter typ (Building -> ProductionBuilding) ***
    public void UpdateState(ProductionBuilding activeProdBuilding) // Kan vara null om ingen prod.byggnad är aktiv/vald
    {
        if (buildableData == null) return;

        // 1. Låst Status (Baserat på data + ev. spelar-progression)
        // *** VIKTIGT: IsRequirementMet behöver implementeras korrekt! ***
        bool isLocked = !IsRequirementMet(buildableData);
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

        // Om inte låst, kolla kö och progress (kräver att 'activeProdBuilding' är den korrekta)
        int queueCount = 0;
        bool isBuildingNow = false;
        float progress = 0f;
        // *** ÄNDRING: Använder BuildPauseState från RTSGAME namespace ***
        BuildPauseState pauseState = BuildPauseState.None;

        // *** ÄNDRING: Kollar activeProdBuilding istället för buildingState ***
        if (activeProdBuilding != null && activeProdBuilding.CanQueueItems()) // Kolla om byggnaden faktiskt kan köa
        {
            // Räkna i kön (använd synkad lista från activeProdBuilding)
            foreach (string id in activeProdBuilding.syncBuildQueueIds) // Använder SyncList från ProductionBuilding
            {
                if (id == buildableData.buildableId) { queueCount++; }
            }
            // Kolla om denna byggs just nu
            if (activeProdBuilding.syncCurrentlyBuildingId == buildableData.buildableId) // Använder SyncVar från ProductionBuilding
            {
                isBuildingNow = true;
                progress = activeProdBuilding.syncCurrentBuildProgress; // Använder SyncVar från ProductionBuilding
                pauseState = activeProdBuilding.syncCurrentPauseState; // Använder SyncVar från ProductionBuilding
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
            else { queueCountText.gameObject.SetActive(false); }
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
                    // *** ÄNDRING: Använder BuildPauseState direkt ***
                    case BuildPauseState.None: progressBarImage.color = Color.green; break; // Normal
                    case BuildPauseState.Resource: progressBarImage.color = Color.yellow; break; // Resursbrist
                    case BuildPauseState.Manual: progressBarImage.color = Color.blue; break; // Manuellt pausad
                }
            }
            else { progressBarImage.gameObject.SetActive(false); }
        }
    }

    // Hantera klick (både vänster och höger)
    public void OnPointerClick(PointerEventData eventData)
    {
        // *** ÄNDRING: Kollar uiManager ***
        if (buildableData == null || uiManager == null || !GetComponent<Button>().interactable) return; // Gör inget om ej klickbar

        if (eventData.button == PointerEventData.InputButton.Left)
        {
            // Shift+Click logik
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            int amount = shiftHeld ? buildableData.queueBatchAmount : 1; // Använder amount inte direkt här

            // *** ÄNDRING: Anropar uiManager ***
            // Anropa UIManager eller direkt NetworkPlayer command
            // UIManager sköter logiken att hitta rätt byggnad och skicka command
            uiManager.OnBuildableItemClicked(buildableData);

            Debug.Log($"Left Click on {buildableData.displayName}, Amount: {amount}"); // Amount används inte direkt
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            // Högerklick logik för paus/avbryt
            Debug.Log($"Right Click on {buildableData.displayName}");

            // *** ÄNDRING: Anropar uiManager ***
            // Hämta aktiv produktionsbyggnad via UIManager
            ProductionBuilding activeBuilding = uiManager.GetActiveBuildingForQueue();
            if (activeBuilding == null) return;

            NetworkPlayer localPlayer = NetworkClient.localPlayer?.GetComponent<NetworkPlayer>();
            if (localPlayer == null) return;

            // *** VIKTIGT: Förutsätter att NetworkPlayer har CmdHandleRightClickBuild(uint buildingNetId, int queueIndex) ***

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

    // Exempel på krav-check (behöver implementeras korrekt!)
    private bool IsRequirementMet(BuildableData data)
    {
        // TODO: Kolla om data.prerequisites är uppfyllda, om data.requiresTechTier är uppnådd etc.
        // Kräver tillgång till spelardata (t.ex. via NetworkPlayer.localPlayer eller en manager).
        // Exempel: if (localPlayer.TechTier < data.requiresTechTier) return false;
        return data.isUnlockedInitially; // Temporär placeholder
    }
}