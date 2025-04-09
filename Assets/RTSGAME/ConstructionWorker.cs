// Assets/RTSGAME/Scripts/Units/ConstructionWorker.cs
using Mirror;
using UnityEngine;
using System.Collections; // För Coroutine

namespace RTSGAME
{
    public enum WorkerState { Idle, MovingToPosition, MovingToBuild, Building, MovingToRepair, Repairing, MovingToCapture, Capturing }

    [RequireComponent(typeof(UnitMovement))]
    public class ConstructionWorker : Unit // Ärver från Unit
    {
        [Header("Worker Specifics")]
        [Tooltip("How close the worker needs to be to interact with a building.")]
        [SerializeField] private float interactionRange = 5f;
        [Tooltip("Amount of 'health' repaired per second.")]
        [SerializeField] private float repairAmountPerSecond = 20f;
        [Tooltip("Amount of 'progress' added per second when constructing.")]
        [SerializeField] private float constructionWorkPerSecond = 1f;

        // --- State Machine & Target ---
        [SyncVar(hook = nameof(OnStateChangedHook))]
        private WorkerState currentState = WorkerState.Idle; // Använder WorkerState här

        [SyncVar]
        private uint currentTargetNetId = 0;
        private Building currentTargetBuildingCache = null;

        private Coroutine server_workCoroutine = null;

        // --- Unity & Mirror Callbacks ---
        // Awake, OnStartServer ärvs från Unit

        public override void OnStartClient()
        {
            base.OnStartClient(); // Anropa Unit's OnStartClient
            if (currentState != WorkerState.Idle && currentTargetNetId != 0) { StartCoroutine(FindTargetBuildingLocally(currentTargetNetId)); }
            // Anropa animationsuppdatering med WorkerState
            OnStateChangedHook(currentState, currentState); // Tvinga hook-anrop
        }

        // --- State Change Hook (Client-side) ---
        void OnStateChangedHook(WorkerState oldState, WorkerState newState)
        {
            // Debug.Log($"Worker {netId} state changed on client: {oldState} -> {newState}");
            if (newState == WorkerState.Idle) { currentTargetBuildingCache = null; }
            UpdateAnimationWorker(oldState, newState); // Anropa animationsmetoden för Worker
        }

        // --- Animation Update (Client-side) ---
        protected virtual void UpdateAnimationWorker(WorkerState oldState, WorkerState newState)
        {
            if (animator == null) return;
            bool isMoving = (newState == WorkerState.MovingToBuild || newState == WorkerState.MovingToCapture || newState == WorkerState.MovingToRepair || newState == WorkerState.MovingToPosition);
            animator.SetBool("IsMoving", isMoving);
            animator.SetBool("IsBuilding", newState == WorkerState.Building);
            animator.SetBool("IsRepairing", newState == WorkerState.Repairing);
            animator.SetBool("IsCapturing", newState == WorkerState.Capturing);
        }


        // --- Server-Side State Logic ---
        [ServerCallback]
        void Update()
        {
            // Servern övervakar state och avstånd
            if (currentState == WorkerState.Building || currentState == WorkerState.Repairing)
            {
                if (currentTargetBuildingCache == null && currentTargetNetId != 0) { if (NetworkServer.spawned.TryGetValue(currentTargetNetId, out var id)) { currentTargetBuildingCache = id.GetComponent<Building>(); } }
                Building targetBuilding = currentTargetBuildingCache;
                if (targetBuilding == null || targetBuilding.IsDead) { Server_TransitionToState(WorkerState.Idle, 0); return; }
                float distance = Vector3.Distance(transform.position, targetBuilding.transform.position);
                float maxRange = interactionRange * 1.2f;
                if (distance > maxRange)
                {
                    if (currentState == WorkerState.Building) targetBuilding.Server_RemoveBuilder(this.netId);
                    Server_TransitionToState(WorkerState.Idle, 0);
                }
            }
        }


        // --- Commands (Called from Client via NetworkPlayer, Run on Server) ---

        [Command]
        public void Cmd_MoveToPosition(Vector3 destination)
        {
            // Använder IsOwner från Unit-basklassen
            if (!IsOwner(connectionToClient)) { Debug.LogWarning($"Non-owner tried to move worker {netId}"); return; }
            Server_TransitionToState(WorkerState.MovingToPosition, 0);
            movementComponent?.Server_SetDestination(destination);
        }

        [Command]
        public void Cmd_StartBuilding(NetworkIdentity buildingGhostIdentity)
        {
            if (!IsOwner(connectionToClient)) return;
            if (buildingGhostIdentity == null) return;
            Building target = buildingGhostIdentity.GetComponent<Building>();
            if (target == null || (target.CurrentState != BuildingState.Placing && target.CurrentState != BuildingState.Constructing)) return;
            // TODO: Validera ägarskap/team för byggnaden
            Server_TransitionToState(WorkerState.MovingToBuild, buildingGhostIdentity.netId);
            movementComponent?.Server_SetDestination(target.transform.position);
        }

        [Command]
        public void Cmd_StartRepairing(NetworkIdentity buildingIdentity)
        {
            if (!IsOwner(connectionToClient)) return;
            if (buildingIdentity == null) return;
            Building target = buildingIdentity.GetComponent<Building>();
            // Använder healthComponent från Building nu!
            if (target == null || target.healthComponent == null || target.healthComponent.IsDead || target.healthComponent.CurrentHealth >= target.healthComponent.MaxHealth || target.CurrentState == BuildingState.Destroyed) return;
            // TODO: Validera ägarskap/lag
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
            // TODO: Validera ägarskap/lag - FÅR INTE vara vår/allierad
            Server_TransitionToState(WorkerState.MovingToCapture, buildingIdentity.netId);
            movementComponent?.Server_SetDestination(target.transform.position);
        }

        [Command]
        public void Cmd_GoToIdle()
        {
            if (!IsOwner(connectionToClient)) return;
            Server_TransitionToState(WorkerState.Idle, 0);
        }

        [Command]
        public void Cmd_CancelMyCaptureAttempt(NetworkIdentity buildingIdentity)
        {
            if (!IsOwner(connectionToClient)) return;
            if (buildingIdentity == null) return;
            Building targetBuilding = buildingIdentity.GetComponent<Building>();
            // TODO: Validera att this.netId == targetBuilding.CapturingWorkerNetId
            if (targetBuilding != null && targetBuilding.IsBeingCaptured /* && targetBuilding.CapturingWorkerNetId == this.netId */ )
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
            Server_StopCurrentWorkCoroutine();
            if (newState == WorkerState.Idle || newState == WorkerState.MovingToPosition) { targetId = 0; movementComponent?.Server_StopMovement(); }

            currentState = newState; // Uppdatera state FÖRST
            currentTargetNetId = targetId; // Uppdatera mål ID
            currentTargetBuildingCache = null; // Rensa cache

            if (currentTargetNetId != 0)
            { // Försök fylla cache direkt
                if (NetworkServer.spawned.TryGetValue(currentTargetNetId, out var identity)) { currentTargetBuildingCache = identity.GetComponent<Building>(); }
                else { Debug.LogWarning($"Worker {netId} could not find target {currentTargetNetId} on server when transitioning state."); }
            }
            if (newState == WorkerState.Idle) { movementComponent?.Server_StopMovement(); } // Stoppa igen för säkerhets skull
        }

        // Överskugga basklassens OnMovementArrival
        [Server]
        public override void OnMovementArrival()
        {
            Debug.Log($"Worker {netId} arrived at destination. Trying action for state: {currentState}");
            Building targetBuilding = null;
            if (currentTargetNetId != 0 && NetworkServer.spawned.TryGetValue(currentTargetNetId, out var identity)) { targetBuilding = identity.GetComponent<Building>(); }

            if (targetBuilding == null && currentState != WorkerState.MovingToPosition && currentState != WorkerState.Idle)
            {
                Debug.LogWarning($"Worker {netId} arrived but target building {currentTargetNetId} not found. Going Idle.");
                Server_TransitionToState(WorkerState.Idle, 0); return;
            }

            switch (currentState)
            {
                case WorkerState.MovingToBuild: HandleArrival_Build(targetBuilding); break;
                case WorkerState.MovingToRepair: HandleArrival_Repair(targetBuilding); break;
                case WorkerState.MovingToCapture: HandleArrival_Capture(targetBuilding); break;
                case WorkerState.MovingToPosition: Server_TransitionToState(WorkerState.Idle, 0); break;
                default: movementComponent?.Server_StopMovement(); break; // Stanna om i annat state
            }
        }

        // Uppdelade ankomsthanterare för tydlighet
        [Server]
        private void HandleArrival_Build(Building targetBuilding)
        {
            if (targetBuilding.Server_AssignBuilder(this.netId))
            {
                Server_TransitionToState(WorkerState.Building, currentTargetNetId);
                Server_StartWorkCoroutine(ConstructionWorkLoop(targetBuilding));
            }
            else { Debug.LogWarning($"Worker {netId} failed assign build {currentTargetNetId}. Idle."); Server_TransitionToState(WorkerState.Idle, 0); }
        }
        [Server]
        private void HandleArrival_Repair(Building targetBuilding)
        {
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
            if (targetBuilding.Server_StartCaptureAttempt(this.netIdentity))
            {
                Server_TransitionToState(WorkerState.Capturing, currentTargetNetId);
            }
            else { Debug.LogWarning($"Worker {netId} failed start capture {currentTargetNetId}. Idle."); Server_TransitionToState(WorkerState.Idle, 0); }
        }


        [Server] private void Server_StartWorkCoroutine(IEnumerator routine) { Server_StopCurrentWorkCoroutine(); server_workCoroutine = StartCoroutine(routine); }
        [Server] private void Server_StopCurrentWorkCoroutine() { /* ... som tidigare ... */ }
        [Server] private IEnumerator ConstructionWorkLoop(Building target) { /* ... som tidigare ... */ yield return null; }
        [Server] private IEnumerator RepairWorkLoop(Building target) { /* ... som tidigare ... */ yield return null; }


        // --- Target RPCs (Called By Server/Building, Run on Owning Client) ---
        [TargetRpc] public void Target_ConstructionComplete(NetworkIdentity buildingIdentity) { GoToIdleStateLocally("Construction complete"); }
        [TargetRpc] public void Target_CaptureComplete(NetworkIdentity buildingIdentity) { GoToIdleStateLocally("Capture complete"); }
        [TargetRpc] public void Target_CaptureInterrupted(NetworkIdentity buildingIdentity) { GoToIdleStateLocally("Capture interrupted"); }
        [TargetRpc] public void TargetSetCaptureState(bool isStarting) { /* Kanske inte behövs */ }
        [TargetRpc] public void TargetNotifyCaptureFailed(string reason) { Debug.LogWarning($"Capture failed for worker {netId}: {reason}"); GoToIdleStateLocally("Capture failed"); }


        // --- Client-Side Helper Methods ---
        private IEnumerator FindTargetBuildingLocally(uint targetNetId) { /* ... som tidigare ... */ yield return null; }
        private void GoToIdleStateLocally(string reason) { /* ... som tidigare ... */ }

    }
}