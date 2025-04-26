// Assets/RTSGAME/Scripts/Units/ConstructionWorker.cs
using Mirror;
using UnityEngine;
using System.Collections; // För Coroutine

namespace RTSGAME
{
    // WorkerState enum ligger nu i Enums.cs

    [RequireComponent(typeof(UnitMovement))]
    public class ConstructionWorker : Unit // Ärver från Unit
    {
        [Header("Worker Specifics")]
        [Tooltip("How close the worker needs to be to interact with a building/marker.")]
        [SerializeField] private float interactionRange = 5f;
        [Tooltip("Amount of 'health' repaired per second.")]
        [SerializeField] private float repairAmountPerSecond = 20f;
        [Tooltip("Amount of 'progress' added per second when constructing.")]
        [SerializeField] private float constructionWorkPerSecond = 1f;

        // --- State Machine & Target ---
        [SyncVar(hook = nameof(OnStateChangedHook))]
        private WorkerState currentState = WorkerState.Idle;
        public WorkerState CurrentState => currentState; // Property finns redan

        [SyncVar]
        private uint currentTargetNetId = 0; // Kan vara Building, PlacementMarker etc.
        private Component currentTargetCache = null; // Mer generell cache nu

        private Coroutine server_workCoroutine = null;

        // --- Unity & Mirror Callbacks ---

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Uppdatera cachen om vi har ett mål vid start
            if (currentTargetNetId != 0) { StartCoroutine(FindTargetLocally(currentTargetNetId)); }
            OnStateChangedHook(currentState, currentState); // Tvinga hook-anrop
        }

        // --- State Change Hook (Client-side) ---
        void OnStateChangedHook(WorkerState oldState, WorkerState newState)
        {
            // Debug.Log($"Worker {netId} state changed on client: {oldState} -> {newState}");
            if (newState == WorkerState.Idle) { currentTargetCache = null; }
            UpdateAnimationWorker(oldState, newState);
        }

        // --- Animation Update (Client-side) ---
        protected virtual void UpdateAnimationWorker(WorkerState oldState, WorkerState newState)
        {
            if (animator == null) return;
            bool isMoving = (newState == WorkerState.MovingToBuild || newState == WorkerState.MovingToCapture || newState == WorkerState.MovingToRepair || newState == WorkerState.MovingToPosition);
            animator.SetBool("IsMoving", isMoving);
            animator.SetBool("IsBuilding", newState == WorkerState.Building); // Används när den bygger på ConstructionSite
            animator.SetBool("IsRepairing", newState == WorkerState.Repairing);
            animator.SetBool("IsCapturing", newState == WorkerState.Capturing);
        }


        // --- Server-Side State Logic ---
        [ServerCallback]
        void Update()
        {
            // Servern övervakar state och avstånd
            // Uppdatera cachen om den är null men vi har ett target ID
            if (currentTargetCache == null && currentTargetNetId != 0)
            {
                if (NetworkServer.spawned.TryGetValue(currentTargetNetId, out var id)) { currentTargetCache = id?.GetComponent<Component>(); } // Hämta bas Component först
                if (currentTargetCache == null) { currentTargetNetId = 0; } // Målet försvann?
            }

            // Behåll logiken för att avbryta om för långt ifrån vid Bygg/Reparation
            if (currentState == WorkerState.Building || currentState == WorkerState.Repairing)
            {
                Building targetBuilding = currentTargetCache as Building; // Försök casta till Building
                if (targetBuilding == null || targetBuilding.IsDead)
                {
                    // Om vi var i Building state men målet försvann (kanske blev klart?)
                    // Gå Idle. Om vi var i Repairing och målet försvann, gå Idle.
                    Server_TransitionToState(WorkerState.Idle, 0);
                    return;
                }
                float distance = Vector3.Distance(transform.position, targetBuilding.transform.position);
                float maxRange = interactionRange * 1.2f; // Lite marginal
                if (distance > maxRange)
                {
                    Debug.Log($"[Server] Worker {netId} too far from target {currentTargetNetId} while {currentState}. Going Idle.");
                    if (currentState == WorkerState.Building) targetBuilding.Server_RemoveBuilder(this.netId);
                    Server_TransitionToState(WorkerState.Idle, 0);
                }
            }
            // Ingen specifik övervakning för Capturing här, det sköts av Building/CaptureTimer
        }


        // --- Commands (Called from Client via NetworkPlayer, Run on Server) ---

        [Command]
        public void Cmd_MoveToPosition(Vector3 destination)
        {
            if (!IsOwner(connectionToClient)) { return; }
            Server_TransitionToState(WorkerState.MovingToPosition, 0);
            movementComponent?.Server_SetDestination(destination);
        }

        // ÄNDRAD: Tar nu emot NetworkIdentity för PlacementMarker
        [Command]
        public void Cmd_StartBuilding(NetworkIdentity markerOrSiteIdentity)
        {
            if (!IsOwner(connectionToClient)) return;
            if (markerOrSiteIdentity == null) return;

            // Kolla FÖRST om det är en PlacementMarker
            PlacementMarker marker = markerOrSiteIdentity.GetComponent<PlacementMarker>();
            if (marker != null)
            {
                // TODO: Validera om markören ägs av spelaren?
                if (marker.OwnerNetId != this.ownerNetId) { Debug.LogWarning($"Worker {netId} trying to build marker {marker.netId} owned by someone else."); return; }

                Server_TransitionToState(WorkerState.MovingToBuild, markerOrSiteIdentity.netId);
                movementComponent?.Server_SetDestination(marker.transform.position);
                Debug.Log($"[Server] Worker {netId} ordered to move to PlacementMarker {marker.netId}.");
                return; // Klart för marker
            }

            // OM det INTE var en marker, kolla om det är en ConstructionSite (för retargeting)
            ConstructionSite site = markerOrSiteIdentity.GetComponent<ConstructionSite>();
            if (site != null)
            {
                // TODO: Validera ägarskap? Behövs det här? Byggnaden ska redan vara ägd.
                if (site.OwnerNetId != this.ownerNetId) { Debug.LogWarning($"Worker {netId} trying to build site {site.netId} owned by someone else."); return; }
                // Kolla om site behöver byggas
                Building siteAsBuilding = site.GetComponent<Building>(); // ConstructionSite ärver inte Building, så hämta Building
                if (siteAsBuilding != null && siteAsBuilding.CurrentState == BuildingState.Constructing)
                {
                    Server_TransitionToState(WorkerState.MovingToBuild, markerOrSiteIdentity.netId);
                    movementComponent?.Server_SetDestination(site.transform.position);
                    Debug.Log($"[Server] Worker {netId} ordered to move to ConstructionSite {site.netId} (likely retargeted).");
                    return;
                }
                else { Debug.LogWarning($"[Server] Worker {netId} cannot build site {site.netId}, it's not in Constructing state (State: {siteAsBuilding?.CurrentState})."); }
            }

            // Om det varken var Marker eller Site...
            Debug.LogWarning($"[Server] Worker {netId} received Cmd_StartBuilding for an invalid target type on {markerOrSiteIdentity.netId}.");

        }

        // Cmd_StartRepairing och Cmd_InitiateCapture är oförändrade, de ska fortfarande
        // rikta sig mot den slutliga byggnaden (inte en marker).

        [Command]
        public void Cmd_StartRepairing(NetworkIdentity buildingIdentity)
        {
            if (!IsOwner(connectionToClient)) return;
            if (buildingIdentity == null) return;
            Building target = buildingIdentity.GetComponent<Building>();
            if (target == null || target.healthComponent == null || target.IsDead || target.healthComponent.CurrentHealth >= target.healthComponent.MaxHealth || target.CurrentState == BuildingState.Destroyed) return;
            // TODO: Validera ägarskap/lag (t.ex. bara egna/allierade)
            if (target.OwnerNetId != this.ownerNetId) { Debug.LogWarning($"Worker {netId} cannot repair building {target.netId} owned by someone else."); return; }

            Server_TransitionToState(WorkerState.MovingToRepair, buildingIdentity.netId);
            movementComponent?.Server_SetDestination(target.transform.position);
        }

        [Command]
        public void Cmd_InitiateCapture(NetworkIdentity buildingIdentity)
        {
            if (!IsOwner(connectionToClient)) return;
            if (buildingIdentity == null) return;
            Building target = buildingIdentity.GetComponent<Building>();
            if (target == null || target.IsDead || target.IsBeingCaptured || target.CurrentState == BuildingState.Destroyed) { TargetNotifyCaptureFailed("Target invalid or busy."); return; }
            // Validera att målet INTE ägs av spelaren
            if (target.OwnerNetId == this.ownerNetId) { TargetNotifyCaptureFailed("Cannot capture own building."); return; }
            // TODO: Validera att målet är neutralt eller fiende (via PlayerManager?)

            Server_TransitionToState(WorkerState.MovingToCapture, buildingIdentity.netId);
            movementComponent?.Server_SetDestination(target.transform.position);
        }

        [Command]
        public void Cmd_GoToIdle()
        {
            if (!IsOwner(connectionToClient)) return;
            Server_TransitionToState(WorkerState.Idle, 0);
        }

        // Cmd_CancelMyCaptureAttempt är oförändrad

        [Command]
        public void Cmd_CancelMyCaptureAttempt(NetworkIdentity buildingIdentity)
        {
            if (!IsOwner(connectionToClient)) return;
            if (buildingIdentity == null) return;
            Building targetBuilding = buildingIdentity.GetComponent<Building>();
            // Kolla om byggnaden finns och faktiskt captureas av DENNA worker
            if (targetBuilding != null && targetBuilding.IsBeingCaptured && targetBuilding.capturingWorkerNetId == this.netId)
            {
                targetBuilding.Server_CancelCaptureAttempt($"Worker {netId} interrupted");
                Server_TransitionToState(WorkerState.Idle, 0); // Gå idle direkt
            }
        }


        // --- Server-Side State & Logic ---

        [Server]
        private void Server_TransitionToState(WorkerState newState, uint targetId)
        {
            WorkerState oldState = currentState;
            // Stoppa nuvarande jobb och släpp capture/builder om nödvändigt
            bool shouldReleaseCapture = (oldState == WorkerState.MovingToCapture || oldState == WorkerState.Capturing);
            // Vi behöver veta om vi ska ta bort oss som builder
            bool wasBuilding = (oldState == WorkerState.Building);
            uint oldTargetId = currentTargetNetId; // Spara gamla målet för att ta bort builder

            Server_StopCurrentWorkCoroutine(shouldReleaseCapture, wasBuilding, oldTargetId); // Skicka med mer info

            // Stoppa rörelse om vi går till Idle eller redan är vid målet för ett jobb
            if (newState == WorkerState.Idle || newState == WorkerState.Building || newState == WorkerState.Repairing || newState == WorkerState.Capturing)
            {
                movementComponent?.Server_StopMovement();
            }
            // Nollställ mål om nya statet är Idle eller bara MovingToPosition
            if (newState == WorkerState.Idle || newState == WorkerState.MovingToPosition) { targetId = 0; }

            // Uppdatera state och target
            currentState = newState;
            currentTargetNetId = targetId;
            currentTargetCache = null; // Rensa cache, hämtas vid behov

            // Försök cacha målet direkt om vi fick ett ID
            if (currentTargetNetId != 0)
            {
                if (NetworkServer.spawned.TryGetValue(currentTargetNetId, out var identity))
                {
                    currentTargetCache = identity?.GetComponent<Component>(); // Hämta bas Component
                    if (currentTargetCache == null) Debug.LogWarning($"Worker {netId} could not find target Component for target {currentTargetNetId} on state transition.");
                }
                else { Debug.LogWarning($"Worker {netId} could not find NetworkIdentity for target {currentTargetNetId} on state transition."); }
            }
            // Ingen automatisk jobbsökning för Worker
        }

        [Server]
        public override void OnMovementArrival()
        {
            // Debug.Log($"Worker {netId} arrived. State: {currentState}, Target: {currentTargetNetId}");

            Component targetComponent = null; // Använd den generella cachen
            if (currentTargetNetId != 0)
            {
                if (currentTargetCache != null) targetComponent = currentTargetCache;
                else if (NetworkServer.spawned.TryGetValue(currentTargetNetId, out var identity)) targetComponent = identity?.GetComponent<Component>();
            }

            // Om målet försvann medan vi rörde oss (och vi inte bara flyttade till en position)
            if (targetComponent == null && currentState != WorkerState.MovingToPosition && currentState != WorkerState.Idle)
            {
                Debug.LogWarning($"Worker {netId} arrived but target {currentTargetNetId} not found. Going Idle.");
                Server_TransitionToState(WorkerState.Idle, 0); return;
            }

            switch (currentState)
            {
                // ÄNDRAD: MovingToBuild ska nu interagera med markören
                case WorkerState.MovingToBuild:
                    if (targetComponent is PlacementMarker marker) Server_InteractWithPlacementMarker(marker); // Om det är en marker
                    else if (targetComponent is ConstructionSite site) HandleArrival_Build(site.GetComponent<Building>()); // Om det är en site (retargeting)
                    else { Debug.LogWarning($"Worker {netId} arrived at build target {currentTargetNetId}, but it's neither PlacementMarker nor ConstructionSite?"); Server_TransitionToState(WorkerState.Idle, 0); }
                    break;
                // Oförändrade: Repair och Capture hanterar fortfarande Buildings
                case WorkerState.MovingToRepair:
                    if (targetComponent is Building buildingToRepair) HandleArrival_Repair(buildingToRepair);
                    else { Debug.LogWarning($"Worker {netId} arrived at repair target {currentTargetNetId}, but it's not a Building?"); Server_TransitionToState(WorkerState.Idle, 0); }
                    break;
                case WorkerState.MovingToCapture:
                    if (targetComponent is Building buildingToCapture) HandleArrival_Capture(buildingToCapture);
                    else { Debug.LogWarning($"Worker {netId} arrived at capture target {currentTargetNetId}, but it's not a Building?"); Server_TransitionToState(WorkerState.Idle, 0); }
                    break;
                // Oförändrad: Gå Idle efter att ha flyttat till en position
                case WorkerState.MovingToPosition:
                    Server_TransitionToState(WorkerState.Idle, 0);
                    break;
                default:
                    // Om vi anländer i ett annat state (t.ex. Idle), stoppa bara rörelsen.
                    movementComponent?.Server_StopMovement();
                    break;
            }
        }

        // NY METOD: Triggar interaktion med markören
        [Server]
        private void Server_InteractWithPlacementMarker(PlacementMarker marker)
        {
            if (marker == null)
            {
                Debug.LogWarning($"Worker {netId} tried to interact with a null marker. Going Idle.");
                Server_TransitionToState(WorkerState.Idle, 0);
                return;
            }
            Debug.Log($"[Server] Worker {netId} interacting with PlacementMarker {marker.netId}");
            // Anropa metoden på markören som sköter transformationen
            marker.Server_WorkerArrivedToBuild(this.netIdentity); // Skicka med vår egen identity

            // Workern går nu Idle i väntan på att få en NY build-order från markören
            // riktad mot den nyskapade ConstructionSite.
            Server_TransitionToState(WorkerState.Idle, 0);
        }


        // ÄNDRAD: Tar nu ConstructionSite som parameter (för retargeting)
        [Server]
        private void HandleArrival_Build(Building targetSiteBuilding) // Accepterar nu Building (som finns på ConstructionSite)
        {
            if (targetSiteBuilding == null) { Server_TransitionToState(WorkerState.Idle, 0); return; }

            // Försök assigna till den existerande siten (detta händer efter retargeting)
            if (targetSiteBuilding.Server_AssignBuilder(this.netId))
            {
                Server_TransitionToState(WorkerState.Building, targetSiteBuilding.netId); // Sätt state till Building
                Server_StartWorkCoroutine(ConstructionWorkLoop(targetSiteBuilding));
            }
            else { Debug.LogWarning($"Worker {netId} failed assign build to existing site {targetSiteBuilding.netId}. Site might be full or in wrong state. Idle."); Server_TransitionToState(WorkerState.Idle, 0); }
        }
        [Server]
        private void HandleArrival_Repair(Building targetBuilding)
        {
            if (targetBuilding == null) { Server_TransitionToState(WorkerState.Idle, 0); return; }
            if (targetBuilding.healthComponent != null && targetBuilding.healthComponent.CurrentHealth < targetBuilding.healthComponent.MaxHealth)
            {
                Server_TransitionToState(WorkerState.Repairing, currentTargetNetId);
                Server_StartWorkCoroutine(RepairWorkLoop(targetBuilding));
            }
            else { Debug.Log($"Worker {netId} arrived repair {currentTargetNetId}, not needed. Idle."); Server_TransitionToState(WorkerState.Idle, 0); }
        }
        [Server]
        private void HandleArrival_Capture(Building targetBuilding)
        {
            if (targetBuilding == null) { Server_TransitionToState(WorkerState.Idle, 0); return; }
            // Använd netId här istället för netIdentity? Nej, Server_StartCaptureAttempt behöver NetworkIdentity.
            if (targetBuilding.Server_StartCaptureAttempt(this.netIdentity))
            {
                Server_TransitionToState(WorkerState.Capturing, currentTargetNetId);
                // Ingen Coroutine här, Capture sköts av Building
            }
            else { Debug.LogWarning($"Worker {netId} failed start capture {currentTargetNetId}. Idle."); Server_TransitionToState(WorkerState.Idle, 0); }
        }


        [Server]
        private void Server_StartWorkCoroutine(IEnumerator routine)
        {
            Server_StopCurrentWorkCoroutine(false, false, 0); // Stoppa ev. gammal, släpp inte capture, inte building heller just nu
            server_workCoroutine = StartCoroutine(routine);
        }

        // Uppdaterad för att hantera att ta bort builder från gamla målet
        [Server]
        private void Server_StopCurrentWorkCoroutine(bool cancelCaptureIfNeeded, bool wasBuilding, uint oldTargetId)
        {
            if (server_workCoroutine != null)
            {
                StopCoroutine(server_workCoroutine);
                server_workCoroutine = null;
            }

            // Om vi var i bygg-state och hade ett gammalt mål, ta bort oss som builder därifrån
            if (wasBuilding && oldTargetId != 0)
            {
                // Försök hitta gamla byggnaden/siten
                if (NetworkServer.spawned.TryGetValue(oldTargetId, out var oldIdentity))
                {
                    Building oldBuilding = oldIdentity.GetComponent<Building>();
                    oldBuilding?.Server_RemoveBuilder(netId);
                }
            }

            // Om vi ska avbryta capture och är/var i det statet och har ett mål
            if (cancelCaptureIfNeeded && (currentState == WorkerState.Capturing || currentState == WorkerState.MovingToCapture) && currentTargetCache != null)
            {
                Building buildingToCancelCapture = currentTargetCache as Building;
                if (buildingToCancelCapture != null && buildingToCancelCapture.capturingWorkerNetId == this.netId)
                {
                    buildingToCancelCapture.Server_CancelCaptureAttempt($"Worker {netId} stopped/got new order");
                }
            }
        }

        // Coroutines för ConstructionWorkLoop och RepairWorkLoop är oförändrade

        [Server]
        private IEnumerator ConstructionWorkLoop(Building target)
        {
            if (target == null) { Server_TransitionToState(WorkerState.Idle, 0); server_workCoroutine = null; yield break; }
            uint targetId = target.netId;
            // Debug.Log($"Worker {netId} starting construction loop on {targetId}");

            while (target != null && target.CurrentState == BuildingState.Constructing)
            {
                // Försök hitta ConstructionSite-komponenten på målet
                ConstructionSite targetSite = target.GetComponent<ConstructionSite>();
                if (targetSite == null) { Debug.LogError($"Worker {netId} building target {targetId} which is not a ConstructionSite!"); break; }

                targetSite.Server_ContributeWork(constructionWorkPerSecond * Time.deltaTime); // Använd variabeln! Uppdatera oftare? Varje frame?
                yield return null; // Kör varje frame för jämnare progress? Eller WaitForSeconds(0.1f)?

                // Checkar för att avbryta loopen
                if (currentState != WorkerState.Building) { target?.Server_RemoveBuilder(this.netId); server_workCoroutine = null; yield break; } // Om state ändrats
                if (target == null) { if (NetworkServer.spawned.TryGetValue(targetId, out var id)) target = id.GetComponent<Building>(); } // Försök hitta igen
                if (target == null || target.CurrentState != BuildingState.Constructing) { break; } // Om målet förstörts eller blivit klart
            }
            // Debug.Log($"Worker {netId} finished construction loop on {targetId}.");
            if (currentState == WorkerState.Building) { Server_TransitionToState(WorkerState.Idle, 0); } // Gå Idle om vi fortfarande var i building state
            server_workCoroutine = null;
        }


        [Server]
        private IEnumerator RepairWorkLoop(Building target)
        {
            if (target == null || target.healthComponent == null) { Server_TransitionToState(WorkerState.Idle, 0); server_workCoroutine = null; yield break; }
            uint targetId = target.netId;
            // Debug.Log($"Worker {netId} starting repair loop on {targetId}");
            while (target != null && target.CurrentState != BuildingState.Destroyed && target.healthComponent.CurrentHealth < target.healthComponent.MaxHealth)
            {
                target.healthComponent.Server_Repair(repairAmountPerSecond * Time.deltaTime); // Använd variabeln! Applicera per frame?
                yield return null; // Kör varje frame

                // Checkar för att avbryta loopen
                if (currentState != WorkerState.Repairing) { server_workCoroutine = null; yield break; } // Om state ändrats
                if (target == null) { if (NetworkServer.spawned.TryGetValue(targetId, out var id)) target = id.GetComponent<Building>(); } // Försök hitta igen
                if (target == null || target.healthComponent == null || target.CurrentState == BuildingState.Destroyed || target.healthComponent.CurrentHealth >= target.healthComponent.MaxHealth) { break; } // Om målet förstörts, blivit fullt reparerat etc.
            }
            // Debug.Log($"Worker {netId} finished repair loop on {targetId}.");
            if (currentState == WorkerState.Repairing) { Server_TransitionToState(WorkerState.Idle, 0); }
            server_workCoroutine = null;
        }


        // --- Target RPCs (Oförändrade) ---
        [TargetRpc] public void Target_ConstructionComplete(NetworkIdentity buildingIdentity) { GoToIdleStateLocally("Construction complete"); }
        [TargetRpc] public void Target_CaptureComplete(NetworkIdentity buildingIdentity) { GoToIdleStateLocally("Capture complete"); }
        [TargetRpc] public void Target_CaptureInterrupted(NetworkIdentity buildingIdentity) { GoToIdleStateLocally("Capture interrupted"); }
        [TargetRpc] public void TargetSetCaptureState(bool isStarting) { /* Kanske inte behövs */ }
        [TargetRpc] public void TargetNotifyCaptureFailed(string reason) { Debug.LogWarning($"Capture failed for worker {netId}: {reason}"); GoToIdleStateLocally("Capture failed"); }


        // --- Client-Side Helper Methods ---
        // Ändrad till att cacha generell Component
        private IEnumerator FindTargetLocally(uint targetNetId)
        {
            if (targetNetId == 0) yield break;
            if (NetworkClient.spawned.TryGetValue(targetNetId, out NetworkIdentity identity))
            {
                currentTargetCache = identity?.GetComponent<Component>();
                if (currentTargetCache != null) yield break;
            }
            float timeout = Time.time + 5f;
            while (Time.time < timeout && currentTargetCache == null)
            {
                if (NetworkClient.spawned.TryGetValue(targetNetId, out identity))
                {
                    currentTargetCache = identity?.GetComponent<Component>();
                }
                if (currentTargetCache == null) yield return null;
            }
        }
        private void GoToIdleStateLocally(string reason) { currentTargetCache = null; /* Animation via hook */ }

    } // End class ConstructionWorker
} // End namespace RTSGAME