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

        // NYTT: SyncVars för Mana Upkeep / Power System (MÅSTE matcha ResourceManager)
        [Tooltip("Total Mana genererad per sekund/tick för denna spelare.")]
        [SyncVar(hook = nameof(OnManaGenerationChanged))] public int manaGeneration;
        [Tooltip("Total Mana upkeep per sekund/tick för denna spelare.")]
        [SyncVar(hook = nameof(OnManaUpkeepChanged))] public int manaUpkeep;
        [Tooltip("Har spelaren tillräckligt med Mana Generation för sin Upkeep?")]
        [SyncVar(hook = nameof(OnPowerStatusChanged))] public bool hasSufficientPower = true;

        [Header("Status")]
        [SyncVar(hook = nameof(OnStatusChanged))] public PlayerStatus status = PlayerStatus.Playing;

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
            // Försök hitta ManaBarController (kan vara null om den inte finns i scenen)
            manaBarController = FindObjectOfType<ManaBarController>();

            // Tilldela spelaren till relevanta managers
            if (inputManager != null) inputManager.AssignLocalPlayer(this); else Debug.LogError("NetworkPlayer could not find InputManager Instance!");
            if (uiManager != null) uiManager.SetLocalPlayer(this); else Debug.LogError("NetworkPlayer could not find UIManager Instance!");
            if (selectionManager == null) Debug.LogError("NetworkPlayer could not find SelectionManager Instance!");
            // else { selectionManager.SetLocalPlayer(this); } // Om SelectionManager behöver referensen

            if (manaBarController == null) Debug.LogWarning("NetworkPlayer could not find ManaBarController Instance!");

            // Tvinga en initial uppdatering av UI via hooks, ifall värdena redan är satta innan UI hann laddas
            OnCreditsChanged(0, credits);
            OnManaGenerationChanged(0, manaGeneration);
            OnManaUpkeepChanged(0, manaUpkeep);
            OnPowerStatusChanged(true, hasSufficientPower);
            OnPlayerNameChanged("", playerName);
            OnTeamIDChanged(0, teamID);
            OnColorChanged(Color.clear, playerColor);
            OnStatusChanged(PlayerStatus.Spectating, status); // Använd ett start-state
        }

        // --- Commands (Called from Client via InputManager, Run on Server) ---

        // --- Movement & Basic Actions ---

        /// <summary>
        /// [Command] Orders specified units to move to a destination.
        /// Also attempts to cancel other jobs for specialized units (workers, harvesters).
        /// </summary>
        [Command]
        public void CmdMoveUnits(List<uint> unitNetIds, Vector3 destination)
        {
            if (unitNetIds == null || unitNetIds.Count == 0) return;
            // Debug.Log($"[Server] CmdMoveUnits received for {unitNetIds.Count} units to {destination} from Player {netId}");

            foreach (uint unitId in unitNetIds)
            {
                // Försök hitta enheten på servern
                if (!NetworkServer.spawned.TryGetValue(unitId, out NetworkIdentity identity) || identity == null)
                {
                    Debug.LogWarning($"[Server] CmdMoveUnits: Unit {unitId} not found.");
                    continue; // Gå till nästa enhet
                }

                // Validera ägarskap (Viktigt!)
                // connectionToClient är den anslutning som skickade detta Command
                if (identity.connectionToClient != connectionToClient)
                {
                    Debug.LogWarning($"[Server] Player {netId} tried to move unit {unitId} they don't own.");
                    continue; // Hoppa över denna enhet
                }

                // Försök hämta UnitMovement-komponenten
                UnitMovement movement = identity.GetComponent<UnitMovement>();
                if (movement != null)
                {
                    // Ge rörelseordern
                    movement.Server_SetDestination(destination);

                    // *** TILLÄGG: Avbryt andra pågående jobb ***
                    ConstructionWorker worker = identity.GetComponent<ConstructionWorker>();
                    if (worker != null && worker.CurrentState != WorkerState.Idle && worker.CurrentState != WorkerState.MovingToPosition)
                    {
                        worker.Cmd_GoToIdle();
                        // Debug.Log($"[Server] Worker {unitId} received move order, stopping current task.");
                    }

                    HarvesterUnit harvester = identity.GetComponent<HarvesterUnit>();
                    if (harvester != null && harvester.CurrentState != HarvesterUnit.HarvesterState.Idle && harvester.CurrentState != HarvesterUnit.HarvesterState.MovingToPosition) // Lägg till HarvesterUnit.
                    {
                        harvester.Cmd_GoToIdle();
                        // Debug.Log($"[Server] Harvester {unitId} received move order, stopping current task.");
                    }

                    // TODO: Avbryt strid? Kräver UnitCombat-komponent.
                    // UnitCombat combat = identity.GetComponent<UnitCombat>();
                    // combat?.Server_ClearTarget();

                    // TODO: Avbryt eventuella kanaliserade abilities?
                }
                else
                {
                    Debug.LogWarning($"[Server] Unit {unitId} is missing UnitMovement component.");
                }
            }
        }

        /// <summary>
        /// [Command] Orders specified units to stop their current actions (movement, work, combat).
        /// </summary>
        [Command]
        public void CmdStopUnits(List<uint> unitNetIds)
        {
            if (unitNetIds == null || unitNetIds.Count == 0) return;
            // Debug.Log($"[Server] CmdStopUnits received for {unitNetIds.Count} units from Player {netId}");

            foreach (uint unitId in unitNetIds)
            {
                if (!NetworkServer.spawned.TryGetValue(unitId, out NetworkIdentity identity) || identity == null)
                {
                    Debug.LogWarning($"[Server] CmdStopUnits: Unit {unitId} not found.");
                    continue;
                }

                // Validera ägarskap
                if (identity.connectionToClient != connectionToClient)
                {
                    Debug.LogWarning($"[Server] Player {netId} tried to stop unit {unitId} they don't own.");
                    continue;
                }

                // 1. Stoppa rörelse via UnitMovement
                UnitMovement movement = identity.GetComponent<UnitMovement>();
                movement?.Server_StopMovement();

                // 2. Sätt specialiserade enheter till Idle state via deras kommandon
                ConstructionWorker worker = identity.GetComponent<ConstructionWorker>();
                if (worker != null && worker.CurrentState != WorkerState.Idle)
                {
                    worker.Cmd_GoToIdle();
                    // Debug.Log($"[Server] Worker {unitId} received stop order, going idle.");
                }

                HarvesterUnit harvester = identity.GetComponent<HarvesterUnit>();
                if (harvester != null && harvester.CurrentState != HarvesterUnit.HarvesterState.Idle)
                {
                    harvester.Cmd_GoToIdle();
                    // Debug.Log($"[Server] Harvester {unitId} received stop order, going idle.");
                }

                // 3. Stoppa strid (om implementerat)
                // UnitCombat combat = identity.GetComponent<UnitCombat>();
                // combat?.Server_ClearTarget(); // Exempel: Säg åt stridskomponenten att sluta attackera
                // Debug.Log($"[Server] Unit {unitId} received stop order, clearing combat target.");

                // 4. Stoppa eventuella andra pågående actions/abilities
                // identity.GetComponent<Unit>()?.Server_CancelCurrentAction(); // Exempel
            }
        }

        /// <summary>
        /// [Command] Orders specified units to attack a target.
        /// </summary>
        /// <summary>
        /// [Command] Orders specified units to attack a target, performing server-side validation.
        /// </summary>
        [Command]
        public void CmdAttackTarget(List<uint> attackerNetIds, NetworkIdentity targetIdentity)
        {
            // --- Grundläggande validering av indata ---
            if (attackerNetIds == null || attackerNetIds.Count == 0 || targetIdentity == null)
            {
                Debug.LogError("[Server] CmdAttackTarget called with invalid arguments (null list or target).");
                return;
            }

            // --- Validering av målet ---
            Health targetHealth = targetIdentity.GetComponent<Health>();
            if (targetHealth == null || targetHealth.IsDead)
            {
                // Debug.Log($"[Server] CmdAttackTarget: Target {targetIdentity.netId} is invalid or dead.");
                // Ingen idé att attackera, enheterna kommer troligen stanna eller gå idle.
                // Alternativt, ge Move-order till positionen?
                // CmdMoveUnits(attackerNetIds, targetIdentity.transform.position);
                return;
            }

            // --- **NYTT: Server-Side Fiende-validering** ---
            // Först, kolla om målet KAN identifieras via ISelectable för att få ägar-ID
            ISelectable targetSelectable = targetIdentity.GetComponent<ISelectable>();
            if (targetSelectable == null)
            {
                Debug.LogError($"[Server] CmdAttackTarget: Target {targetIdentity.netId} does not implement ISelectable! Cannot determine owner/enemy status.");
                return; // Kan inte avgöra om det är en giltig attack
            }

            // Hämta ägar-ID för målet
            uint targetOwnerNetId = targetSelectable.GetOwnerNetId();

            // Anropa PlayerManager FÖR ATT AVGÖRA OM DET ÄR EN FIENDE
            // (Denna metod måste finnas i PlayerManager.cs och fungera på servern)
            if (PlayerManager.Instance == null || !PlayerManager.Instance.IsEnemy(this.netId, targetOwnerNetId))
            {
                // Om PlayerManager saknas ELLER IsEnemy returnerar false (dvs. målet är inte en fiende)
                Debug.LogWarning($"[Server] Player {netId} tried to attack non-enemy target {targetIdentity.netId} (Owner: {targetOwnerNetId}). Command ignored.");
                // Ignorera attack-kommandot. Kanske ge en Move-order istället?
                // CmdMoveUnits(attackerNetIds, targetIdentity.transform.position);
                return; // Avbryt kommandot
            }
            // --- SLUT PÅ Fiende-validering ---


            // --- Ge Attack-Order till varje anfallare ---
            int attackersOrdered = 0;
            foreach (uint unitId in attackerNetIds)
            {
                if (NetworkServer.spawned.TryGetValue(unitId, out NetworkIdentity attackerIdentity) && attackerIdentity != null)
                {
                    // Validera ägarskap för anfallaren
                    if (attackerIdentity.connectionToClient != connectionToClient)
                    {
                        Debug.LogWarning($"[Server] Player {netId} tried to order attack from unit {unitId} they don't own.");
                        continue; // Hoppa över denna enhet
                    }

                    // Försök hitta stridskomponent (Antagande: UnitCombat)
                    UnitCombat combat = attackerIdentity.GetComponent<UnitCombat>(); // **Antagande**
                    if (combat != null)
                    {
                        // Ge ordern till stridskomponenten
                        combat.Server_SetAttackTarget(targetIdentity); // **Antagande**
                        attackersOrdered++;

                        // Avbryt andra jobb för specialenheter? Nej, låt UnitCombat hantera det.
                        // Om en worker får attackorder ska dess AI/Combat avgöra om den ska sluta bygga.
                    }
                    else
                    {
                        Debug.LogWarning($"[Server] Unit {unitId} is missing UnitCombat component and cannot attack.");
                        // Ska enheten flytta dit istället? Eller bara ignoreras?
                        // attackerIdentity.GetComponent<UnitMovement>()?.Server_SetDestination(targetIdentity.transform.position);
                    }
                }
                else
                {
                    Debug.LogWarning($"[Server] CmdAttackTarget: Attacker Unit {unitId} not found.");
                }
            }
            // Debug.Log($"[Server] CmdAttackTarget: Ordered {attackersOrdered}/{attackerNetIds.Count} units to attack target {targetIdentity.netId}");
        } // Slut på CmdAttackTarget

        /// <summary>
        /// [Command] Orders specified units to perform an Attack-Move towards a destination.
        /// </summary>
        [Command]
        public void CmdAttackMoveUnits(List<uint> unitNetIds, Vector3 destination)
        {
            if (unitNetIds == null || unitNetIds.Count == 0) return;
            Debug.Log($"[Server] CmdAttackMoveUnits received for {unitNetIds.Count} units to {destination} from Player {netId}. IMPLEMENT SERVER LOGIC!");

            foreach (uint unitId in unitNetIds)
            {
                if (NetworkServer.spawned.TryGetValue(unitId, out NetworkIdentity identity))
                {
                    // Validera ägarskap
                    if (identity.connectionToClient != connectionToClient) { continue; }

                    // *** TODO: IMPLEMENTERA SERVERLOGIK FÖR ATTACK-MOVE ***
                    // Detta kräver troligen en AI-komponent på enheten.
                    // Exempel:
                    // UnitAI ai = identity.GetComponent<UnitAI>();
                    // if (ai != null)
                    // {
                    //     ai.Server_OrderAttackMove(destination);
                    // }
                    // else { Debug.LogWarning($"[Server] Unit {unitId} cannot Attack-Move (missing AI?). Moving normally.");
                    //        identity.GetComponent<UnitMovement>()?.Server_SetDestination(destination); }

                    // Tillfällig fallback: Flytta bara enheterna
                    identity.GetComponent<UnitMovement>()?.Server_SetDestination(destination);

                    // Se till att specialiserade enheter avbryter jobb
                    ConstructionWorker worker = identity.GetComponent<ConstructionWorker>(); worker?.Cmd_GoToIdle();
                    HarvesterUnit harvester = identity.GetComponent<HarvesterUnit>(); harvester?.Cmd_GoToIdle();
                }
            }
        }


        // --- Rally Point ---

        /// <summary>
        /// [Command] Sets the rally point for a specific building owned by the player.
        /// </summary>
        [Command]
        public void CmdSetRallyPoint(uint buildingNetId, Vector3 position)
        {
            if (!NetworkServer.spawned.TryGetValue(buildingNetId, out NetworkIdentity buildingIdentity))
            {
                Debug.LogWarning($"[Server] CmdSetRallyPoint: Building {buildingNetId} not found.");
                return;
            }

            // Validera ägarskap till byggnaden
            if (buildingIdentity.connectionToClient != connectionToClient)
            {
                Debug.LogWarning($"[Server] Player {netId} tried to set rally point for building {buildingNetId} they don't own.");
                return;
            }

            Building building = buildingIdentity.GetComponent<Building>();
            if (building != null)
            {
                // TODO: Kolla om byggnaden KAN ha ett rally point (t.ex. produktionsbyggnader)
                // ProductionStructure prod = building as ProductionStructure; // Exempel på check
                // if (prod != null) {
                building.Server_SetRallyPoint(position);
                // Debug.Log($"[Server] Rally point set for building {buildingNetId} at {position}");
                // } else { Debug.LogWarning($"[Server] Building {buildingNetId} cannot have a rally point."); }
            }
            else { Debug.LogWarning($"[Server] Building {buildingNetId} is missing Building component."); }
        }

        /// <summary>
        /// [Command] Clears the rally point for a specific building owned by the player.
        /// </summary>
        [Command]
        public void CmdClearRallyPoint(uint buildingNetId)
        {
            if (!NetworkServer.spawned.TryGetValue(buildingNetId, out NetworkIdentity buildingIdentity)) return;
            if (buildingIdentity.connectionToClient != connectionToClient) return; // Ägarskapskoll
            Building building = buildingIdentity.GetComponent<Building>();
            // TODO: Kolla om den kan ha rally point?
            building?.Server_ClearRallyPoint();
        }


        // --- Building Placement & Production Queue ---

        /// <summary>
        /// [Command] Requests to place a construction site for a specific buildable item.
        /// </summary>
        [Command]
        public void CmdPlaceBuilding(string buildableId, Vector3 position, Quaternion rotation)
        {
            Debug.Log($"[Server] CmdPlaceBuilding received for {buildableId} from player {netId}");
            if (ResourceManager.Instance == null) { Debug.LogError("[Server] ResourceManager missing!"); return; }

            if (RTSNetworkManager.singleton.BuildableDB == null) // Kolla om databasen är kopplad
            {
                Debug.LogError("[Server] BuildableDatabase reference missing in RTSNetworkManager!");
                Target_NotifyPlacementFailed("Internal server error (database missing)");
                return;
            }
            if (RTSNetworkManager.singleton.BuildableDB == null) // Kolla om databasen är kopplad
            {
                Debug.LogError("[Server] CmdQueueItem: BuildableDatabase reference missing in RTSNetworkManager!");
                Target_NotifyQueueFailed("Internal server error (database missing)"); // Använd rätt TargetRpc
                return;
            }
            BuildableData data = RTSNetworkManager.singleton.BuildableDB.GetDataById(buildableId); // Använd databasens metod!

            // TODO: Implementera validering av position och prerequisites
            bool positionValid = true; // placeholder
            bool requirementsMet = true; // placeholder
                                         // Kolla endast kostnad, spendera inte än
            bool canAfford = ResourceManager.Instance.GetCurrentCredits(netId) >= data.creditCost;

            if (canAfford && positionValid && requirementsMet)
            {
                GameObject sitePrefab = GetConstructionSitePrefabFor(data); // Kräver implementation
                if (sitePrefab != null)
                {
                    GameObject siteInstance = Instantiate(sitePrefab, position, rotation);
                    NetworkServer.Spawn(siteInstance, connectionToClient); // Ge ägarskap till spelaren
                    ConstructionSite siteScript = siteInstance.GetComponent<ConstructionSite>(); // **Antagande: ConstructionSite script finns**
                    if (siteScript != null)
                    {
                        siteScript.InitializeSite(netId, data); // **Antagande: InitializeSite metod finns**
                        Debug.Log($"[Server] Spawned ConstructionSite for {data.buildableName} for player {netId}.");
                    }
                    else { Debug.LogError("[Server] Spawned ConstructionSite is missing ConstructionSite script!"); NetworkServer.Destroy(siteInstance); Target_NotifyPlacementFailed("Internal server error (script missing)"); }
                }
                else
                {
                    Debug.LogError($"[Server] Could not find ConstructionSite prefab for {data.buildableName}");
                    Target_NotifyPlacementFailed("Internal server error (prefab missing)");
                }
            }
            else
            {
                Debug.LogWarning($"[Server] Placement failed for {data.buildableName}. Afford: {canAfford}, ValidPos: {positionValid}, ReqsMet: {requirementsMet}");
                if (!canAfford) Target_NotifyInsufficientResources("Credits");
                else Target_NotifyPlacementFailed("Invalid location or requirements not met");
            }
        }

        /// <summary>
        /// [Command] Requests to queue a unit or upgrade at a specific building.
        /// </summary>
        [Command]
        public void CmdQueueItem(uint buildingNetId, string buildableId, int quantity)
        {
            if (quantity <= 0) return;
            if (!NetworkServer.spawned.TryGetValue(buildingNetId, out NetworkIdentity buildingIdentity)) { Debug.LogWarning($"[Server] CmdQueueItem: Building {buildingNetId} not found."); return; }
            if (buildingIdentity.connectionToClient != connectionToClient) { Target_NotifyQueueFailed("Not your building"); return; } // Ägarskap

            Building building = buildingIdentity.GetComponent<Building>();
            //BuildableData data = ResourceManager.Instance?.GetBuildableDataById(buildableId);
            if (RTSNetworkManager.singleton.BuildableDB == null) // Kolla om databasen är kopplad
            {
                Debug.LogError("[Server] CmdQueueItem: BuildableDatabase reference missing in RTSNetworkManager!");
                Target_NotifyQueueFailed("Internal server error (database missing)");
                return;
            }
            BuildableData data = RTSNetworkManager.singleton.BuildableDB.GetDataById(buildableId); // Använd databasens metod!
            if (building == null || data == null) { Debug.LogWarning("[Server] CmdQueueItem: Building or BuildableData not found."); return; }
            if (data.itemType == RTSGAME.BuildableItemType.Building)
            {
                Debug.LogWarning("[Server] CmdQueueItem: Cannot queue a building.");
                return;
            } // Kan inte köa byggnader

            // TODO: Validera om byggnaden KAN producera/forska detta (t.ex. building.CanProduce(buildableId))
            // TODO: Validera om spelaren uppfyller prerequisites

            int totalCost = data.creditCost * quantity;
            if (!ResourceManager.Instance.Server_HasEnoughCredits(netId, totalCost)) { Target_NotifyInsufficientResources("Credits"); return; }

            // Antag att Building har metoden för att köa
            bool queuedOk = building.Server_QueueItem(buildableId, quantity); // **Antagande: Server_QueueItem finns**

            if (!queuedOk) { Target_NotifyQueueFailed("Queue full or invalid item?"); }
            // else { Debug.Log($"[Server] Player {netId} queued {quantity} of {data.buildableName} at building {buildingNetId}."); }
        }



        /// <summary>
        /// [Command] Handles right-click actions on items in a building's queue (e.g., cancel).
        /// </summary>
        [Command]
        public void CmdHandleRightClickBuild(uint buildingNetId, int queueIndex)
        {
            if (!NetworkServer.spawned.TryGetValue(buildingNetId, out NetworkIdentity buildingIdentity)) return;
            if (buildingIdentity.connectionToClient != connectionToClient) return; // Ägarskap
            Building building = buildingIdentity.GetComponent<Building>();
            building?.Server_HandleRightClickOnQueue(queueIndex); // **Antagande: Server_HandleRightClickOnQueue finns**
        }


        // --- Worker Commands ---

        /// <summary>
        /// [Command] Orders specified workers to construct a target building site.
        /// </summary>
        [Command]
        public void CmdOrderWorkersToBuild(List<uint> workerNetIds, uint targetBuildingNetId)
        {
            if (workerNetIds == null || workerNetIds.Count == 0 || targetBuildingNetId == 0) return;
            if (!NetworkServer.spawned.TryGetValue(targetBuildingNetId, out NetworkIdentity buildingIdentity)) { Debug.LogWarning($"[Server] CmdOrderWorkersToBuild: Target building {targetBuildingNetId} not found."); return; }
            Building targetBuilding = buildingIdentity.GetComponent<Building>();
            if (targetBuilding == null || !targetBuilding.NeedsConstruction) { Debug.LogWarning($"[Server] CmdOrderWorkersToBuild: Target {targetBuildingNetId} is not a valid construction site."); return; }
            // TODO: Validera att byggnaden ägs av spelaren? (Man bygger väl bara egna?)

            int workersSent = 0;
            foreach (uint workerId in workerNetIds)
            {
                if (NetworkServer.spawned.TryGetValue(workerId, out NetworkIdentity workerIdentity))
                {
                    if (workerIdentity.connectionToClient != connectionToClient) { Debug.LogWarning($"[Server] Player {netId} tried to order worker {workerId} they don't own."); continue; } // Ägarskap
                    ConstructionWorker worker = workerIdentity.GetComponent<ConstructionWorker>();
                    if (worker != null)
                    {
                        worker.Cmd_StartBuilding(buildingIdentity); // Worker sköter resten (flytta + bygg)
                        workersSent++;
                    }
                    else { Debug.LogWarning($"[Server] Unit {workerId} is not a ConstructionWorker."); }
                }
                else { Debug.LogWarning($"[Server] CmdOrderWorkersToBuild: Worker {workerId} not found."); }
            }
            // Debug.Log($"[Server] Sent {workersSent} workers to build {targetBuildingNetId}");
        }

        /// <summary>
        /// [Command] Orders specified workers to capture a target building.
        /// </summary>
        [Command]
        public void CmdOrderWorkersToCapture(List<uint> workerNetIds, uint targetBuildingNetId)
        {
            if (workerNetIds == null || workerNetIds.Count == 0 || targetBuildingNetId == 0) return;
            if (!NetworkServer.spawned.TryGetValue(targetBuildingNetId, out NetworkIdentity buildingIdentity)) { Debug.LogWarning($"[Server] CmdOrderWorkersToCapture: Target building {targetBuildingNetId} not found."); return; }
            Building targetBuilding = buildingIdentity.GetComponent<Building>();
            if (targetBuilding == null || targetBuilding.IsDead || targetBuilding.IsBeingCaptured) { Debug.LogWarning($"[Server] CmdOrderWorkersToCapture: Target {targetBuildingNetId} is not capturable right now."); return; }
            // Validera att målet INTE ägs av spelaren
            if (targetBuilding.OwnerNetId == this.netId) { Debug.LogWarning($"[Server] Player {netId} cannot capture their own building {targetBuildingNetId}."); return; }
            // TODO: Validera att målet är neutralt eller fiende (via PlayerManager?)

            int workersSent = 0;
            foreach (uint workerId in workerNetIds)
            {
                if (NetworkServer.spawned.TryGetValue(workerId, out NetworkIdentity workerIdentity))
                {
                    if (workerIdentity.connectionToClient != connectionToClient) { continue; } // Ägarskap
                    ConstructionWorker worker = workerIdentity.GetComponent<ConstructionWorker>();
                    if (worker != null)
                    {
                        worker.Cmd_InitiateCapture(buildingIdentity); // Worker sköter resten
                        workersSent++;
                    }
                    else { Debug.LogWarning($"[Server] Unit {workerId} is not a ConstructionWorker."); }
                }
                else { Debug.LogWarning($"[Server] CmdOrderWorkersToCapture: Worker {workerId} not found."); }
            }
            // Debug.Log($"[Server] Sent {workersSent} workers to capture {targetBuildingNetId}");
        }

        /// <summary>
        /// [Command] Orders specified workers to repair a target building.
        /// </summary>
        [Command]
        public void CmdOrderWorkersToRepair(List<uint> workerNetIds, uint targetBuildingNetId)
        {
            if (workerNetIds == null || workerNetIds.Count == 0 || targetBuildingNetId == 0) return;
            if (!NetworkServer.spawned.TryGetValue(targetBuildingNetId, out NetworkIdentity buildingIdentity)) { Debug.LogWarning($"[Server] CmdOrderWorkersToRepair: Target building {targetBuildingNetId} not found."); return; }
            Building targetBuilding = buildingIdentity.GetComponent<Building>();
            if (targetBuilding == null || targetBuilding.IsDead || targetBuilding.healthComponent == null || targetBuilding.healthComponent.CurrentHealth >= targetBuilding.healthComponent.MaxHealth) { Debug.LogWarning($"[Server] CmdOrderWorkersToRepair: Target {targetBuildingNetId} does not need repair."); return; }
            // Validera att byggnaden ägs av spelaren eller är allierad?
            if (targetBuilding.OwnerNetId != this.netId) { Debug.LogWarning($"[Server] Player {netId} cannot repair building {targetBuildingNetId} they don't own."); return; } // Tillåt bara egna?

            int workersSent = 0;
            foreach (uint workerId in workerNetIds)
            {
                if (NetworkServer.spawned.TryGetValue(workerId, out NetworkIdentity workerIdentity))
                {
                    if (workerIdentity.connectionToClient != connectionToClient) { continue; } // Ägarskap
                    ConstructionWorker worker = workerIdentity.GetComponent<ConstructionWorker>();
                    if (worker != null)
                    {
                        worker.Cmd_StartRepairing(buildingIdentity); // Worker sköter resten
                        workersSent++;
                    }
                    else { Debug.LogWarning($"[Server] Unit {workerId} is not a ConstructionWorker."); }
                }
                else { Debug.LogWarning($"[Server] CmdOrderWorkersToRepair: Worker {workerId} not found."); }
            }
            // Debug.Log($"[Server] Sent {workersSent} workers to repair {targetBuildingNetId}");
        }

        // --- Harvester Commands ---

        /// <summary>
        /// [Command] Orders specified harvesters to harvest a target crystal.
        /// </summary>
        [Command]
        public void CmdOrderHarvestersToHarvest(List<uint> harvesterNetIds, uint targetCrystalNetId)
        {
            if (harvesterNetIds == null || harvesterNetIds.Count == 0 || targetCrystalNetId == 0) return;
            if (!NetworkServer.spawned.TryGetValue(targetCrystalNetId, out NetworkIdentity crystalIdentity)) { Debug.LogWarning($"[Server] CmdOrderHarvestersToHarvest: Target crystal {targetCrystalNetId} not found."); return; }
            HarvestableCrystal targetCrystal = crystalIdentity.GetComponent<HarvestableCrystal>();
            if (targetCrystal == null) { Debug.LogWarning($"[Server] CmdOrderHarvestersToHarvest: Target {targetCrystalNetId} is not a HarvestableCrystal."); return; }
            // TODO: Kolla om kristallen redan är upptagen av NÅGON ANNAN än de som nu beordras? Kan bli komplext. Låt Harvester hantera reservationen.

            int harvestersSent = 0;
            foreach (uint harvesterId in harvesterNetIds)
            {
                if (NetworkServer.spawned.TryGetValue(harvesterId, out NetworkIdentity harvesterIdentity))
                {
                    if (harvesterIdentity.connectionToClient != connectionToClient) { continue; } // Ägarskap
                    HarvesterUnit harvester = harvesterIdentity.GetComponent<HarvesterUnit>();
                    if (harvester != null)
                    {
                        harvester.Cmd_OrderHarvest(crystalIdentity); // Harvester sköter resten
                        harvestersSent++;
                    }
                    else { Debug.LogWarning($"[Server] Unit {harvesterId} is not a HarvesterUnit."); }
                }
                else { Debug.LogWarning($"[Server] CmdOrderHarvestersToHarvest: Harvester {harvesterId} not found."); }
            }
            // Debug.Log($"[Server] Sent {harvestersSent} harvesters to harvest {targetCrystalNetId}");
        }


        // --- Building Actions ---

        /// <summary>
        /// [Command] Sells a building owned by the player.
        /// </summary>
        [Command]
        void CmdSellBuilding(NetworkIdentity buildingIdentity)
        {
            if (buildingIdentity == null) return;
            // Validera ägarskap (görs även i Server_Sell)
            if (buildingIdentity.connectionToClient != connectionToClient)
            {
                Debug.LogWarning($"[Server] Player {netId} tried to sell building {buildingIdentity.netId} they don't own.");
                return;
            }
            Building building = buildingIdentity.GetComponent<Building>();
            // TODO: Kolla om byggnaden FÅR säljas (inte under attack? inte Townhall?)
            building?.Server_Sell(netId); // Skicka med spelarens ID för säkerhetskoll?
        }

        // --- Misc Commands ---

        // [Command] void CmdUpgradeTier(NetworkIdentity townhallIdentity) { /* ... anropa Townhall-script ... */ }


        // --- Server Methods (Called by Server logic) ---
        [Server] public void Server_ChangeStatus(PlayerStatus newStatus) { status = newStatus; }
        [Server] public void Server_SetTeam(int newTeamID) { teamID = newTeamID; }
        [Server] public void Server_SetColor(Color newColor) { playerColor = newColor; }
        [Server] public void Server_SetName(string newName) { playerName = newName; }

        // --- ClientRpc & TargetRpc ---
        [ClientRpc] public void RpcAnnounceMessage(string message) { /* TODO: Visa i UI */ UIManager.Instance?.ShowNotification(message); }
        [TargetRpc] public void Target_NotifyInsufficientResources(string resourceName) { /* TODO: Visa i UI */ Debug.LogWarning($"Not enough {resourceName}!"); UIManager.Instance?.ShowError($"Not enough {resourceName}!"); }
        [TargetRpc] public void Target_NotifyPlacementFailed(string reason) { /* TODO: Visa i UI */ Debug.LogWarning($"Placement Failed: {reason}"); UIManager.Instance?.ShowError($"Placement Failed: {reason}"); }
        [TargetRpc] public void Target_NotifyQueueFailed(string reason) { /* TODO: Visa i UI */ Debug.LogWarning($"Queue Failed: {reason}"); UIManager.Instance?.ShowError($"Queue Failed: {reason}"); }

        // --- SyncVar Hooks (Called on Clients) ---
        void OnPlayerNameChanged(string oldName, string newName) { if (isLocalPlayer) gameObject.name = $"LOCAL Player - {newName} ({netId})"; else gameObject.name = $"Remote Player - {newName} ({netId})"; uiManager?.UpdatePlayerList(); }
        void OnTeamIDChanged(int oldTeamID, int newTeamID) { uiManager?.UpdatePlayerList(); /* Uppdatera Scoreboard? */ }
        void OnColorChanged(Color oldColor, Color newColor) { /* TODO: Uppdatera färg på UI/MiniMap? */ }
        void OnCreditsChanged(int oldCredits, int newCredits) { if (isLocalPlayer) uiManager?.UpdateCreditsDisplay(newCredits); }
        void OnStatusChanged(PlayerStatus oldStatus, PlayerStatus newStatus) { if (isLocalPlayer) { /* Hantera Defeat/Victory etc. */ UIManager.Instance?.HandlePlayerStatusChange(newStatus); } uiManager?.UpdatePlayerList(); /* Uppdatera Scoreboard? */ }

        void OnManaGenerationChanged(int oldGen, int newGen) { if (isLocalPlayer) manaBarController?.UpdateGeneration(newGen); }
        void OnManaUpkeepChanged(int oldUpkeep, int newUpkeep) { if (isLocalPlayer) manaBarController?.UpdateUpkeep(newUpkeep); }
        void OnPowerStatusChanged(bool oldStatus, bool newStatus) { if (isLocalPlayer) { manaBarController?.UpdatePowerStatus(newStatus); uiManager?.ShowPowerWarning(!newStatus); } }


        // --- Helper Functions ---
        private GameObject GetConstructionSitePrefabFor(BuildableData data) // Tar nu data som parameter
        {
            if (data == null) return null;

            // *** VIKTIGT: ANTAGANDE OM FÄLTNAMN ***
            // Antag att din BuildableData-klass (och dess subklasser för byggnader)
            // har ett fält eller property som heter 'constructionSitePrefab'
            // som håller prefaben för byggarbetsplatsen.
            // Ändra 'data.constructionSitePrefab' till ditt faktiska fältnamn!

            GameObject prefab = data.constructionSitePrefab; // Exempel på fältnamn

            if (prefab == null)
            {
                Debug.LogError($"[Server] BuildableData '{data.buildableId}' is missing the 'constructionSitePrefab' reference!");
            }
            return prefab;
        }

    } // End class NetworkPlayer
} // End namespace RTSGAME