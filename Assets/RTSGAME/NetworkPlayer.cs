// Assets/RTSGAME/Scripts/Player/NetworkPlayer.cs
using Mirror;
using UnityEngine;
using System.Collections.Generic; // För List<>
using System.Linq; // För .Select() i exempel

namespace RTSGAME
{
    public enum PlayerStatus { Playing, Defeated, Spectating }

    public class NetworkPlayer : NetworkBehaviour
    {
        [Header("Player Info")]
        [SyncVar(hook = nameof(OnPlayerNameChanged))] public string playerName = "New Player";
        [SyncVar(hook = nameof(OnTeamIDChanged))] public int teamID = 0;
        [SyncVar(hook = nameof(OnColorChanged))] public Color playerColor = Color.grey;

        [Header("Resources")]
        [SyncVar(hook = nameof(OnCreditsChanged))] public int credits = 1000; // Startvärde, sätts av servern egentligen
        [SyncVar(hook = nameof(OnManaChanged))] public int mana = 100;
        [SyncVar(hook = nameof(OnMaxManaChanged))] public int maxMana = 100;
        // OBS: Att ha resurser som SyncVars här kan vara OK för enklare fall, men för komplexa spel
        // är det ofta säkrare att ResourceManager på servern håller de auktoritativa värdena
        // och detta script bara tar emot uppdateringar för UI-visning.

        [Header("Status")]
        [SyncVar(hook = nameof(OnStatusChanged))] public PlayerStatus status = PlayerStatus.Playing;

        // Referenser till lokala system (sätts i OnStartLocalPlayer)
        private InputManager inputManager;
        private SelectionManager selectionManager;
        private UIManager uiManager;
        // private CameraController cameraController; // Om kameran styrs härifrån

        // --- Mirror Callbacks ---

        public override void OnStartServer()
        {
            base.OnStartServer();
            // Servern kan sätta initiala värden här om de inte sätts från lobbyn/GameManager
            // Ex: credits = GameSettings.startCredits;

            // Registrera spelaren hos PlayerManager på servern
            // PlayerManager.Instance?.Server_RegisterPlayer(this); // Skicka med NetworkPlayer-instansen
            Debug.Log($"Player {playerName} (NetId: {netId}) connected to server.");
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            // Avregistrera från PlayerManager
            // PlayerManager.Instance?.Server_UnregisterPlayer(netId);
            Debug.Log($"Player {playerName} (NetId: {netId}) disconnected from server.");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Alla klienter (inklusive host) registrerar spelaren lokalt för enkel åtkomst?
            // PlayerManager.Instance?.Client_RegisterPlayer(this); // Kan behöva separat lista för klienter
            Debug.Log($"Player {playerName} (NetId: {netId}, Team {teamID}) loaded on client.");
            // Uppdatera UI etc baserat på initiala SyncVar-värden via hooks
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            // Avregistrera från lokal PlayerManager-lista
            // PlayerManager.Instance?.Client_UnregisterPlayer(netId);
            Debug.Log($"Player {playerName} (NetId: {netId}) removed from client.");
            // Om den lokala spelaren lämnar, ladda meny-scenen?
            if (isLocalPlayer)
            {
                Debug.Log("Local player disconnected.");
                // Ladda meny scen...
                // NetworkManager.singleton.StopClient(); // Eller StopHost
            }
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            gameObject.name = $"LOCAL Player - {playerName} ({netId})";
            Debug.Log($"OnStartLocalPlayer: {playerName} (Team {teamID}, Color {playerColor})");

            // Hitta managers via deras Singleton Instance
            inputManager = InputManager.Instance;
            selectionManager = SelectionManager.Instance; // Kan behövas för att läsa av val senare
            uiManager = UIManager.Instance;
            // cameraController = CameraController.Instance;

            // Kontrollera att managers hittades och registrera denna NetworkPlayer
            if (inputManager != null)
            {
                inputManager.AssignLocalPlayer(this); // Ge InputManager referensen!
            }
            else { Debug.LogError("NetworkPlayer could not find InputManager Instance!"); }

            if (uiManager != null)
            {
                uiManager.SetLocalPlayer(this); // Ge UIManager referensen!
                                                // Uppdatera UI initialt (detta kan ligga kvar eller flyttas till UIManager.SetLocalPlayer)
                OnCreditsChanged(0, credits);
                OnManaChanged(0, mana);
                OnMaxManaChanged(0, maxMana);
                OnPlayerNameChanged("", playerName);
                OnTeamIDChanged(0, teamID);
                OnColorChanged(Color.clear, playerColor);
                OnStatusChanged(PlayerStatus.Spectating, status);
            }
            else { Debug.LogError("NetworkPlayer could not find UIManager Instance!"); }

            if (selectionManager == null) Debug.LogError("NetworkPlayer could not find SelectionManager Instance!");
            // SelectionManager behöver oftast ingen direkt referens TILL NetworkPlayer.
        }

        // --- Input Handling (Conceptual - Anropas från InputManager) ---

        // Denna metod anropas av InputManager när spelaren ger en bygg-order
        public void ProcessBuildRequest(int buildingTypeId, Vector3 position, Quaternion rotation)
        {
            if (!isLocalPlayer) return; // Säkerhetscheck
            Debug.Log($"Local player wants to build {buildingTypeId} at {position}");
            CmdRequestBuild(buildingTypeId, position, rotation); // Skicka till servern
        }

        // Anropas av InputManager när spelaren ger en flytt-order
        public void ProcessMoveRequest(Vector3 destination)
        {
            if (!isLocalPlayer || selectionManager == null) return;
            List<NetworkIdentity> selectedUnits = selectionManager.GetSelectedUnitsNetworkIdentities(); // Antag metod finns
            if (selectedUnits.Count > 0)
            {
                List<uint> selectedUnitNetIds = selectedUnits.Select(unit => unit.netId).ToList();
                CmdMoveUnits(selectedUnitNetIds, destination);
            }
        }

        // Anropas av InputManager när spelaren ger en attack-order
        public void ProcessAttackRequest(NetworkIdentity targetIdentity)
        {
            if (!isLocalPlayer || selectionManager == null || targetIdentity == null) return;
            List<NetworkIdentity> selectedUnits = selectionManager.GetSelectedUnitsNetworkIdentities();
            if (selectedUnits.Count > 0)
            {
                List<uint> attackerNetIds = selectedUnits.Select(unit => unit.netId).ToList();
                CmdAttackTarget(attackerNetIds, targetIdentity);
            }
        }

        // Anropas av InputManager när spelaren ger en Rally Point-order
        public void ProcessSetRallyPointRequest(Vector3 position)
        {
            if (!isLocalPlayer || selectionManager == null) return;
            // Antag att vi sätter rally point för första valda byggnaden
            GameObject firstSelected = selectionManager.GetFirstSelectedObject();
            if (firstSelected != null && firstSelected.TryGetComponent<Building>(out Building building))
            {
                if (building.TryGetComponent<NetworkIdentity>(out var buildingId))
                { // Hämta NetworkIdentity
                    CmdSetRallyPoint(buildingId, position); // Skicka NetworkIdentity
                }
            }
        }

        // Anropas av InputManager när spelaren ger en Capture-order
        public void ProcessCaptureRequest(NetworkIdentity workerIdentity, NetworkIdentity targetBuildingIdentity)
        {
            if (!isLocalPlayer || workerIdentity == null || targetBuildingIdentity == null) return;
            // Validera att spelaren äger arbetaren? Kan göras i Command.
            CmdStartCapture(workerIdentity, targetBuildingIdentity);
        }

        // Anropas av InputManager när spelaren vill köa en enhet
        public void ProcessQueueUnitRequest(NetworkIdentity buildingIdentity, int unitTypeId)
        {
            if (!isLocalPlayer || buildingIdentity == null) return;
            CmdQueueUnit(buildingIdentity, unitTypeId);
        }


        // --- Commands (Called from Client, Run on Server) ---

        [Command]
        void CmdRequestBuild(int buildingTypeId, Vector3 position, Quaternion rotation)
        {
            Debug.Log($"Server received build request for {buildingTypeId} from player {netId}");
            // TODO: Server-side logic:
            // 1. Hämta prefab baserat på buildingTypeId.
            // 2. Hämta kostnad från prefab/data.
            // 3. Kolla om spelaren (this) har råd via ResourceManager.Instance.Server_TrySpendCredits(this.netId, cost).
            // 4. Validera position.
            // 5. Om allt ok: Dra resurser (redan gjort i TrySpendCredits).
            // 6. Skapa byggnads-ghost: GameObject ghost = Instantiate(buildingPrefab, position, rotation);
            // 7. Hämta Building-scriptet.
            // 8. Initialisera på servern: buildingScript.Server_InitializeBuilding(this.netId, this.teamID, BuildingState.Placing); // Eller hämta factionID
            // 9. Spawna på nätverket: NetworkServer.Spawn(ghost, connectionToClient); // Ge ägarskap till spelaren
        }

        [Command]
        void CmdMoveUnits(List<uint> unitNetIds, Vector3 destination)
        {
            Debug.Log($"Server received move request for {unitNetIds.Count} units from player {netId}");
            foreach (uint unitNetId in unitNetIds)
            {
                if (NetworkServer.spawned.TryGetValue(unitNetId, out NetworkIdentity unitIdentity))
                {
                    // Validera ägarskap!
                    if (unitIdentity.connectionToClient == connectionToClient)
                    {
                        // TODO: Anropa rörelsekomponenten på enheten
                        // unitIdentity.GetComponent<UnitMovement>()?.Server_SetDestination(destination);
                    }
                    else { Debug.LogWarning($"Player {netId} tried to move unit {unitNetId} they don't own."); }
                }
            }
        }

        [Command]
        void CmdAttackTarget(List<uint> attackerNetIds, NetworkIdentity targetIdentity)
        {
            Debug.Log($"Server received attack request from {attackerNetIds.Count} units (Player {netId}) targeting {targetIdentity?.netId ?? 0}"); // Lägg till null-check
            if (targetIdentity == null) return; // Målet finns inte

            foreach (uint attackerNetId in attackerNetIds)
            {
                if (NetworkServer.spawned.TryGetValue(attackerNetId, out NetworkIdentity attackerIdentity))
                {
                    if (attackerIdentity.connectionToClient == connectionToClient)
                    { // Ägarkoll
                      // TODO: Anropa attack-komponenten på attackeraren
                      // attackerIdentity.GetComponent<UnitCombat>()?.Server_SetAttackTarget(targetIdentity);
                    }
                }
            }
        }

        [Command]
        void CmdSetRallyPoint(NetworkIdentity buildingIdentity, Vector3 position)
        {
            if (buildingIdentity != null && buildingIdentity.connectionToClient == connectionToClient)
            { // Ägarkoll
                Building building = buildingIdentity.GetComponent<Building>();
                if (building != null)
                {
                    building.Server_SetRallyPoint(position);
                }
            }
        }

        [Command]
        void CmdClearRallyPoint(NetworkIdentity buildingIdentity)
        {
            if (buildingIdentity != null && buildingIdentity.connectionToClient == connectionToClient)
            { // Ägarkoll
                Building building = buildingIdentity.GetComponent<Building>();
                if (building != null)
                {
                    building.Server_ClearRallyPoint();
                }
            }
        }

        [Command]
        void CmdStartCapture(NetworkIdentity workerIdentity, NetworkIdentity targetBuildingIdentity)
        {
            Debug.Log($"Server received capture request from worker {workerIdentity?.netId ?? 0} (Player {netId}) for building {targetBuildingIdentity?.netId ?? 0}"); // Null checks
            if (workerIdentity == null || targetBuildingIdentity == null) return;
            // Validera att spelaren äger arbetaren
            if (workerIdentity.connectionToClient != connectionToClient)
            {
                Debug.LogWarning($"Player {netId} sent capture command for worker they don't own.");
                return;
            }

            Building targetBuilding = targetBuildingIdentity.GetComponent<Building>();
            ConstructionWorker worker = workerIdentity.GetComponent<ConstructionWorker>();
            if (targetBuilding != null && worker != null)
            {
                // Försök starta capture på byggnaden
                bool started = targetBuilding.Server_StartCaptureAttempt(workerIdentity);
                // Meddela arbetaren (via TargetRpc?) om det lyckades/misslyckades
                if (started) worker.TargetSetCaptureState(true); else worker.TargetNotifyCaptureFailed("Failed to start capture."); // Exempel
            }
            else
            {
                if (worker != null) worker.TargetNotifyCaptureFailed("Target building invalid.");
            }
        }

        [Command]
        void CmdQueueUnit(NetworkIdentity buildingIdentity, int unitTypeId)
        {
            Debug.Log($"Server received queue unit {unitTypeId} request for building {buildingIdentity?.netId ?? 0} from player {netId}");
            if (buildingIdentity != null && buildingIdentity.connectionToClient == connectionToClient)
            { // Ägarkoll
                Building building = buildingIdentity.GetComponent<Building>();
                if (building != null)
                {
                    // TODO: Hämta byggnadens produktionskomponent
                    // ProductionComponent producer = building.GetComponent<ProductionComponent>();
                    // Hämta enhetsdata baserat på unitTypeId och byggnadens originalFactionID
                    // UnitData unitToBuild = GetUnitData(building.originalFactionID, unitTypeId);
                    // Hämta kostnad
                    // int cost = unitToBuild.cost;
                    // if (ResourceManager.Instance.Server_TrySpendCredits(this.netId, cost)) {
                    //     producer.Server_QueueUnit(unitToBuild);
                    // } else { Target_NotifyInsufficientResources("Credits"); }
                }
            }
        }

        [Command]
        void CmdSellBuilding(NetworkIdentity buildingIdentity)
        {
            Debug.Log($"Server received sell request for building {buildingIdentity?.netId ?? 0} from player {netId}");
            if (buildingIdentity != null && buildingIdentity.connectionToClient == connectionToClient)
            { // Ägarkoll
                Building building = buildingIdentity.GetComponent<Building>();
                if (building != null)
                {
                    building.Server_Sell(this.netId); // Skicka med säljarens ID
                }
            }
        }

        [Command]
        void CmdUpgradeTier(NetworkIdentity townhallIdentity)
        {
            Debug.Log($"Server received tier upgrade request for townhall {townhallIdentity?.netId ?? 0} from player {netId}");
            if (townhallIdentity != null && townhallIdentity.connectionToClient == connectionToClient)
            { // Ägarkoll
                Townhall townhall = townhallIdentity.GetComponent<Townhall>(); // Antag att Townhall är en klass
                if (townhall != null)
                {
                    // TODO: townhall.Server_AttemptTierUpgrade(this); // Skicka med spelarobjektet för resurskoll
                }
            }
        }

        // --- Server Methods (Called by Server logic) ---

        [Server]
        public void Server_AwardCredits(int amount)
        {
            // Anropas av ResourceManager efter att den har uppdaterat sitt interna värde
            // credits += amount; // Ta bort - ResourceManager sköter detta nu
            // Uppdatering sker när ResourceManager kallar Server_UpdateClientResources
        }
        [Server]
        public void Server_AwardMana(int amount)
        {
            // Anropas av ResourceManager
            // mana = Mathf.Clamp(mana + amount, 0, maxMana); // Ta bort
        }

        // Dessa behövs inte här längre om ResourceManager hanterar allt
        // [Server] public bool Server_TrySpendCredits(int amount) { ... }
        // [Server] public bool Server_TrySpendMana(int amount) { ... }
        // [Server] public void Server_SetMaxMana(int newMax) { ... }


        [Server]
        public void Server_ChangeStatus(PlayerStatus newStatus) { status = newStatus; }

        [Server]
        public void Server_SetTeam(int newTeamID) { teamID = newTeamID; }
        [Server]
        public void Server_SetColor(Color newColor) { playerColor = newColor; }
        [Server]
        public void Server_SetName(string newName) { playerName = newName; }


        // --- ClientRpc & TargetRpc Examples ---

        [ClientRpc] // Körs på alla klienter
        public void RpcAnnounceMessage(string message)
        {
            if (uiManager != null)
            {
                // uiManager.ShowNotification(message);
            }
            Debug.Log($"[ClientRpc Received] {message}");
        }

        [TargetRpc] // Körs bara på klienten som äger detta NetworkPlayer-objekt
        public void Target_NotifyInsufficientResources(string resourceName)
        {
            if (uiManager != null)
            {
                // uiManager.ShowError($"Not enough {resourceName}!");
            }
            Debug.LogWarning($"Server notification: Not enough {resourceName}.");
        }


        // --- SyncVar Hooks (Called on Clients when value changes) ---

        void OnPlayerNameChanged(string oldName, string newName)
        {
            // Uppdatera namnet i hierarkin bara om det är den lokala spelaren för tydlighet
            if (isLocalPlayer) gameObject.name = $"LOCAL Player - {newName} ({netId})";
            else gameObject.name = $"Remote Player - {newName} ({netId})";

            if (isLocalPlayer && uiManager != null)
            {
                // uiManager.UpdatePlayerName(newName);
            }
            // Uppdatera lobby UI etc. för alla spelare?
            Debug.Log($"Hook: Player {oldName} changed name to {newName}");
        }

        void OnTeamIDChanged(int oldTeamID, int newTeamID)
        {
            if (isLocalPlayer && uiManager != null)
            {
                // uiManager.UpdateTeamDisplay(newTeamID);
            }
            Debug.Log($"Hook: {playerName} changed to Team {newTeamID}");
        }

        void OnColorChanged(Color oldColor, Color newColor)
        {
            if (isLocalPlayer && uiManager != null)
            {
                // uiManager.UpdatePlayerColorSwatch(newColor);
            }
            // TODO: Uppdatera färgen på befintliga enheter/byggnader? Kan vara komplext.
            // Enklast är att enheter/byggnader sätter sin färg när de spawnar.
            Debug.Log($"Hook: {playerName} changed color to {newColor}");
        }

        void OnCreditsChanged(int oldCredits, int newCredits)
        {
            if (isLocalPlayer && uiManager != null)
            {
                uiManager.UpdateCreditsDisplay(newCredits);
                // Spela ev. ljudeffekt om man fick pengar?
                if (newCredits > oldCredits) { /* Play sound */ }
            }
            Debug.Log($"Hook: {playerName} credits changed to {newCredits}");
        }

        void OnManaChanged(int oldMana, int newMana)
        {
            if (isLocalPlayer && uiManager != null)
            {
                uiManager.UpdateManaDisplay(newMana, maxMana);
            }
            Debug.Log($"Hook: {playerName} mana changed to {newMana}");
        }

        void OnMaxManaChanged(int oldMax, int newMax)
        {
            if (isLocalPlayer && uiManager != null)
            {
                uiManager.UpdateManaDisplay(mana, newMax); // Uppdatera med nya maxvärdet
            }
            Debug.Log($"Hook: {playerName} max mana changed to {newMax}");
        }

        void OnStatusChanged(PlayerStatus oldStatus, PlayerStatus newStatus)
        {
            if (isLocalPlayer)
            {
                Debug.Log($"Hook: My status changed from {oldStatus} to: {newStatus}");
                if (newStatus == PlayerStatus.Defeated)
                {
                    // Visa "Defeated" meddelande, inaktivera input?
                    // uiManager.ShowDefeatScreen();
                    // inputManager?.DisableInput();
                }
                else if (newStatus == PlayerStatus.Playing && oldStatus != PlayerStatus.Playing)
                {
                    // Återaktivera UI/input om man gick från t.ex. Spectating
                    // inputManager?.EnableInput();
                }
            }
            // Uppdatera global spelarlista/scoreboard UI?
            Debug.Log($"Hook: {playerName} status changed to {newStatus}");
        }
    }
}