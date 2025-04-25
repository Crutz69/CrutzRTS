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
        [Tooltip("Range to pickup crystals AND deposit at refinery.")]
        [SerializeField] private float interactionRange = 2f;
        [Tooltip("Time in seconds to gather ONE crystal.")]
        [SerializeField] private float gatherDuration = 1.5f;

        // State Machine (specifik för Harvester)
        public enum HarvesterState { Idle, MovingToPosition, MovingToCrystal, Harvesting, MovingToRefinery, Depositing }
        [SyncVar(hook = nameof(OnStateChangedHook))]
        private HarvesterState currentState = HarvesterState.Idle;

        // Inventarie (synkas till klienter för UI/visuella effekter)
        [SyncVar(hook = nameof(OnLoadChangedHook))]
        private int currentLoad = 0;
        [SyncVar(hook = nameof(OnCrystalTypeChangedHook))]
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

        public override void OnStartServer() {
            base.OnStartServer();
            currentState = HarvesterState.Idle;
            currentLoad = 0;
            carriedCrystalType = CrystalType.None;
            targetCrystalNetId = 0;
            targetRefineryNetId = 0;
            if (queryResults == null || queryResults.Length != maxQueryColliders) { queryResults = new Collider[maxQueryColliders]; }
            // Server_FindWork(); // Anropa inte automatiskt här, låt RTSNetworkManager göra det efter spawn
        }

        public override void OnStartClient() {
            base.OnStartClient();
            OnStateChangedHook(currentState, currentState);
            OnLoadChangedHook(0, currentLoad);
            OnCrystalTypeChangedHook(CrystalType.None, carriedCrystalType);
            if(targetCrystalNetId != 0) StartCoroutine(FindTargetCrystalLocally(targetCrystalNetId));
            if(targetRefineryNetId != 0) StartCoroutine(FindTargetRefineryLocally(targetRefineryNetId));
        }


        // --- SyncVar Hooks (Client-side) ---

        void OnStateChangedHook(HarvesterState oldState, HarvesterState newState) {
            if (newState == HarvesterState.Idle) { currentTargetCrystalCache = null; currentTargetRefineryCache = null; }
            UpdateAnimationHarvester(oldState, newState);
        }
        void OnLoadChangedHook(int oldLoad, int newLoad) { UpdateInventoryBarUI(newLoad, carriedCrystalType); }
        void OnCrystalTypeChangedHook(CrystalType oldType, CrystalType newType) { UpdateInventoryBarUI(currentLoad, newType); }

        // --- Animation & UI (Client-side) ---

        void UpdateAnimationHarvester(HarvesterState oldState, HarvesterState newState) {
            if (animator == null) return;
            bool isMoving = (newState == HarvesterState.MovingToCrystal || newState == HarvesterState.MovingToRefinery || newState == HarvesterState.MovingToPosition);
            animator.SetBool("IsMoving", isMoving);
            animator.SetBool("IsHarvesting", newState == HarvesterState.Harvesting);
        }
        void UpdateInventoryBarUI(int load, CrystalType type) {
            if (inventoryBarSlider == null || inventoryBarCanvasGO == null) return;
            bool showBar = (load > 0);
            inventoryBarCanvasGO.SetActive(showBar);
            if (!showBar) return;
            inventoryBarSlider.value = (carryCapacity > 0) ? Mathf.Clamp01((float)load / carryCapacity) : 0f;
            if (inventoryBarFillImage != null) { inventoryBarFillImage.color = GetCrystalColor(type); }
        }
        Color GetCrystalColor(CrystalType type) {
             switch (type) { case CrystalType.Green: return Color.green; case CrystalType.Blue: return Color.blue; case CrystalType.Red: return Color.red; default: return Color.grey;}
        }

        // --- Commands (Called by Local Player via NetworkPlayer, Run on Server) ---

        [Command]
        public void Cmd_OrderHarvest(NetworkIdentity resourceIdentity) {
             if (!IsOwner(connectionToClient)) return;
             if (resourceIdentity == null) return;
             HarvestableCrystal targetCrystal = resourceIdentity.GetComponent<HarvestableCrystal>();
             if(targetCrystal == null) return;
             if(currentLoad >= carryCapacity) { Server_FindRefineryAndMove(); return; }
             if (targetCrystal.Server_TryReserve(this.netId)) { Server_TransitionToState(HarvesterState.MovingToCrystal, resourceIdentity.netId, 0); movementComponent?.Server_SetDestination(targetCrystal.transform.position); }
             else { Target_AssignTaskFailed("Crystal is already targeted."); Server_FindWork(); }
        }
        [Command]
        public void Cmd_OrderDeposit(NetworkIdentity refineryIdentity) {
             if (!IsOwner(connectionToClient)) return;
             if (refineryIdentity == null) return;
              RefineryBuilding targetRefinery = refineryIdentity.GetComponent<RefineryBuilding>();
               if(targetRefinery == null) return;
               if (currentLoad <= 0) return;
                // if(targetRefinery.OwnerNetId != this.ownerNetId) return;
                Server_TransitionToState(HarvesterState.MovingToRefinery, 0, refineryIdentity.netId);
                 Vector3 dest = targetRefinery.dockingPoint != null ? targetRefinery.dockingPoint.position : targetRefinery.transform.position;
                 movementComponent?.Server_SetDestination(dest);
        }
         [Command] public void Cmd_GoToIdle() { if (!IsOwner(connectionToClient)) return; Server_TransitionToState(HarvesterState.Idle, 0, 0); }
         [Command] public void Cmd_MoveToPosition(Vector3 destination) { if (!IsOwner(connectionToClient)) return; Server_TransitionToState(HarvesterState.MovingToPosition, 0, 0); movementComponent?.Server_SetDestination(destination); }


        // --- Server-Side Logic ---

        [Server]
        private void Server_TransitionToState(HarvesterState newState, uint crystalTargetId, uint refineryTargetId) {
             HarvesterState oldState = currentState;
             bool shouldRelease = (oldState == HarvesterState.MovingToCrystal || oldState == HarvesterState.Harvesting);
             Server_StopCurrentWorkCoroutine(shouldRelease);
             currentState = newState; targetCrystalNetId = crystalTargetId; targetRefineryNetId = refineryTargetId; currentTargetCrystalCache = null; currentTargetRefineryCache = null;
             if(targetCrystalNetId != 0 && NetworkServer.spawned.TryGetValue(targetCrystalNetId, out var id1)) currentTargetCrystalCache = id1.GetComponent<HarvestableCrystal>();
             if(targetRefineryNetId != 0 && NetworkServer.spawned.TryGetValue(targetRefineryNetId, out var id2)) currentTargetRefineryCache = id2.GetComponent<RefineryBuilding>();
             if (newState == HarvesterState.Idle || newState == HarvesterState.MovingToPosition) { movementComponent?.Server_StopMovement(); }
             if (newState == HarvesterState.Idle && oldState != HarvesterState.Idle) { StartCoroutine(Server_DelayedFindWork(0.2f)); }
        }
        [Server]
        private void Server_StopCurrentWorkCoroutine(bool releaseCrystal) {
             if (server_workCoroutine != null) { StopCoroutine(server_workCoroutine); server_workCoroutine = null; }
             // Använd cache för att undvika GetComponent här om möjligt
              HarvestableCrystal crystalToRelease = currentTargetCrystalCache;
              if(crystalToRelease == null && targetCrystalNetId != 0) { // Försök hitta om cache var null
                   if(NetworkServer.spawned.TryGetValue(targetCrystalNetId, out var identity)) crystalToRelease = identity.GetComponent<HarvestableCrystal>();
              }
              if(releaseCrystal && crystalToRelease != null) { crystalToRelease.Server_TryRelease(this.netId); }
        }
        [Server]
        public override void OnMovementArrival() {
             Debug.Log($"[Server] Harvester {netId} arrived. State: {currentState}");
               switch(currentState) {
                    case HarvesterState.MovingToCrystal: Server_StartGathering(); break;
                    case HarvesterState.MovingToRefinery: Server_AttemptDeposit(); break;
                    case HarvesterState.MovingToPosition: Server_TransitionToState(HarvesterState.Idle, 0, 0); break;
                    default: movementComponent?.Server_StopMovement(); break;
               }
        }
        [Server]
        private void Server_StartGathering() {
            if (targetCrystalNetId == 0 || !NetworkServer.spawned.TryGetValue(targetCrystalNetId, out var id)) { Server_FindWork(); return; } // Hitta nytt jobb om mål borta
            currentTargetCrystalCache = id?.GetComponent<HarvestableCrystal>();
            if (currentTargetCrystalCache == null) { Server_FindWork(); return; } // Hitta nytt jobb om mål borta
            if (currentTargetCrystalCache.TargetedByNetId != this.netId) { Debug.LogWarning($"Harvester {netId} lost reservation for {targetCrystalNetId}. Finding new work."); Server_FindWork(); return; }
            float distance = Vector3.Distance(transform.position, currentTargetCrystalCache.transform.position);
            if (distance > interactionRange * 1.1f) { Debug.LogWarning($"Harvester {netId} too far ({distance:F1}m > {interactionRange}) to gather {targetCrystalNetId}. Moving closer."); movementComponent?.Server_SetDestination(currentTargetCrystalCache.transform.position); return; } // Försök flytta närmare, state är fortfarande MovingToCrystal

            Server_TransitionToState(HarvesterState.Harvesting, targetCrystalNetId, 0);
            movementComponent?.Server_StopMovement();
            server_workCoroutine = StartCoroutine(HarvestTimer());
        }

        // Server Coroutine (Inkluderar extra loggning och kontroller)
        [Server]
        private IEnumerator HarvestTimer() {
            HarvestableCrystal targetOnStart = currentTargetCrystalCache;
            if (targetOnStart == null) { Debug.LogError($"Harvester {netId} HarvestTimer started with null target cache!"); Server_TransitionToState(HarvesterState.Idle, 0, 0); server_workCoroutine = null; yield break; }
            uint harvestingTargetId = targetOnStart.netId;
            Debug.Log($"[Server] Harvester {netId} starting harvest timer ({gatherDuration}s) for {harvestingTargetId}");
            yield return new WaitForSeconds(gatherDuration);

            // --- Efter väntan ---
             Debug.Log($"[Server] Harvester {netId} finished waiting for harvest timer ({harvestingTargetId}). Current State: {currentState}, Target ID: {targetCrystalNetId}");

            // Kolla om vi fortfarande är i Harvesting state OCH om målet fortfarande är detsamma
            if (currentState == HarvesterState.Harvesting && targetCrystalNetId == harvestingTargetId) {
                HarvestableCrystal finalTargetCrystal = null;
                NetworkIdentity targetIdentity = null;
                if (NetworkServer.spawned.TryGetValue(harvestingTargetId, out targetIdentity) && targetIdentity != null) { finalTargetCrystal = targetIdentity.GetComponent<HarvestableCrystal>(); }

                Debug.Log($"[Server] HarvestTimer resumed ({harvestingTargetId}). Found Target Identity: {targetIdentity != null}. Found Crystal Script: {finalTargetCrystal != null}");

                // Kolla om kristallen finns OCH om den fortfarande är reserverad av OSS
                if (finalTargetCrystal != null && finalTargetCrystal.TargetedByNetId == this.netId) {
                     Debug.Log($"[Server] HarvestTimer completing gather for {harvestingTargetId}.");
                     Server_CompleteGathering(finalTargetCrystal);
                } else {
                     if (finalTargetCrystal == null) Debug.LogWarning($"Harvester {netId} target crystal {harvestingTargetId} was destroyed during harvest timer.");
                     else Debug.LogWarning($"Harvester {netId} lost reservation for crystal {harvestingTargetId} (Current target: {finalTargetCrystal.TargetedByNetId}) during harvest timer.");
                     Server_FindWork(); // Hitta nytt jobb
                }
            } else { Debug.Log($"[Server] Harvester {netId} state ({currentState}) or target ({targetCrystalNetId}) changed during harvest timer for {harvestingTargetId}. Aborting completion."); }
             server_workCoroutine = null;
        }

        [Server]
        private void Server_CompleteGathering(HarvestableCrystal crystal) {
              if (crystal == null) { Debug.LogError("Server_CompleteGathering called with null crystal!"); return; } // Extra null check
               CrystalType gatheredType = crystal.crystalType;
               if (currentLoad < carryCapacity && (carriedCrystalType == CrystalType.None || carriedCrystalType == gatheredType)) {
                    if (carriedCrystalType == CrystalType.None) { carriedCrystalType = gatheredType; }
                    currentLoad++;
                     Debug.Log($"[Server] Harvester {netId} gathered crystal {crystal.netId}. Load: {currentLoad}/{carryCapacity}. Type: {carriedCrystalType}");
                     crystal.Server_HarvestComplete(); // Ber kristallen förstöra sig
                     currentTargetCrystalCache = null; targetCrystalNetId = 0; // Rensa mål direkt
                     Server_FindWork(); // Hitta nästa jobb
               } else {
                      Debug.LogWarning($"Harvester {netId} could not gather {crystal.netId}. Load:{currentLoad}/{carryCapacity} Type:{carriedCrystalType} vs {gatheredType}");
                       crystal.Server_TryRelease(this.netId); // Släpp reservationen korrekt
                       Server_FindWork(); // Hitta annat jobb
               }
        }

        [Server]
        private void Server_AttemptDeposit() {
               if(targetRefineryNetId == 0 || !NetworkServer.spawned.TryGetValue(targetRefineryNetId, out var id)) { Server_FindWork(); return; } // Hitta jobb om mål borta
               currentTargetRefineryCache = id?.GetComponent<RefineryBuilding>();
               if(currentTargetRefineryCache == null) { Server_FindWork(); return; } // Hitta jobb om mål borta

                 Vector3 targetPos = currentTargetRefineryCache.dockingPoint != null ? currentTargetRefineryCache.dockingPoint.position : currentTargetRefineryCache.transform.position;
                 float distance = Vector3.Distance(transform.position, targetPos);
                  if(distance > interactionRange * 1.1f) { Debug.LogWarning($"Harvester {netId} too far to deposit."); Server_TransitionToState(HarvesterState.MovingToRefinery, 0, targetRefineryNetId); movementComponent?.Server_SetDestination(targetPos); return; }

                   bool accepted = currentTargetRefineryCache.Server_RequestDeposit(this.netIdentity, currentLoad, carriedCrystalType);
                   if(accepted) { Server_TransitionToState(HarvesterState.Depositing, 0, targetRefineryNetId); movementComponent?.Server_StopMovement(); }
                   else { Debug.Log($"Deposit denied by refinery {targetRefineryNetId}. Finding new work."); Server_FindWork(); } // Hitta nytt jobb om denied
        }

        [Server]
        public void Server_AcknowledgeDepositComplete() {
              // Debug.Log($"Harvester {netId} deposit acknowledged by server.");
               if(currentState == HarvesterState.Depositing) {
                    currentLoad = 0;
                    carriedCrystalType = CrystalType.None;
                     Server_FindWork(); // Hitta nytt jobb direkt efter acknowledge
               }
        }

        // Server Coroutine för fördröjd jobbsökning
         [Server]
         private IEnumerator Server_DelayedFindWork(float delay) {
              yield return new WaitForSeconds(delay);
              if (currentState == HarvesterState.Idle) { Server_FindWork(); }
         }

        // Server-metod för att hitta jobb (kristall eller refinery)
        [Server]
        public void Server_FindWork() {
             Server_StopCurrentWorkCoroutine(true);
             if (currentLoad >= carryCapacity) { Server_FindRefineryAndMove(); return; }

              HarvestableCrystal targetCrystal = null;
              if (carriedCrystalType != CrystalType.None) { targetCrystal = Server_FindClosestAvailableCrystalOfType(carriedCrystalType); }
              if (targetCrystal == null) {
                   if (currentLoad > 0) { Server_FindRefineryAndMove(); return; }
                   carriedCrystalType = CrystalType.None; currentLoad = 0;
                   targetCrystal = Server_FindClosestAvailableCrystal();
              }
              if(targetCrystal != null) {
                   if(targetCrystal.Server_TryReserve(this.netId)) { Server_TransitionToState(HarvesterState.MovingToCrystal, targetCrystal.netId, 0); movementComponent?.Server_SetDestination(targetCrystal.transform.position); }
                   else { Server_TransitionToState(HarvesterState.Idle, 0, 0); } // Race condition, går Idle, triggar DelayedFindWork
              } else { Server_TransitionToState(HarvesterState.Idle, 0, 0); } // Inget jobb alls, går Idle, triggar DelayedFindWork
        }

        // --- Server Find Methods (Using Physics Query) ---
        [Server]
        private HarvestableCrystal Server_FindClosestAvailableCrystal() { return FindClosestCrystalInternal(CrystalType.None); }
        [Server]
        private HarvestableCrystal Server_FindClosestAvailableCrystalOfType(CrystalType type) { return FindClosestCrystalInternal(type); }
        [Server]
        private HarvestableCrystal FindClosestCrystalInternal(CrystalType requiredType) {
            if (queryResults == null) queryResults = new Collider[maxQueryColliders];
            int hitCount = Physics.OverlapSphereNonAlloc(transform.position, initialSearchRadius, queryResults, resourceLayerMask);
            HarvestableCrystal closestCrystal = null; float closestDistSqr = float.MaxValue;
            for (int i = 0; i < hitCount; i++) {
                if (queryResults[i].TryGetComponent<HarvestableCrystal>(out HarvestableCrystal crystal)) {
                    if (crystal.Server_IsAvailable() && (requiredType == CrystalType.None || crystal.crystalType == requiredType)) {
                        float distSqr = (crystal.transform.position - transform.position).sqrMagnitude;
                        if (distSqr < closestDistSqr) { closestDistSqr = distSqr; closestCrystal = crystal; } } } }
             // if(closestCrystal == null) Debug.Log($"FindClosestCrystalInternal({requiredType}) found nothing."); // Debug
            return closestCrystal;
        }
        [Server]
        private void Server_FindRefineryAndMove() {
             if (queryResults == null) queryResults = new Collider[maxQueryColliders];
             int hitCount = Physics.OverlapSphereNonAlloc(transform.position, initialSearchRadius * 2, queryResults, refineryLayerMask);
             RefineryBuilding closestRefinery = null; float closestDistSqr = float.MaxValue; uint closestRefineryId = 0;
             for (int i = 0; i < hitCount; i++) {
                  if (queryResults[i].TryGetComponent<RefineryBuilding>(out RefineryBuilding refinery)) {
                       if (refinery.OwnerNetId == this.ownerNetId) { // Måste vara vårt eget?
                            float distSqr = (refinery.transform.position - transform.position).sqrMagnitude;
                            if (distSqr < closestDistSqr) { closestDistSqr = distSqr; closestRefinery = refinery; closestRefineryId = refinery.netId; } } } }
             if (closestRefinery != null) { Server_TransitionToState(HarvesterState.MovingToRefinery, 0, closestRefineryId); Vector3 dest = closestRefinery.dockingPoint != null ? closestRefinery.dockingPoint.position : closestRefinery.transform.position; movementComponent?.Server_SetDestination(dest); }
             else { Debug.LogWarning($"Harvester {netId} cannot find a refinery! Going Idle."); Server_TransitionToState(HarvesterState.Idle, 0, 0); }
        }

        // --- Target RPCs (Called By Server, Run on Owning Client) ---
        [TargetRpc]
        public void Target_AssignTaskFailed(string reason) {
             Debug.LogWarning($"Harvester {netId} task failed: {reason}");
              GoToIdleStateLocally("Task failed");
              // TODO: Visa meddelande till spelaren i UI?
        }

        // --- Client-Side Helper Methods ---
        private IEnumerator FindTargetCrystalLocally(uint targetNetId) {
             if (targetNetId == 0) yield break; if (NetworkClient.spawned.TryGetValue(targetNetId, out NetworkIdentity identity)) { currentTargetCrystalCache = identity?.GetComponent<HarvestableCrystal>(); if (currentTargetCrystalCache != null) yield break; }
             float timeout = Time.time + 5f; while (Time.time < timeout && currentTargetCrystalCache == null) { if (NetworkClient.spawned.TryGetValue(targetNetId, out identity)) { currentTargetCrystalCache = identity?.GetComponent<HarvestableCrystal>(); } if (currentTargetCrystalCache == null) yield return null; }
        }
        private IEnumerator FindTargetRefineryLocally(uint targetNetId) {
             if (targetNetId == 0) yield break; if (NetworkClient.spawned.TryGetValue(targetNetId, out NetworkIdentity identity)) { currentTargetRefineryCache = identity?.GetComponent<RefineryBuilding>(); if (currentTargetRefineryCache != null) yield break; }
             float timeout = Time.time + 5f; while (Time.time < timeout && currentTargetRefineryCache == null) { if (NetworkClient.spawned.TryGetValue(targetNetId, out identity)) { currentTargetRefineryCache = identity?.GetComponent<RefineryBuilding>(); } if (currentTargetRefineryCache == null) yield return null; }
        }
        private void GoToIdleStateLocally(string reason) {
             // Debug.Log($"Harvester {netId} going idle locally. Reason: {reason}");
             currentTargetCrystalCache = null; currentTargetRefineryCache = null;
             // Låt SyncVar hooken sköta animation
        }


    } // End of class HarvesterUnit
} // End of namespace RTSGAME