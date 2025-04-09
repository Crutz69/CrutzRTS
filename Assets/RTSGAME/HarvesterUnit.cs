// Assets/RTSGAME/Scripts/Units/HarvesterUnit.cs
using Mirror;
using UnityEngine;
using UnityEngine.UI; // För Slider/Image
using System.Collections;
using System.Collections.Generic; // För List<>

namespace RTSGAME
{
    // Enum bör ligga globalt eller i egen fil: CrystalType.cs
    // public enum CrystalType { None, Green, Blue, Red }

    [RequireComponent(typeof(UnitMovement))] // Ärver Unit, som kräver resten
    public class HarvesterUnit : Unit // Ärver från Unit
    {
        [Header("Harvester Settings")]
        [SerializeField] private int carryCapacity = 5;
        [SerializeField] private float pickupRange = 2f;
        [SerializeField] private float pickupDuration = 1.5f; // Tid att samla EN kristall

        // State Machine (specifik för Harvester)
        public enum HarvesterState { Idle, MovingToPosition, MovingToCrystal, Harvesting, MovingToRefinery, Depositing }
        [SyncVar(hook = nameof(OnStateChangedHook))]
        private HarvesterState currentState = HarvesterState.Idle;

        // Inventarie (synkas till klienter för UI/visuella effekter)
        [SyncVar(hook = nameof(OnInventoryChangedHook))]
        private int currentLoad = 0;
        [SyncVar(hook = nameof(OnInventoryChangedHook))] // Ändra färg på UI/effekt
        private CrystalType carriedCrystalType = CrystalType.None;

        // Mål (NetId synkas, lokal cache för script-referens)
        [SyncVar] private uint targetCrystalNetId = 0;
        [SyncVar] private uint targetRefineryNetId = 0;
        private HarvestableCrystal currentTargetCrystalCache = null; // Klient & server cache
        private RefineryBuilding currentTargetRefineryCache = null; // Klient & server cache

        [Header("Target Finding")]
        [Tooltip("Layers containing harvestable resources (e.g., crystals). Set in Inspector!")]
        [SerializeField] private LayerMask resourceLayerMask;
        [Tooltip("Layer containing refineries. Set in Inspector!")]
        [SerializeField] private LayerMask refineryLayerMask;
        [Tooltip("How far the harvester searches for targets initially.")]
        [SerializeField] private float initialSearchRadius = 50f;
        [Tooltip("Maximum colliders to check in one physics query.")]
        [SerializeField] private int maxQueryColliders = 32;
        // Återanvändbar array för fysikresultat (server-side)
        private Collider[] queryResults;

        [Header("Harvester UI")]
        [SerializeField] private Slider inventoryBarSlider; // Koppla i prefab
        [SerializeField] private Image inventoryBarFillImage; // Koppla i prefab
        [SerializeField] private GameObject inventoryBarCanvasGO; // Koppla Canvas i prefab

        // Server-side timer/coroutine
        private Coroutine server_workCoroutine = null;


        // --- Mirror Callbacks & Unity ---

        public override void OnStartServer()
        {
            base.OnStartServer();
            currentState = HarvesterState.Idle;
            currentLoad = 0;
            carriedCrystalType = CrystalType.None;
            targetCrystalNetId = 0;
            targetRefineryNetId = 0;
            queryResults = new Collider[maxQueryColliders]; // Initiera arrayen på servern
            // TODO: Starta AI:n på servern om detta är en AI-harvester?
            // if (isAIControlled) Server_FindWork();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Initial UI setup baserat på SyncVars
            OnInventoryChangedHook(0, currentLoad, CrystalType.None, carriedCrystalType);
            OnStateChangedHook(currentState, currentState); // Tvinga initial hook call
            // Försök hitta mål om state kräver det
            if (targetCrystalNetId != 0) StartCoroutine(FindTargetCrystalLocally(targetCrystalNetId));
            if (targetRefineryNetId != 0) StartCoroutine(FindTargetRefineryLocally(targetRefineryNetId));
        }


        // --- SyncVar Hooks (Client-side) ---

        void OnStateChangedHook(HarvesterState oldState, HarvesterState newState)
        {
            // Debug.Log($"Harvester {netId} client state: {oldState} -> {newState}");
            if (newState == HarvesterState.Idle) { currentTargetCrystalCache = null; currentTargetRefineryCache = null; }
            UpdateAnimationHarvester(oldState, newState);
        }

        void OnInventoryChangedHook(int oldLoad, int newLoad, CrystalType oldType, CrystalType newType)
        {
            UpdateInventoryBarUI(newLoad, newType);
        }

        // --- Animation & UI (Client-side) ---

        void UpdateAnimationHarvester(HarvesterState oldState, HarvesterState newState)
        {
            if (animator == null) return; // animator ärvs från Unit
            bool isMoving = (newState == HarvesterState.MovingToCrystal || newState == HarvesterState.MovingToRefinery || newState == HarvesterState.MovingToPosition);
            animator.SetBool("IsMoving", isMoving);
            animator.SetBool("IsHarvesting", newState == HarvesterState.Harvesting);
            // float speed = networkTransform != null ? networkTransform.calculateVelocity.magnitude : 0f;
            // animator.SetFloat("Forward", speed, 0.1f, Time.deltaTime);
        }

        void UpdateInventoryBarUI(int load, CrystalType type)
        {
            if (inventoryBarSlider == null || inventoryBarCanvasGO == null) return;
            bool showBar = (load > 0);
            inventoryBarCanvasGO.SetActive(showBar);
            if (!showBar) return;
            inventoryBarSlider.value = (carryCapacity > 0) ? Mathf.Clamp01((float)load / carryCapacity) : 0f;
            if (inventoryBarFillImage != null) { inventoryBarFillImage.color = GetCrystalColor(type); }
        }
        Color GetCrystalColor(CrystalType type)
        {
            switch (type) { case CrystalType.Green: return Color.green; case CrystalType.Blue: return Color.blue; case CrystalType.Red: return Color.red; default: return Color.grey; }
        }


        // --- Commands (Called by Local Player via NetworkPlayer, Run on Server) ---

        [Command]
        public void Cmd_OrderHarvest(NetworkIdentity resourceIdentity)
        {
            if (!IsOwner(connectionToClient)) return;
            if (resourceIdentity == null) return;
            HarvestableCrystal targetCrystal = resourceIdentity.GetComponent<HarvestableCrystal>();
            if (targetCrystal == null) return;
            if (currentLoad >= carryCapacity) { Server_FindRefineryAndMove(); return; }

            if (targetCrystal.Server_TryReserve(this.netIdentity))
            {
                Server_TransitionToState(HarvesterState.MovingToCrystal, resourceIdentity.netId, 0);
                movementComponent?.Server_SetDestination(targetCrystal.transform.position);
            }
            else { Target_AssignTaskFailed("Crystal is already targeted."); Server_FindWork(); }
        }

        [Command]
        public void Cmd_OrderDeposit(NetworkIdentity refineryIdentity)
        {
            if (!IsOwner(connectionToClient)) return;
            if (refineryIdentity == null) return;
            RefineryBuilding targetRefinery = refineryIdentity.GetComponent<RefineryBuilding>();
            if (targetRefinery == null) return;
            if (currentLoad <= 0) return;
            // if(targetRefinery.OwnerNetId != this.ownerNetId) return; // Validera ägarskap?

            Server_TransitionToState(HarvesterState.MovingToRefinery, 0, refineryIdentity.netId);
            Vector3 dest = targetRefinery.dockingPoint != null ? targetRefinery.dockingPoint.position : targetRefinery.transform.position;
            movementComponent?.Server_SetDestination(dest);
        }

        [Command]
        public void Cmd_GoToIdle()
        {
            if (!IsOwner(connectionToClient)) return;
            Server_TransitionToState(HarvesterState.Idle, 0, 0);
        }

        [Command]
        public void Cmd_MoveToPosition(Vector3 destination)
        {
            if (!IsOwner(connectionToClient)) return;
            Server_TransitionToState(HarvesterState.MovingToPosition, 0, 0);
            movementComponent?.Server_SetDestination(destination);
        }


        // --- Server-Side Logic ---

        [Server]
        private void Server_TransitionToState(HarvesterState newState, uint crystalTargetId, uint refineryTargetId)
        {
            HarvesterState oldState = currentState;
            Server_StopCurrentWorkCoroutine(true);

            currentState = newState;
            targetCrystalNetId = crystalTargetId;
            targetRefineryNetId = refineryTargetId;
            currentTargetCrystalCache = null;
            currentTargetRefineryCache = null;

            if (targetCrystalNetId != 0 && NetworkServer.spawned.TryGetValue(targetCrystalNetId, out var id1)) currentTargetCrystalCache = id1.GetComponent<HarvestableCrystal>();
            if (targetRefineryNetId != 0 && NetworkServer.spawned.TryGetValue(targetRefineryNetId, out var id2)) currentTargetRefineryCache = id2.GetComponent<RefineryBuilding>();

            if (newState == HarvesterState.Idle || newState == HarvesterState.MovingToPosition)
            {
                movementComponent?.Server_StopMovement();
            }
        }

        [Server]
        private void Server_StopCurrentWorkCoroutine(bool releaseCrystal)
        {
            if (server_workCoroutine != null) { StopCoroutine(server_workCoroutine); server_workCoroutine = null; }
            if (releaseCrystal && (currentState == HarvesterState.MovingToCrystal || currentState == HarvesterState.Harvesting) && currentTargetCrystalCache != null)
            {
                currentTargetCrystalCache.Server_TryRelease(this.netId);
            }
        }

        [Server]
        public override void OnMovementArrival()
        {
            // base.OnMovementArrival();
            Debug.Log($"Harvester {netId} arrived. State: {currentState}");
            switch (currentState)
            {
                case HarvesterState.MovingToCrystal: Server_StartGathering(); break;
                case HarvesterState.MovingToRefinery: Server_AttemptDeposit(); break;
                case HarvesterState.MovingToPosition: Server_TransitionToState(HarvesterState.Idle, 0, 0); break;
                default: movementComponent?.Server_StopMovement(); break;
            }
        }

        [Server]
        private void Server_StartGathering()
        {
            if (targetCrystalNetId == 0 || !NetworkServer.spawned.TryGetValue(targetCrystalNetId, out var id)) { Server_TransitionToState(HarvesterState.Idle, 0, 0); return; }
            currentTargetCrystalCache = id.GetComponent<HarvestableCrystal>();
            if (currentTargetCrystalCache == null) { Server_TransitionToState(HarvesterState.Idle, 0, 0); return; }

            if (currentTargetCrystalCache.TargetedByNetId != this.netId) { Debug.LogWarning("Lost reservation."); Server_TransitionToState(HarvesterState.Idle, 0, 0); return; }
            float distance = Vector3.Distance(transform.position, currentTargetCrystalCache.transform.position);
            if (distance > pickupRange * 1.1f) { Debug.LogWarning("Too far to gather."); Server_TransitionToState(HarvesterState.MovingToCrystal, targetCrystalNetId, 0); movementComponent?.Server_SetDestination(currentTargetCrystalCache.transform.position); return; }

            Server_TransitionToState(HarvesterState.Harvesting, targetCrystalNetId, 0);
            movementComponent?.Server_StopMovement();
            Server_StartWorkCoroutine(HarvestTimer());
        }

        // Server Coroutine
        [Server]
        private IEnumerator HarvestTimer()
        {
            HarvestableCrystal target = currentTargetCrystalCache;
            if (target == null) { Debug.LogError($"Harvester {netId} HarvestTimer started with null target!"); Server_TransitionToState(HarvesterState.Idle, 0, 0); server_workCoroutine = null; yield break; }
            uint harvestingTargetId = target.netId;
            Debug.Log($"Harvester {netId} starting harvest timer ({pickupDuration}s) for {harvestingTargetId}");
            yield return new WaitForSeconds(pickupDuration);

            if (currentState == HarvesterState.Harvesting && targetCrystalNetId == harvestingTargetId) // Använd targetCrystalNetId här!
            {
                HarvestableCrystal finalTargetCrystal = null;
                if (NetworkServer.spawned.TryGetValue(harvestingTargetId, out var id)) finalTargetCrystal = id.GetComponent<HarvestableCrystal>();

                // Använd propertyn TargetedByNetId här!
                if (finalTargetCrystal != null && finalTargetCrystal.TargetedByNetId == this.netId)
                {
                    Server_CompleteGathering(finalTargetCrystal);
                }
                else { Debug.LogWarning($"Target {harvestingTargetId} invalid/lost reserve during timer."); Server_FindWork(); }
            }
            else { Debug.Log($"State/target changed during harvest timer."); }
            server_workCoroutine = null;
        }


        [Server]
        private void Server_CompleteGathering(HarvestableCrystal crystal)
        {
            if (crystal == null) return;
            CrystalType gatheredType = crystal.crystalType;
            if (currentLoad < carryCapacity && (carriedCrystalType == CrystalType.None || carriedCrystalType == gatheredType))
            {
                if (carriedCrystalType == CrystalType.None) { carriedCrystalType = gatheredType; }
                currentLoad++;
                // Debug.Log($"Harvester {netId} gathered crystal {crystal.netId}. Load: {currentLoad}/{carryCapacity}. Type: {carriedCrystalType}");
                crystal.Server_HarvestComplete();
                currentTargetCrystalCache = null; targetCrystalNetId = 0;
                Server_FindWork();
            }
            else
            {
                Debug.LogWarning($"Harvester {netId} could not gather crystal {crystal.netId}. Load/Type mismatch?");
                crystal.Server_TryRelease(this.netId);
                Server_FindWork();
            }
        }

        [Server]
        private void Server_AttemptDeposit()
        {
            if (targetRefineryNetId == 0 || !NetworkServer.spawned.TryGetValue(targetRefineryNetId, out var id)) { Server_TransitionToState(HarvesterState.Idle, 0, 0); return; }
            currentTargetRefineryCache = id.GetComponent<RefineryBuilding>();
            if (currentTargetRefineryCache == null) { Server_TransitionToState(HarvesterState.Idle, 0, 0); return; }

            Vector3 targetPos = currentTargetRefineryCache.dockingPoint != null ? currentTargetRefineryCache.dockingPoint.position : currentTargetRefineryCache.transform.position;
            float distance = Vector3.Distance(transform.position, targetPos);
            // Använd interactionRange från ConstructionWorker här, eller egen variabel
            float depositRange = 2.0f; // Exempelvärde
            if (distance > depositRange * 1.1f) { Server_TransitionToState(HarvesterState.MovingToRefinery, 0, targetRefineryNetId); movementComponent?.Server_SetDestination(targetPos); return; }

            bool accepted = currentTargetRefineryCache.Server_RequestDeposit(this.netIdentity, currentLoad, carriedCrystalType);
            if (accepted) { Server_TransitionToState(HarvesterState.Depositing, 0, targetRefineryNetId); movementComponent?.Server_StopMovement(); }
            else { Debug.Log($"Deposit denied by refinery {targetRefineryNetId}. Idle."); Server_TransitionToState(HarvesterState.Idle, 0, 0); }
        }

        [Server]
        public void Server_AcknowledgeDepositComplete()
        {
            Debug.Log($"Harvester {netId} deposit acknowledged by server.");
            if (currentState == HarvesterState.Depositing)
            {
                currentLoad = 0;
                carriedCrystalType = CrystalType.None;
                Server_FindWork();
            }
        }

        // Server-metod för att hitta jobb (kristall eller refinery)
        [Server]
        public void Server_FindWork()
        {
            Server_StopCurrentWorkCoroutine(true);

            if (currentLoad >= carryCapacity)
            {
                Server_FindRefineryAndMove();
            }
            else
            {
                HarvestableCrystal targetCrystal = null;
                if (carriedCrystalType != CrystalType.None)
                {
                    targetCrystal = Server_FindClosestAvailableCrystalOfType(carriedCrystalType);
                }
                if (targetCrystal == null)
                {
                    if (currentLoad > 0) { Server_FindRefineryAndMove(); return; } // Om vi har last men inte hittar mer
                    carriedCrystalType = CrystalType.None; currentLoad = 0;
                    targetCrystal = Server_FindClosestAvailableCrystal();
                }

                if (targetCrystal != null)
                {
                    if (targetCrystal.Server_TryReserve(this.netIdentity))
                    {
                        Server_TransitionToState(HarvesterState.MovingToCrystal, targetCrystal.netId, 0);
                        movementComponent?.Server_SetDestination(targetCrystal.transform.position);
                    }
                    else { Server_TransitionToState(HarvesterState.Idle, 0, 0); } // Race condition
                }
                else { Server_TransitionToState(HarvesterState.Idle, 0, 0); } // Inget jobb alls
            }
        }

        // --- Server Find Methods (Använder Physics Query) ---
        [Server]
        private HarvestableCrystal Server_FindClosestAvailableCrystal()
        {
            return FindClosestCrystalInternal(CrystalType.None);
        }
        [Server]
        private HarvestableCrystal Server_FindClosestAvailableCrystalOfType(CrystalType type)
        {
            if (type == CrystalType.None) return Server_FindClosestAvailableCrystal();
            return FindClosestCrystalInternal(type);
        }
        [Server]
        private HarvestableCrystal FindClosestCrystalInternal(CrystalType requiredType)
        {
            if (queryResults == null) queryResults = new Collider[maxQueryColliders]; // Initiera om null
            int hitCount = Physics.OverlapSphereNonAlloc(transform.position, initialSearchRadius, queryResults, resourceLayerMask);
            HarvestableCrystal closestCrystal = null; float closestDistSqr = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                if (queryResults[i].TryGetComponent<HarvestableCrystal>(out HarvestableCrystal crystal))
                {
                    if (crystal.Server_IsAvailable() && (requiredType == CrystalType.None || crystal.crystalType == requiredType))
                    {
                        float distSqr = (crystal.transform.position - transform.position).sqrMagnitude;
                        if (distSqr < closestDistSqr) { closestDistSqr = distSqr; closestCrystal = crystal; }
                    }
                }
            }
            return closestCrystal;
        }
        [Server]
        private void Server_FindRefineryAndMove()
        {
            if (queryResults == null) queryResults = new Collider[maxQueryColliders];
            int hitCount = Physics.OverlapSphereNonAlloc(transform.position, initialSearchRadius * 2, queryResults, refineryLayerMask); // Sök längre
            RefineryBuilding closestRefinery = null; float closestDistSqr = float.MaxValue; uint closestRefineryId = 0;
            for (int i = 0; i < hitCount; i++)
            {
                if (queryResults[i].TryGetComponent<RefineryBuilding>(out RefineryBuilding refinery))
                {
                    // TODO: Ägarkoll eller lagkoll?
                    if (refinery.OwnerNetId == this.ownerNetId)
                    { // Exempel: Måste vara vårt eget
                        float distSqr = (refinery.transform.position - transform.position).sqrMagnitude;
                        if (distSqr < closestDistSqr) { closestDistSqr = distSqr; closestRefinery = refinery; closestRefineryId = refinery.netId; }
                    }
                }
            }
            if (closestRefinery != null) { Server_TransitionToState(HarvesterState.MovingToRefinery, 0, closestRefineryId); Vector3 dest = closestRefinery.dockingPoint != null ? closestRefinery.dockingPoint.position : closestRefinery.transform.position; movementComponent?.Server_SetDestination(dest); }
            else { Debug.LogWarning($"Harvester {netId} cannot find a refinery! Going Idle."); Server_TransitionToState(HarvesterState.Idle, 0, 0); }
        }

        // --- Target RPCs (Called By Server, Run on Owning Client) ---
        [TargetRpc]
        public void Target_AssignTaskFailed(string reason)
        {
            Debug.LogWarning($"Harvester {netId} task failed: {reason}");
            GoToIdleStateLocally("Task failed");
        }
        // Target_DepositComplete behövs inte om servern sköter allt direkt

        // --- Client-Side Helper Methods ---
        private IEnumerator FindTargetCrystalLocally(uint targetNetId)
        {
            if (targetNetId == 0) yield break; if (NetworkClient.spawned.TryGetValue(targetNetId, out NetworkIdentity identity)) { currentTargetCrystalCache = identity.GetComponent<HarvestableCrystal>(); if (currentTargetCrystalCache != null) yield break; }
            float timeout = Time.time + 5f; while (Time.time < timeout && currentTargetCrystalCache == null) { if (NetworkClient.spawned.TryGetValue(targetNetId, out identity)) { currentTargetCrystalCache = identity.GetComponent<HarvestableCrystal>(); } if (currentTargetCrystalCache == null) yield return null; }
        }
        private IEnumerator FindTargetRefineryLocally(uint targetNetId)
        {
            if (targetNetId == 0) yield break; if (NetworkClient.spawned.TryGetValue(targetNetId, out NetworkIdentity identity)) { currentTargetRefineryCache = identity.GetComponent<RefineryBuilding>(); if (currentTargetRefineryCache != null) yield break; }
            float timeout = Time.time + 5f; while (Time.time < timeout && currentTargetRefineryCache == null) { if (NetworkClient.spawned.TryGetValue(targetNetId, out identity)) { currentTargetRefineryCache = identity.GetComponent<RefineryBuilding>(); } if (currentTargetRefineryCache == null) yield return null; }
        }
        private void GoToIdleStateLocally(string reason)
        {
            currentTargetCrystalCache = null; currentTargetRefineryCache = null;
            // Animation uppdateras via hook
        }

    } // End of class HarvesterUnit
} // End of namespace RTSGAME