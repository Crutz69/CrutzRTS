// Assets/RTSGAME/Scripts/Buildings/Building.cs
using UnityEngine;
using UnityEngine.UI;      // För Slider
using UnityEngine.Events;
using System.Collections.Generic; // För lista med byggare
using System.Collections; // För Coroutine
using Mirror;

namespace RTSGAME
{
    public enum BuildingState { Ghost, Placing, Constructing, Operational, Disabled_NoPower, BeingCaptured, Destroyed }

    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(Health))] // <--- VIKTIGT: Kräver Health-komponent
    public abstract class Building : NetworkBehaviour
    {
        [Header("Building Identification")]
        [SerializeField] private string buildingName = "Default Building";
        [Tooltip("Faction this building originally belonged to. Determines tech/units.")]
        [SyncVar(hook = nameof(OnFactionChanged))]
        public int originalFactionID = 0;

        [Header("Core Stats")]
        // maxHealth finns nu i Health.cs
        [SerializeField] private int costCredits = 100;
        [Tooltip("Positive value means consumption, negative means generation (alternative).")]
        [SerializeField] private int manaUpkeep = 5;
        [Tooltip("Positive value means generation (e.g., Power Plant). Used alongside Upkeep.")]
        [SerializeField] private int manaGeneration = 0;
        [SerializeField][Range(0f, 1f)] private float sellReturnPercentage = 0.7f;
        [SerializeField] private int requiredTier = 1;

        [Header("Construction")]
        [SerializeField] private float constructionDuration = 10f;
        [Tooltip("Max number of workers that can construct this building simultaneously.")]
        [SerializeField] private int maxConcurrentBuilders = 1;
        private readonly SyncList<uint> currentBuilderNetIds = new SyncList<uint>();
        [SyncVar(hook = nameof(OnConstructionProgressChanged))]
        private float constructionProgress = 0f;

        [Header("Capture")]
        [Tooltip("Time in seconds it takes for ONE worker to capture this building.")]
        [SerializeField] private float captureDuration = 10.0f;
        [SyncVar(hook = nameof(OnCaptureStateChanged))]
        private bool isBeingCaptured = false;
        [SyncVar]
        private uint capturingWorkerNetId = 0;
        [SyncVar(hook = nameof(OnCaptureProgressChanged))]
        private float captureProgress = 0f;
        private Coroutine captureCoroutine = null;

        [Header("Gameplay")]
        [Tooltip("The range this building provides vision.")]
        [SerializeField] private float visionRadius = 15f;

        [Header("Ownership & State")]
        [SyncVar(hook = nameof(OnOwnerChanged))]
        private uint ownerNetId = 0;
        [SyncVar(hook = nameof(OnCurrentStateChanged))]
        private BuildingState currentState = BuildingState.Ghost;
        private bool isPowered => currentState == BuildingState.Operational;

        [Header("Component References")] // Referens till Health-komponenten
        [SerializeField] public Health healthComponent; // <--- VIKTIGT: Referens till Health

        [Header("UI & Visuals")]
        [SerializeField] private Slider healthBarSlider;
        [SerializeField] private GameObject selectionIndicator;
        [SerializeField] private Slider progressBarSlider;

        [Header("Rally Point")]
        [SerializeField] private GameObject rallyPointVisualPrefab;
        [SerializeField] private LineRenderer rallyPointLineRenderer;
        [SyncVar(hook = nameof(OnRallyPointChanged))]
        protected Vector3 rallyPointPosition;
        [SyncVar] protected bool hasRallyPoint = false;

        // --- Properties ---
        public string BuildingName => buildingName;
        // Hälsa nås via healthComponent nu
        public float CurrentHealth => healthComponent != null ? healthComponent.CurrentHealth : 0;
        public float MaxHealth => healthComponent != null ? healthComponent.MaxHealth : 1;
        public bool IsDead => healthComponent != null ? healthComponent.IsDead : true;
        // Public int MaxHealth { get { /* Tas bort - finns i Health.cs */ } } // Tas bort
        public int CostCredits => costCredits;
        public int ManaUpkeep => manaUpkeep;
        public int ManaGeneration => manaGeneration;
        public int RequiredTier => requiredTier;
        public uint OwnerNetId => ownerNetId;
        // Public float CurrentHealth { get { /* Tas bort - finns i Health.cs */ } } // Tas bort
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
        // OnDamaged / OnDied hanteras av Health.cs's events (OnClientDamaged, OnClientDied)
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
            // Hitta Health komponenten
            if (healthComponent == null) healthComponent = GetComponent<Health>();
            if (healthComponent == null) Debug.LogError($"Building {gameObject.name} is missing Health component!", this); // Säkerställ att den finns

            if (selectionIndicator) selectionIndicator.SetActive(false);
            if (rallyPointLineRenderer) rallyPointLineRenderer.enabled = false;
            if (rallyPointVisualPrefab && rallyPointVisualPrefab.activeSelf) rallyPointVisualPrefab.SetActive(false);
            UpdateProgressBar();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            // Hälsa initieras av Health.cs's OnStartServer
        }

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
            // Uppdatera health bar initialt via healthComponent
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
        void OnOwnerChanged(uint oldOwnerNetId, uint newOwnerNetId) { UpdateColorBasedOnOwner(oldOwnerNetId, newOwnerNetId); Debug.Log($"{buildingName} owner changed from {oldOwnerNetId} to {newOwnerNetId}"); }
        void OnCurrentStateChanged(BuildingState oldState, BuildingState newState)
        {
            Debug.Log($"{buildingName} state changed from {oldState} to {newState}");
            if ((oldState == BuildingState.Operational && newState == BuildingState.Disabled_NoPower) || (oldState == BuildingState.Disabled_NoPower && newState == BuildingState.Operational)) { OnPowerStateChanged_Local?.Invoke(); }
            // OnDestroyed_Local triggas via Health.cs's OnClientDied event nu
            if (oldState == BuildingState.Constructing && newState == BuildingState.Operational) { OnConstructionComplete_Local?.Invoke(); }
            if (oldState == BuildingState.Placing && newState == BuildingState.Constructing) { OnConstructionStart_Local?.Invoke(); }
            // UpdateHealthBarUI(0, CurrentHealth); // Anropas av Health hook
            UpdateProgressBar();
        }
        // void OnHealthChanged(...) // Tas bort - Hanteras av Health.cs
        void OnRallyPointChanged(Vector3 oldPos, Vector3 newPos) { if (IsSelected()) { UpdateRallyPointVisuals(); } if (hasRallyPoint && oldPos == Vector3.zero && newPos != Vector3.zero) OnRallyPointSet_Local?.Invoke(); }


        // --- Metod för UI-uppdatering av Health Bar (anropas av Health.cs hook) ---
        public virtual void UpdateHealthBarUI(float oldHealth, float newHealth)
        {
            if (healthBarSlider != null && healthComponent != null)
            {
                healthBarSlider.value = newHealth / healthComponent.MaxHealth;
            }
            else if (healthBarSlider != null) { healthBarSlider.value = 0; }
        }

        // --- Server-Side Logic ---
        [Server]
        public void Server_InitializeBuilding(uint ownerId, int factionId, BuildingState initialState = BuildingState.Operational)
        {
            ownerNetId = ownerId;
            originalFactionID = factionId;
            healthComponent?.Server_SetInitialHealth(); // Initiera via Health
            currentState = initialState;
            if (initialState == BuildingState.Constructing)
            {
                constructionProgress = 0f;
                // healthComponent?.Server_SetHealthDirectly(1f); // Health behöver metod för detta?
            }
            else if (initialState == BuildingState.Operational)
            {
                constructionProgress = 1f;
            }
        }

        [Server]
        public bool Server_AssignBuilder(uint workerNetId)
        {
            if (CanAssignBuilder && !currentBuilderNetIds.Contains(workerNetId))
            {
                if (currentBuilderNetIds.Count == 0 && currentState == BuildingState.Placing) { currentState = BuildingState.Constructing; }
                currentBuilderNetIds.Add(workerNetId);
                return true;
            }
            return false;
        }
        [Server] public void Server_RemoveBuilder(uint workerNetId) { currentBuilderNetIds.Remove(workerNetId); }

        [Server]
        public void Server_ContributeConstruction(float workAmount)
        {
            if (currentState != BuildingState.Constructing || constructionProgress >= 1f) return;
            constructionProgress += workAmount / constructionDuration;
            constructionProgress = Mathf.Clamp01(constructionProgress);
            // Uppdatera hälsa proportionellt (via Health komponenten)
            if (healthComponent != null)
            {
                float targetHealth = Mathf.Lerp(1, healthComponent.MaxHealth, constructionProgress);
                // healthComponent.Server_SetHealthDirectly(targetHealth); // Health behöver metod?
                // Alternativt, hela bara lite:
                healthComponent.Server_Heal((healthComponent.MaxHealth / constructionDuration) * workAmount * 1.1f); // Exempel: Hela lite snabbare än bygget
            }
            if (constructionProgress >= 1f) { Server_MarkAsFunctional(); }
        }

        [Server]
        private void Server_MarkAsFunctional()
        {
            if (currentState == BuildingState.Operational || currentState == BuildingState.Destroyed) return;
            currentState = BuildingState.Operational;
            constructionProgress = 1f;
            if (healthComponent != null) healthComponent.Server_Heal(healthComponent.MaxHealth); // Full hälsa

            List<uint> buildersToNotify = new List<uint>(currentBuilderNetIds);
            currentBuilderNetIds.Clear();
            foreach (uint workerNetId in buildersToNotify) { /* ... meddela worker via TargetRpc ... */ }
        }

        [Server] public void Server_SetPoweredState(bool hasPower) { /* ... som tidigare ... */ }

        // Server_TakeDamage & Server_Repair finns nu i Health.cs!

        [Server] public void Server_Sell(uint sellingPlayerNetId) { /* ... som tidigare ... */ }

        // --- Capture Logic (Server-Side) ---
        [Server] public bool Server_StartCaptureAttempt(NetworkIdentity workerIdentity) { /* ... som tidigare ... */ return false; }
        private IEnumerator CaptureTimer(NetworkIdentity workerIdentity, float duration) { /* ... som tidigare ... */ yield return null; }
        [Server] public void Server_CancelCaptureAttempt(string reason) { /* ... som tidigare ... */ }
        [Server] private void Server_ChangeOwner(uint newOwnerNetId) { /* ... som tidigare ... */ }
        [Server] private void Server_UpdateColorBasedOnOwner() { /* ... som tidigare ... */ }
        [ClientRpc] private void RpcUpdateVisualColor(Color newColor) { /* ... som tidigare ... */ }

        // --- Rally Point Logic ---
        [Command] public void CmdSetRallyPoint(Vector3 position) { Server_SetRallyPoint(position); }
        [Command] public void CmdClearRallyPoint() { Server_ClearRallyPoint(); }
        [Server] private void Server_SetRallyPoint(Vector3 position) { rallyPointPosition = position; hasRallyPoint = true; }
        [Server] private void Server_ClearRallyPoint() { hasRallyPoint = false; }
        public virtual Vector3 GetRallyPointPosition() { return hasRallyPoint ? rallyPointPosition : (transform.position + transform.forward * 5.0f); }
        protected virtual void UpdateRallyPointVisuals() { /* ... som tidigare ... */ }
        protected virtual void PositionRallyMarker() { /* ... som tidigare ... */ }

        // --- Selection Methods (Klient-sida) ---
        public virtual void Select() { /* ... som tidigare (med isMyBuilding check) ... */ }
        public virtual void Deselect() { /* ... som tidigare ... */ }

        // --- Protected Helper & Override Methods ---
        [Server] protected virtual void Server_HandleDestruction(bool isSold = false) { /* ... som tidigare ... */ }
        private IEnumerator Server_DestroyAfterDelay(float delay) { /* ... som tidigare ... */ yield return null; }
        [ClientRpc] private void RpcSetVisualsActive(bool active) { /* ... som tidigare ... */ }
        protected void UpdateProgressBar() { /* ... som tidigare ... */ }
        protected void UpdateProgressBarVisibility(bool isSelected) { /* ... som tidigare ... */ }
        protected bool IsSelected() { /* ... TODO: Implementera ... */ return false; }
        protected void UpdateColorBasedOnOwner(uint oldOwnerNetId, uint newOwnerNetId) { /* ... som tidigare ... */ }
        protected virtual void HaltFunctionality() { /* Subclasses implement */ }
        protected virtual void ResumeFunctionality() { /* Subclasses implement */ }
    }
}