// Assets/RTSGAME/Scripts/Player/NetworkPlayer.cs
using Mirror;
using UnityEngine;
using System.Collections.Generic; // För List<>
using System.Linq; // För .Select() och andra LINQ-metoder

namespace RTSGAME
{
    public class NetworkPlayer : NetworkBehaviour
    {
        [Header("Player Info")]
        [SyncVar(hook = nameof(OnPlayerNameChanged))] public string playerName = "New Player";
        [SyncVar(hook = nameof(OnTeamIDChanged))] public int teamID = 0;
        [SyncVar(hook = nameof(OnColorChanged))] public Color playerColor = Color.grey;

        [Header("Resources (Synced from ResourceManager)")]
        [SyncVar(hook = nameof(OnCreditsChanged))] public int credits = 0;

        // SyncVars för Mana Upkeep / Power System
        [Tooltip("Total Mana genererad per sekund/tick för denna spelare.")]
        [SyncVar(hook = nameof(OnManaGenerationChanged))] public int manaGeneration;
        [Tooltip("Total Mana upkeep per sekund/tick för denna spelare.")]
        [SyncVar(hook = nameof(OnManaUpkeepChanged))] public int manaUpkeep;
        [Tooltip("Har spelaren tillräckligt med Mana Generation för sin Upkeep?")]
        [SyncVar(hook = nameof(OnPowerStatusChanged))] public bool hasSufficientPower = true; // Notera: 'a' i slutet togs bort från din kod

        [Header("Status")]
        [SyncVar(hook = nameof(OnStatusChanged))] public PlayerStatus status = PlayerStatus.Playing;

        // *** TILLAGD: Synkroniserad lista över ägda byggnader ***
        [Header("Ownership")] // Ny Header för tydlighet
        [Tooltip("NetIDs of buildings currently owned by this player. Managed by the server.")]
        public readonly SyncList<uint> ownedBuildingNetIds = new SyncList<uint>();
        // *** --------------------------------------------- ***

        // Referenser till lokala system (sätts i OnStartLocalPlayer)
        private InputManager inputManager;
        private SelectionManager selectionManager;
        private UIManager uiManager;
        private ManaBarController manaBarController; // Kan vara null

        // --- Mirror Callbacks ---

        public override void OnStartServer()
        {
            base.OnStartServer();
            // Registrering hos managers sker nu via PlayerManager/RTSNetworkManager
        }

        public override void OnStopServer()
        {
            // Avregistrering sker nu via PlayerManager/RTSNetworkManager
            base.OnStopServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Debug.Log($"Player {playerName} (NetId: {netId}, Team {teamID}) loaded on client.");
            // Initial UI-uppdatering triggas av SyncVar Hooks när värdena synkas
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            Debug.Log($"Player {playerName} (NetId: {netId}) removed from client.");
            if (isLocalPlayer)
            {
                Debug.Log("Local player disconnected.");
                // TODO: Ladda meny scen...
                // UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenuScene"); // Exempel
            }
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            gameObject.name = $"LOCAL Player - {playerName} ({netId})";
            Debug.Log($"OnStartLocalPlayer: {playerName} (Team {teamID}, Color {playerColor})");

            // Hitta managers
            inputManager = InputManager.Instance;
            selectionManager = SelectionManager.Instance;
            uiManager = UIManager.Instance;
            manaBarController = FindFirstObjectByType<ManaBarController>();

            // Tilldela spelaren till relevanta managers
            if (inputManager != null) inputManager.AssignLocalPlayer(this); else Debug.LogError("NetworkPlayer could not find InputManager Instance!");
            if (uiManager != null) uiManager.SetLocalPlayer(this); else Debug.LogError("NetworkPlayer could not find UIManager Instance!");
            if (selectionManager == null) Debug.LogError("NetworkPlayer could not find SelectionManager Instance!");
            if (manaBarController == null) Debug.LogWarning("NetworkPlayer could not find ManaBarController Instance!");

            // Tvinga en initial uppdatering av UI via hooks
            OnCreditsChanged(0, credits);
            OnManaGenerationChanged(0, manaGeneration);
            OnManaUpkeepChanged(0, manaUpkeep);
            OnPowerStatusChanged(true, hasSufficientPower);
            OnPlayerNameChanged("", playerName);
            OnTeamIDChanged(0, teamID);
            OnColorChanged(Color.clear, playerColor);
            OnStatusChanged(PlayerStatus.Spectating, status);

            // *** TILLAGD: Prenumerera på ändringar i byggnadslistan för att uppdatera UI ***
            ownedBuildingNetIds.Callback += OnOwnedBuildingsChanged;
            // Kör en initial uppdatering av UI som använder byggnadslistan
            OnOwnedBuildingsChanged(SyncList<uint>.Operation.OP_ADD, 0, 0, 0); // Simulerar en ändring för att trigga UI
            ownedBuildingNetIds.Callback += OnOwnedBuildingsChanged;

            // Anropa den centrala UI-uppdateringen direkt när spelaren startar
            // (Körs efter att uiManager-referensen är satt)
            if (uiManager != null)
            {
                // Anropa detta EFTER att uiManager.SetLocalPlayer(this) har körts om GetCurrentBuildingData behöver localPlayer
                StartCoroutine(InitialBuildingUIUpdate());
            }
            else
            {
                Debug.LogError("UIManager instance not found in OnStartLocalPlayer!");
            }
        }

        private System.Collections.IEnumerator InitialBuildingUIUpdate()
        {
            yield return null; // Vänta en frame för säkerhets skull
            if (uiManager != null)
            {
                uiManager.UpdateOwnedBuildingUI();
            }
        }

        // *** TILLAGD: Avprenumerera när spelarobjektet förstörs på klienten ***
        public override void OnStopLocalPlayer()
        {
            ownedBuildingNetIds.Callback -= OnOwnedBuildingsChanged;
            base.OnStopLocalPlayer();
        }


        // --- Commands (Called from Client via InputManager, Run on Server) ---

        #region Commands
        // --- Movement & Basic Actions ---
        [Command]
        public void CmdMoveUnits(List<uint> unitNetIds, Vector3 destination)
        {
            if (unitNetIds == null || unitNetIds.Count == 0) return;
            foreach (uint unitId in unitNetIds)
            {
                if (!NetworkServer.spawned.TryGetValue(unitId, out NetworkIdentity identity) || identity == null) { continue; }
                if (identity.connectionToClient != connectionToClient) { continue; }
                UnitMovement movement = identity.GetComponent<UnitMovement>();
                if (movement != null)
                {
                    movement.Server_SetDestination(destination);
                    ConstructionWorker worker = identity.GetComponent<ConstructionWorker>(); worker?.Cmd_GoToIdle();
                    HarvesterUnit harvester = identity.GetComponent<HarvesterUnit>(); harvester?.Cmd_GoToIdle();
                    // TODO: UnitCombat?.Server_ClearTarget();
                }
            }
        }

        [Command]
        public void CmdStopUnits(List<uint> unitNetIds)
        {
            if (unitNetIds == null || unitNetIds.Count == 0) return;
            foreach (uint unitId in unitNetIds)
            {
                if (!NetworkServer.spawned.TryGetValue(unitId, out NetworkIdentity identity) || identity == null) { continue; }
                if (identity.connectionToClient != connectionToClient) { continue; }
                UnitMovement movement = identity.GetComponent<UnitMovement>(); movement?.Server_StopMovement();
                ConstructionWorker worker = identity.GetComponent<ConstructionWorker>(); worker?.Cmd_GoToIdle();
                HarvesterUnit harvester = identity.GetComponent<HarvesterUnit>(); harvester?.Cmd_GoToIdle();
                // TODO: UnitCombat?.Server_ClearTarget();
            }
        }

        [Command]
        public void CmdAttackTarget(List<uint> attackerNetIds, NetworkIdentity targetIdentity)
        {
            if (attackerNetIds == null || attackerNetIds.Count == 0 || targetIdentity == null) return;
            Health targetHealth = targetIdentity.GetComponent<Health>();
            if (targetHealth == null || targetHealth.IsDead) return;
            ISelectable targetSelectable = targetIdentity.GetComponent<ISelectable>();
            if (targetSelectable == null) { return; }
            uint targetOwnerNetId = targetSelectable.GetOwnerNetId();
            if (PlayerManager.Instance == null || !PlayerManager.Instance.IsEnemy(this.netId, targetOwnerNetId)) { return; }

            foreach (uint unitId in attackerNetIds)
            {
                if (NetworkServer.spawned.TryGetValue(unitId, out NetworkIdentity attackerIdentity) && attackerIdentity != null)
                {
                    if (attackerIdentity.connectionToClient != connectionToClient) { continue; }
                    UnitCombat combat = attackerIdentity.GetComponent<UnitCombat>();
                    if (combat != null) { combat.Server_SetAttackTarget(targetIdentity); }
                    else { Debug.LogWarning($"[Server] Unit {unitId} cannot attack (missing UnitCombat)."); }
                }
            }
        }

        [Command]
        public void CmdAttackMoveUnits(List<uint> unitNetIds, Vector3 destination)
        {
            if (unitNetIds == null || unitNetIds.Count == 0) return;
            Debug.Log($"[Server] CmdAttackMoveUnits received for {unitNetIds.Count} units to {destination} from Player {netId}. IMPLEMENT SERVER LOGIC!");
            foreach (uint unitId in unitNetIds)
            {
                if (NetworkServer.spawned.TryGetValue(unitId, out NetworkIdentity identity))
                {
                    if (identity.connectionToClient != connectionToClient) { continue; }
                    // TODO: Implement AI logic for attack-move
                    identity.GetComponent<UnitMovement>()?.Server_SetDestination(destination); // Fallback
                    ConstructionWorker worker = identity.GetComponent<ConstructionWorker>(); worker?.Cmd_GoToIdle();
                    HarvesterUnit harvester = identity.GetComponent<HarvesterUnit>(); harvester?.Cmd_GoToIdle();
                }
            }
        }

        // --- Rally Point ---
        [Command]
        public void CmdSetRallyPoint(uint buildingNetId, Vector3 position)
        {
            if (!NetworkServer.spawned.TryGetValue(buildingNetId, out NetworkIdentity buildingIdentity)) return;
            if (buildingIdentity.connectionToClient != connectionToClient) return;
            Building building = buildingIdentity.GetComponent<Building>();
            if (building != null) { building.Server_SetRallyPoint(position); }
            else { Debug.LogWarning($"[Server] Building {buildingNetId} missing Building component for rally point."); }
        }

        [Command]
        public void CmdClearRallyPoint(uint buildingNetId)
        {
            if (!NetworkServer.spawned.TryGetValue(buildingNetId, out NetworkIdentity buildingIdentity)) return;
            if (buildingIdentity.connectionToClient != connectionToClient) return;
            Building building = buildingIdentity.GetComponent<Building>();
            building?.Server_ClearRallyPoint();
        }

        // --- Building Placement & Production Queue ---
        [Command]
        public void CmdPlaceBuilding(string buildableId, Vector3 position, Quaternion rotation)
        {
            if (RTSNetworkManager.singleton.BuildableDB == null) { Target_NotifyPlacementFailed("Server error"); return; }
            BuildableData data = RTSNetworkManager.singleton.BuildableDB.GetDataById(buildableId);
            if (data == null || data.itemType != BuildableItemType.Building) { Target_NotifyPlacementFailed("Invalid type"); return; }
            GameObject markerPrefab = data.placementMarkerPrefab;
            if (markerPrefab == null) { Target_NotifyPlacementFailed("Server error"); return; }

            bool positionValid = true; // TODO: Implement placement validation
            bool requirementsMet = true; // TODO: Implement prerequisite validation
            bool canAfford = ResourceManager.Instance.GetCurrentCredits(netId) >= data.creditCost;

            if (canAfford && positionValid && requirementsMet)
            {
                GameObject markerInstance = Instantiate(markerPrefab, position, rotation);
                NetworkServer.Spawn(markerInstance, connectionToClient);
                PlacementMarker markerScript = markerInstance.GetComponent<PlacementMarker>();
                if (markerScript != null) { markerScript.Server_InitializeMarker(netId, buildableId); }
                else { Debug.LogError("PlacementMarker prefab missing script!", markerInstance); NetworkServer.Destroy(markerInstance); Target_NotifyPlacementFailed("Server error"); }
            }
            else
            {
                if (!canAfford) Target_NotifyInsufficientResources("Credits");
                else Target_NotifyPlacementFailed("Invalid location or requirements not met");
            }
        }

        [Command]
        public void CmdQueueItem(uint buildingNetId, string buildableId, int quantity)
        {
            if (quantity <= 0) return;
            if (!NetworkServer.spawned.TryGetValue(buildingNetId, out NetworkIdentity buildingIdentity)) { return; }
            if (buildingIdentity.connectionToClient != connectionToClient) { Target_NotifyQueueFailed("Not your building"); return; }
            if (RTSNetworkManager.singleton.BuildableDB == null) { Target_NotifyQueueFailed("Server error"); return; }
            BuildableData data = RTSNetworkManager.singleton.BuildableDB.GetDataById(buildableId);
            Building building = buildingIdentity.GetComponent<Building>();
            if (building == null || data == null) { return; }
            if (data.itemType == BuildableItemType.Building) { return; }

            // TODO: Validate CanProduce & Prerequisites
            int totalCost = data.creditCost * quantity;
            if (!ResourceManager.Instance.Server_HasEnoughCredits(netId, totalCost)) { Target_NotifyInsufficientResources("Credits"); return; }

            bool queuedOk = building.Server_QueueItem(buildableId, quantity);
            if (!queuedOk) { Target_NotifyQueueFailed("Queue full or invalid item?"); }
        }

        [Command]
        public void CmdHandleRightClickBuild(uint buildingNetId, int queueIndex)
        {
            if (!NetworkServer.spawned.TryGetValue(buildingNetId, out NetworkIdentity buildingIdentity)) return;
            if (buildingIdentity.connectionToClient != connectionToClient) return;
            Building building = buildingIdentity.GetComponent<Building>();
            building?.Server_HandleRightClickOnQueue(queueIndex);
        }

        // --- Worker Commands ---
        [Command]
        public void CmdOrderWorkersToBuild(List<uint> workerNetIds, uint targetBuildingNetId)
        {
            if (workerNetIds == null || workerNetIds.Count == 0 || targetBuildingNetId == 0) return;
            if (!NetworkServer.spawned.TryGetValue(targetBuildingNetId, out NetworkIdentity targetIdentity)) { return; }
            bool isMarker = targetIdentity.TryGetComponent<PlacementMarker>(out _);
            bool isSite = targetIdentity.TryGetComponent<ConstructionSite>(out _);
            if (!isMarker && !isSite) { Debug.LogWarning($"[Server] CmdOrderWorkersToBuild: Target {targetBuildingNetId} is neither Marker nor Site."); return; }

            foreach (uint workerId in workerNetIds)
            {
                if (NetworkServer.spawned.TryGetValue(workerId, out NetworkIdentity workerIdentity))
                {
                    if (workerIdentity.connectionToClient != connectionToClient) { continue; }
                    ConstructionWorker worker = workerIdentity.GetComponent<ConstructionWorker>();
                    if (worker != null) { worker.Cmd_StartBuilding(targetIdentity); }
                }
            }
        }

        [Command]
        public void CmdOrderWorkersToCapture(List<uint> workerNetIds, uint targetBuildingNetId)
        {
            if (workerNetIds == null || workerNetIds.Count == 0 || targetBuildingNetId == 0) return;
            if (!NetworkServer.spawned.TryGetValue(targetBuildingNetId, out NetworkIdentity buildingIdentity)) { return; }
            foreach (uint workerId in workerNetIds)
            {
                if (NetworkServer.spawned.TryGetValue(workerId, out NetworkIdentity workerIdentity))
                {
                    if (workerIdentity.connectionToClient != connectionToClient) { continue; }
                    ConstructionWorker worker = workerIdentity.GetComponent<ConstructionWorker>();
                    if (worker != null) { worker.Cmd_InitiateCapture(buildingIdentity); }
                }
            }
        }

        [Command]
        public void CmdOrderWorkersToRepair(List<uint> workerNetIds, uint targetBuildingNetId)
        {
            if (workerNetIds == null || workerNetIds.Count == 0 || targetBuildingNetId == 0) return;
            if (!NetworkServer.spawned.TryGetValue(targetBuildingNetId, out NetworkIdentity buildingIdentity)) { return; }
            foreach (uint workerId in workerNetIds)
            {
                if (NetworkServer.spawned.TryGetValue(workerId, out NetworkIdentity workerIdentity))
                {
                    if (workerIdentity.connectionToClient != connectionToClient) { continue; }
                    ConstructionWorker worker = workerIdentity.GetComponent<ConstructionWorker>();
                    if (worker != null) { worker.Cmd_StartRepairing(buildingIdentity); }
                }
            }
        }

        // --- Harvester Commands ---
        [Command]
        public void CmdOrderHarvestersToHarvest(List<uint> harvesterNetIds, uint targetCrystalNetId)
        {
            if (harvesterNetIds == null || harvesterNetIds.Count == 0 || targetCrystalNetId == 0) return;
            if (!NetworkServer.spawned.TryGetValue(targetCrystalNetId, out NetworkIdentity crystalIdentity)) { return; }
            foreach (uint harvesterId in harvesterNetIds)
            {
                if (NetworkServer.spawned.TryGetValue(harvesterId, out NetworkIdentity harvesterIdentity))
                {
                    if (harvesterIdentity.connectionToClient != connectionToClient) { continue; }
                    HarvesterUnit harvester = harvesterIdentity.GetComponent<HarvesterUnit>();
                    if (harvester != null) { harvester.Cmd_OrderHarvest(crystalIdentity); }
                }
            }
        }

        // --- Building Actions ---
        [Command]
        void CmdSellBuilding(NetworkIdentity buildingIdentity)
        {
            if (buildingIdentity == null) return;
            if (buildingIdentity.connectionToClient != connectionToClient) { return; }
            Building building = buildingIdentity.GetComponent<Building>();
            // TODO: Validera om byggnad får säljas?
            building?.Server_Sell(netId);
        }

        // --- Misc Commands ---
        // [Command] void CmdUpgradeTier(NetworkIdentity townhallIdentity) { /* ... */ }
        #endregion // End Commands


        // --- Server Methods (Called by Server logic) ---
        [Server] public void Server_ChangeStatus(PlayerStatus newStatus) { status = newStatus; }
        [Server] public void Server_SetTeam(int newTeamID) { teamID = newTeamID; }
        [Server] public void Server_SetColor(Color newColor) { playerColor = newColor; }
        [Server] public void Server_SetName(string newName) { playerName = newName; }

        // *** TILLAGD: Server-metoder för att hantera ägda byggnader ***
        [Server]
        public void Server_AddOwnedBuilding(NetworkIdentity buildingIdentity)
        {
            if (buildingIdentity != null && !ownedBuildingNetIds.Contains(buildingIdentity.netId))
            {
                ownedBuildingNetIds.Add(buildingIdentity.netId);
                // Optional Debug:
                // Debug.Log($"[Server] Added building {buildingIdentity.netId} to owner {this.netId}");
            }
        }

        [Server]
        public void Server_RemoveOwnedBuilding(NetworkIdentity buildingIdentity)
        {
            if (buildingIdentity != null)
            {
                Server_RemoveOwnedBuilding(buildingIdentity.netId); // Använd overload
            }
        }
        [Server]
        public void Server_RemoveOwnedBuilding(uint buildingNetId) // Overload för bekvämlighet
        {
            if (buildingNetId != 0)
            {
                bool removed = ownedBuildingNetIds.Remove(buildingNetId);
                // Optional Debug:
                // if(removed) Debug.Log($"[Server] Removed building {buildingNetId} from owner {this.netId}");
            }
        }
        // *** ----------------------------------------------- ***


        // --- ClientRpc & TargetRpc ---
        [ClientRpc] public void RpcAnnounceMessage(string message) { UIManager.Instance?.ShowNotification(message); }
        [TargetRpc] public void Target_NotifyInsufficientResources(string resourceName) { Debug.LogWarning($"Not enough {resourceName}!"); UIManager.Instance?.ShowError($"Not enough {resourceName}!"); }
        [TargetRpc] public void Target_NotifyPlacementFailed(string reason) { Debug.LogWarning($"Placement Failed: {reason}"); UIManager.Instance?.ShowError($"Placement Failed: {reason}"); }
        [TargetRpc] public void Target_NotifyQueueFailed(string reason) { Debug.LogWarning($"Queue Failed: {reason}"); UIManager.Instance?.ShowError($"Queue Failed: {reason}"); }

        // --- SyncVar Hooks (Called on Clients) ---
        void OnPlayerNameChanged(string oldName, string newName) { if (isLocalPlayer) gameObject.name = $"LOCAL Player - {newName} ({netId})"; else gameObject.name = $"Remote Player - {newName} ({netId})"; uiManager?.UpdatePlayerList(); }
        void OnTeamIDChanged(int oldTeamID, int newTeamID) { uiManager?.UpdatePlayerList(); /* Update Scoreboard? */ }
        void OnColorChanged(Color oldColor, Color newColor) { /* Update color on UI/Minimap? */ }
        void OnCreditsChanged(int oldCredits, int newCredits) { if (isLocalPlayer) uiManager?.UpdateCreditsDisplay(newCredits); }
        void OnStatusChanged(PlayerStatus oldStatus, PlayerStatus newStatus) { if (isLocalPlayer) { UIManager.Instance?.HandlePlayerStatusChange(newStatus); } uiManager?.UpdatePlayerList(); /* Update Scoreboard? */ }
        void OnManaGenerationChanged(int oldGen, int newGen) { if (isLocalPlayer) manaBarController?.UpdateGeneration(newGen); }
        void OnManaUpkeepChanged(int oldUpkeep, int newUpkeep) { if (isLocalPlayer) manaBarController?.UpdateUpkeep(newUpkeep); }
        void OnPowerStatusChanged(bool oldStatus, bool newStatus) { if (isLocalPlayer) { manaBarController?.UpdatePowerStatus(newStatus); uiManager?.ShowPowerWarning(!newStatus); } }

        // *** TILLAGD: Callback när byggnadslistan ändras på klienten ***
        void OnOwnedBuildingsChanged(SyncList<uint>.Operation op, int itemIndex, uint oldItem, uint newItem)
        {
            if (isLocalPlayer && uiManager != null)
            {
                // Meddela UIManager att UI relaterat till byggnadslistan behöver uppdateras
                uiManager.UpdateOwnedBuildingUI();
            }
        }
        // *** ----------------------------------------------------- ***


        // --- Helper Functions ---

        // *** TILLAGD: Metod som anropas av UIManager (Klient-sida) ***
        public List<Building> GetOwnedBuildings()
        {
            List<Building> buildings = new List<Building>();
            if (!isClient) return buildings; // Bara meningsfull på klienten

            // Gå igenom de synkroniserade NetIDs
            foreach (uint netId in ownedBuildingNetIds)
            {
                // Hitta det spawnade objektet på klienten via NetID
                if (NetworkClient.spawned.TryGetValue(netId, out NetworkIdentity identity) && identity != null)
                {
                    // Försök hämta Building-komponenten
                    Building building = identity.GetComponent<Building>();
                    if (building != null)
                    {
                        buildings.Add(building);
                    }
                    // Optional: Logga varning om objektet finns men saknar Building-komponent
                    // else { Debug.LogWarning($"Object with NetId {netId} is in ownedBuildingNetIds but has no Building component."); }
                }
                // Optional: Logga varning om NetID från listan inte hittas bland spawnade objekt
                // else { Debug.LogWarning($"Owned building NetId {netId} not found in NetworkClient.spawned."); }
            }
            return buildings;
        }
        // *** ----------------------------------------------- ***

        // Denna är troligen inte nödvändig längre eftersom CmdPlaceBuilding hanterar PlacementMarker
        private GameObject GetConstructionSitePrefabFor(BuildableData data)
        {
            if (data == null) return null;
            GameObject prefab = data.constructionSitePrefab;
            if (prefab == null)
            {
                Debug.LogError($"[Server] BuildableData '{data.buildableId}' is missing the 'constructionSitePrefab' reference!");
            }
            return prefab;
        }

    } // End class NetworkPlayer
} // End namespace RTSGAME