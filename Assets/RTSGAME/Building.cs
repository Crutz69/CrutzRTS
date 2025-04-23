// Assets/RTSGAME/Scripts/Buildings/Building.cs
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using System.Collections.Generic;
using System.Collections;
using Mirror;

namespace RTSGAME
{
    public enum BuildingState { Ghost, Placing, Constructing, Operational, Disabled_NoPower, BeingCaptured, Destroyed }

    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(Health))]
    public abstract class Building : NetworkBehaviour
    {
        [Header("Building Identification")]
        [SerializeField] private string buildingName = "Default Building";
        [Tooltip("Faction this building originally belonged to. Determines tech/units.")]
        [SyncVar(hook = nameof(OnFactionChanged))]
        public int originalFactionID = 0;

        [Header("Core Stats")]
        [SerializeField] private int costCredits = 100;
        [SerializeField] private int manaUpkeep = 5;
        [SerializeField] private int manaGeneration = 0;
        [SerializeField][Range(0f, 1f)] private float sellReturnPercentage = 0.7f;
        [SerializeField] private int requiredTier = 1;

        [Header("Combat Stats")] // Lade till header för ArmorType
        [SerializeField] private ArmorType armorType = ArmorType.Fortified; // Byggnader är ofta Fortified
        public ArmorType ArmorType => armorType; // Gör den läsbar utifrån

        [Header("Construction")]
        [SerializeField] private float constructionDuration = 10f;
        [SerializeField] private int maxConcurrentBuilders = 1;
        private readonly SyncList<uint> currentBuilderNetIds = new SyncList<uint>();
        [SyncVar(hook = nameof(OnConstructionProgressChanged))]
        private float constructionProgress = 0f;

        [Header("Capture")]
        [SerializeField] private float captureDuration = 10.0f;
        [SyncVar(hook = nameof(OnCaptureStateChanged))]
        private bool isBeingCaptured = false;
        [SyncVar] private uint capturingWorkerNetId = 0;
        [SyncVar(hook = nameof(OnCaptureProgressChanged))] private float captureProgress = 0f;
        private Coroutine captureCoroutine = null;

        [Header("Gameplay")]
        [SerializeField] private float visionRadius = 15f;

        [Header("Ownership & State")]
        [SyncVar(hook = nameof(OnOwnerChanged))] private uint ownerNetId = 0;
        [SyncVar(hook = nameof(OnCurrentStateChanged))] private BuildingState currentState = BuildingState.Ghost;
        private bool isPowered => currentState == BuildingState.Operational;

        [Header("Component References")]
        [SerializeField] public Health healthComponent; // Public för att andra ska kunna komma åt (t.ex. Worker)

        [Header("UI & Visuals")]
        [SerializeField] private Slider healthBarSlider;
        [SerializeField] private GameObject selectionIndicator;
        [SerializeField] private Slider progressBarSlider;

        [Header("Rally Point")]
        [SerializeField] public GameObject rallyPointVisualPrefab; // Public om externa script ska komma åt?
        [SerializeField] public LineRenderer rallyPointLineRenderer; // Public om externa script ska komma åt?
        [SyncVar(hook = nameof(OnRallyPointChanged))] protected Vector3 rallyPointPosition;
        [SyncVar] protected bool hasRallyPoint = false;

        // --- Properties ---
        public string BuildingName => buildingName;
        public float CurrentHealth => healthComponent != null ? healthComponent.CurrentHealth : 0;
        public float MaxHealth => healthComponent != null ? healthComponent.MaxHealth : 1;
        public bool IsDead => healthComponent != null ? healthComponent.IsDead : true;
        public int CostCredits => costCredits;
        public int ManaUpkeep => manaUpkeep;
        public int ManaGeneration => manaGeneration;
        public int RequiredTier => requiredTier;
        public uint OwnerNetId => ownerNetId;
        public BuildingState CurrentState => currentState;
        public bool IsPowered => isPowered;
        public float ConstructionProgress => constructionProgress;
        public bool NeedsConstruction => currentState == BuildingState.Constructing || currentState == BuildingState.Placing;
        public bool CanAssignBuilder => (currentState == BuildingState.Constructing || currentState == BuildingState.Placing) && currentBuilderNetIds.Count < maxConcurrentBuilders;
        public int AssignedBuilderCount => currentBuilderNetIds.Count;
        public int MaxConcurrentBuilders => maxConcurrentBuilders;
        public float VisionRadius => visionRadius;
        public bool HasRallyPoint => hasRallyPoint;
        public bool IsBeingCaptured => isBeingCaptured;
        public float CaptureProgress => captureProgress;
        public float CaptureDuration => captureDuration;
        public uint CapturingWorkerNetId => capturingWorkerNetId;

        // --- Events ---
        [Header("Events")]
        public UnityEvent OnBuildingPlaced_Local;
        public UnityEvent OnConstructionStart_Local;
        public UnityEvent OnConstructionProgress_Local;
        public UnityEvent OnConstructionComplete_Local;
        public UnityEvent OnSold_Local;
        public UnityEvent OnPowerStateChanged_Local;
        public UnityEvent OnSelected_Local;
        public UnityEvent OnDeselected_Local;
        public UnityEvent OnRallyPointSet_Local;
        public UnityEvent OnCaptureStart_Local;
        public UnityEvent OnCaptureComplete_Local;
        public UnityEvent OnCaptureCancel_Local;

        // --- Unity Methods & Mirror Callbacks ---
        protected virtual void Awake()
        {
            if (healthComponent == null) healthComponent = GetComponent<Health>();
            if (healthComponent == null) Debug.LogError($"Building {gameObject.name} is missing Health component!", this);
            if (selectionIndicator) selectionIndicator.SetActive(false);
            if (rallyPointLineRenderer) rallyPointLineRenderer.enabled = false;
            if (rallyPointVisualPrefab && rallyPointVisualPrefab.activeSelf) rallyPointVisualPrefab.SetActive(false);
            UpdateProgressBar();
        }
        public override void OnStartServer() { base.OnStartServer(); /* Hälsa initieras av Health.cs */ }
        public override void OnStartClient()
        {
            base.OnStartClient();
            OnFactionChanged(0, originalFactionID);
            OnOwnerChanged(0, ownerNetId);
            OnCurrentStateChanged(currentState, currentState);
            OnConstructionProgressChanged(0, constructionProgress);
            OnCaptureStateChanged(false, isBeingCaptured);
            OnCaptureProgressChanged(0, captureProgress);
            OnRallyPointChanged(Vector3.zero, rallyPointPosition);
            if (healthComponent != null) UpdateHealthBarUI(0, healthComponent.CurrentHealth);
        }
        protected virtual void OnDestroy()
        {
            if (isServer && captureCoroutine != null) { StopCoroutine(captureCoroutine); captureCoroutine = null; }
            // TODO: Avregistrera från managers
        }

        // --- SyncVar Hooks ---
        void OnFactionChanged(int oldId, int newId) { /* ... */ }
        void OnConstructionProgressChanged(float oldProgress, float newProgress) { UpdateProgressBar(); OnConstructionProgress_Local?.Invoke(); }
        void OnCaptureStateChanged(bool oldState, bool newState) { UpdateProgressBar(); if (newState) { OnCaptureStart_Local?.Invoke(); } else { if (captureProgress < 1f) { captureProgress = 0f; UpdateProgressBar(); OnCaptureCancel_Local?.Invoke(); } } }
        void OnCaptureProgressChanged(float oldProgress, float newProgress) { UpdateProgressBar(); if (oldProgress < 1f && newProgress >= 1f && !isBeingCaptured) { OnCaptureComplete_Local?.Invoke(); } }
        void OnOwnerChanged(uint oldOwnerNetId, uint newOwnerNetId) { UpdateColorBasedOnOwner(oldOwnerNetId, newOwnerNetId); /* Debug.Log($"{BuildingName} owner changed from {oldOwnerNetId} to {newOwnerNetId}"); */ }
        void OnCurrentStateChanged(BuildingState oldState, BuildingState newState)
        {
            // Debug.Log($"{BuildingName} state changed from {oldState} to {newState}");
            if ((oldState == BuildingState.Operational && newState == BuildingState.Disabled_NoPower) || (oldState == BuildingState.Disabled_NoPower && newState == BuildingState.Operational)) { OnPowerStateChanged_Local?.Invoke(); }
            // OnDestroyed_Local triggas via Health.cs's OnClientDied event nu
            if (oldState == BuildingState.Constructing && newState == BuildingState.Operational) { OnConstructionComplete_Local?.Invoke(); }
            if (oldState == BuildingState.Placing && newState == BuildingState.Constructing) { OnConstructionStart_Local?.Invoke(); }
            UpdateProgressBar();
        }
        void OnRallyPointChanged(Vector3 oldPos, Vector3 newPos) { if (IsSelected()) { UpdateRallyPointVisuals(); } if (hasRallyPoint && oldPos == Vector3.zero && newPos != Vector3.zero) OnRallyPointSet_Local?.Invoke(); }

        // --- Metod för UI-uppdatering av Health Bar (anropas av Health.cs hook) ---
        public virtual void UpdateHealthBarUI(float oldHealth, float newHealth)
        {
            if (healthBarSlider != null && healthComponent != null) { healthBarSlider.value = newHealth / healthComponent.MaxHealth; }
            else if (healthBarSlider != null) { healthBarSlider.value = 0; }
        }

        // --- Server-Side Logic ---
        [Server]
        public void Server_InitializeBuilding(uint ownerId, int factionId, BuildingState initialState = BuildingState.Operational)
        {
            ownerNetId = ownerId; originalFactionID = factionId; healthComponent?.Server_SetInitialHealth(); currentState = initialState;
            if (initialState == BuildingState.Constructing) { constructionProgress = 0f; /* TODO: SetHealthDirectly(1f)? */ }
            else if (initialState == BuildingState.Operational) { constructionProgress = 1f; }
        }
        [Server] public bool Server_AssignBuilder(uint workerNetId) { if (CanAssignBuilder && !currentBuilderNetIds.Contains(workerNetId)) { if (currentBuilderNetIds.Count == 0 && currentState == BuildingState.Placing) { currentState = BuildingState.Constructing; } currentBuilderNetIds.Add(workerNetId); return true; } return false; }
        [Server] public void Server_RemoveBuilder(uint workerNetId) { currentBuilderNetIds.Remove(workerNetId); }
        [Server] public void Server_ContributeConstruction(float workAmount) { if (currentState != BuildingState.Constructing || constructionProgress >= 1f) return; constructionProgress = Mathf.Clamp01(constructionProgress + workAmount / constructionDuration); if (healthComponent != null) { float targetHealth = Mathf.Lerp(1, healthComponent.MaxHealth, constructionProgress); healthComponent.Server_Heal((healthComponent.MaxHealth / constructionDuration) * workAmount * 1.1f); } if (constructionProgress >= 1f) { Server_MarkAsFunctional(); } }
        [Server] private void Server_MarkAsFunctional() { if (currentState == BuildingState.Operational || currentState == BuildingState.Destroyed) return; currentState = BuildingState.Operational; constructionProgress = 1f; if (healthComponent != null) healthComponent.Server_Heal(healthComponent.MaxHealth); List<uint> buildersToNotify = new List<uint>(currentBuilderNetIds); currentBuilderNetIds.Clear(); foreach (uint workerNetId in buildersToNotify) { if (NetworkServer.spawned.TryGetValue(workerNetId, out var id)) id.GetComponent<ConstructionWorker>()?.Target_ConstructionComplete(netIdentity); } }
        [Server] public void Server_SetPoweredState(bool hasPower) { /* ... */ }
        [Server]
        public void Server_Sell(uint sellingPlayerNetId)
        {
            // Använd publika properties för kontroller
            if (IsDead) return;
            if (sellingPlayerNetId != OwnerNetId)
            {
                Debug.LogWarning($"Player {sellingPlayerNetId} tried to sell building owned by {OwnerNetId}. Denied.");
                return;
            }

            // ---- SE TILL ATT DENNA RAD FINNS OCH ÄR KORREKT ----
            // Använd CostCredits property och sellReturnPercentage fältet
            int creditsReturned = Mathf.FloorToInt(CostCredits * sellReturnPercentage);
            // -----------------------------------------------------

            // Ge tillbaka credits via ResourceManager
            ResourceManager.Instance?.Server_AddCredits(OwnerNetId, creditsReturned);

            // Använd publik property för loggning
            Debug.Log($"{BuildingName} sold by player {OwnerNetId} for {creditsReturned} credits.");

            // RpcInformSold(); // Skicka ev RPC för ljudeffekt
            Server_HandleDestruction(true); // Hantera förstörelse (med isSold = true)
        }

        // --- Capture Logic (Server-Side) ---
        [Server] public bool Server_StartCaptureAttempt(NetworkIdentity workerIdentity) { /* ... */ return false; }
        private IEnumerator CaptureTimer(NetworkIdentity workerIdentity, float duration) { yield return null; /* ... */ }
        [Server] public void Server_CancelCaptureAttempt(string reason) { /* ... */ }
        [Server] public void Server_ChangeOwner(uint newOwnerNetId) { /* ... */ } // Public som krävt
        [Server] private void Server_UpdateColorBasedOnOwner() { /* ... */ }
        [ClientRpc] private void RpcUpdateVisualColor(Color newColor) { /* ... */ }

        // --- Rally Point Logic ---
        [Command] public void CmdSetRallyPoint(Vector3 position) { if (IsOwner(connectionToClient)) Server_SetRallyPoint(position); } // Lade till ägarkoll
        [Command] public void CmdClearRallyPoint() { if (IsOwner(connectionToClient)) Server_ClearRallyPoint(); } // Lade till ägarkoll
        [Server] public void Server_SetRallyPoint(Vector3 position) { rallyPointPosition = position; hasRallyPoint = true; } // Public som krävt
        [Server] public void Server_ClearRallyPoint() { hasRallyPoint = false; } // Public som krävt
        public virtual Vector3 GetRallyPointPosition() { return hasRallyPoint ? rallyPointPosition : (transform.position + transform.forward * 5.0f); }
        protected virtual void UpdateRallyPointVisuals() { /* ... */ }
        protected virtual void PositionRallyMarker() { /* ... */ }

        // --- Selection Methods (Klient-sida) ---
        public virtual void Select() { bool isMy = (OwnerNetId != 0 && NetworkClient.active && NetworkClient.localPlayer != null && OwnerNetId == NetworkClient.localPlayer.netId); if (selectionIndicator) selectionIndicator.SetActive(true); if (healthBarSlider) healthBarSlider.gameObject.SetActive(isMy); if (isMy && hasRallyPoint) { if (rallyPointLineRenderer) rallyPointLineRenderer.enabled = true; if (rallyPointVisualPrefab) { PositionRallyMarker(); rallyPointVisualPrefab.SetActive(true); } UpdateRallyPointVisuals(); } UpdateProgressBarVisibility(true); OnSelected_Local?.Invoke(); }
        public virtual void Deselect() { if (selectionIndicator) selectionIndicator.SetActive(false); if (healthBarSlider) healthBarSlider.gameObject.SetActive(false); if (progressBarSlider) progressBarSlider.gameObject.SetActive(false); if (rallyPointLineRenderer) rallyPointLineRenderer.enabled = false; if (rallyPointVisualPrefab) rallyPointVisualPrefab.SetActive(false); OnDeselected_Local?.Invoke(); }

        // --- Protected Helper & Override Methods ---
        [Server] protected virtual void Server_HandleDestruction(bool isSold = false) { /* ... */ }
        private IEnumerator Server_DestroyAfterDelay(float delay) { /* ... */ yield return null; }
        [ClientRpc] private void RpcSetVisualsActive(bool active) { /* ... */ }
        protected void UpdateProgressBar() { /* ... */ }
        protected void UpdateProgressBarVisibility(bool isSelected) { /* ... */ }
        protected bool IsSelected() { return SelectionManager.Instance != null && SelectionManager.Instance.IsSelected(this.gameObject); } // Enkel implementering
        protected void UpdateColorBasedOnOwner(uint oldOwnerNetId, uint newOwnerNetId) { /* ... */ }
        protected virtual void HaltFunctionality() { /* Subclasses implement */ }
        protected virtual void ResumeFunctionality() { /* Subclasses implement */ }

        // Helper för ägarkoll (behövs för Commands) - kan flyttas till basklass om fler kommandon tillkommer?
        // Eller anropa NetworkPlayer som gör kollen? För nu, enkel check här.
        protected bool IsOwner(NetworkConnection conn)
        {
            return conn != null && conn == connectionToClient; // Stämmer detta för byggnader? Beror på hur de spawnas.
                                                               // Alternativt: return conn != null && OwnerNetId != 0 && conn.identity != null && conn.identity.netId == OwnerNetId;
        }

    } // End class Building
} // End namespace RTSGAME