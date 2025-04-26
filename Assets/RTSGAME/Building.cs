// Filnamn: Building.cs
// Uppdaterad version med korrigeringar för OnServerDied och ConstructionWorker

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using System.Collections.Generic;
using System.Collections;
using Mirror;

namespace RTSGAME
{
    // Säkerställ att denna enum finns definierad (t.ex. i Enums.cs)
    // public enum BuildingState { Ghost, Placing, Constructing, Operational, Disabled_NoPower, BeingCaptured, Destroyed }

    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(Health))]
    public abstract class Building : NetworkBehaviour // Fortfarande abstract!
    {
        [Header("Building Identification")]
        [SerializeField] private string buildingName = "Default Building";
        [Tooltip("Faction this building originally belonged to. Determines tech/units.")]
        [SyncVar(hook = nameof(OnFactionChanged))]
        public int originalFactionID = 0;

        [Header("Core Stats")]
        [SerializeField] private int costCredits = 100;
        // *** VIKTIGT: Dessa används nu för Mana Upkeep systemet ***
        [Tooltip("Hur mycket Mana denna byggnad KRÄVER per sekund/tick.")]
        [SerializeField] private int manaUpkeep = 5;
        [Tooltip("Hur mycket Mana denna byggnad GENERERAR per sekund/tick.")]
        [SerializeField] private int manaGeneration = 0;
        // *** ----------------------------------------------- ***
        [SerializeField][Range(0f, 1f)] private float sellReturnPercentage = 0.7f;
        [SerializeField] private int requiredTier = 1;

        [Header("Combat Stats")]
        [SerializeField] private ArmorType armorType = ArmorType.Fortified;
        public ArmorType ArmorType => armorType;

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
        [SyncVar] public uint capturingWorkerNetId = 0; // Gör public för att Worker ska kunna kolla? Eller via metod?
        [SyncVar(hook = nameof(OnCaptureProgressChanged))] private float captureProgress = 0f;
        private Coroutine captureCoroutine = null;

        [Header("Gameplay")]
        [SerializeField] private float visionRadius = 15f;

        [Header("Ownership & State")]
        [SyncVar(hook = nameof(OnOwnerChanged))] private uint ownerNetId = 0;
        [SyncVar(hook = nameof(OnCurrentStateChanged))] private BuildingState currentState = BuildingState.Ghost;
        // ÄNDRAD: isPowered kollar nu specifikt mot Operational state
        private bool isPowered => currentState == BuildingState.Operational;

        [Header("Component References")]
        [SerializeField] public Health healthComponent;
        // Hälsa initieras i Awake/OnStartServer av Health.cs scriptet

        [Header("UI & Visuals")]
        [SerializeField] private Slider healthBarSlider;
        [SerializeField] private GameObject selectionIndicator;
        [SerializeField] private Slider progressBarSlider; // För byggnation/capture/forskning?

        [Header("Rally Point")]
        [SerializeField] public GameObject rallyPointVisualPrefab;
        [SerializeField] public LineRenderer rallyPointLineRenderer;
        [SyncVar(hook = nameof(OnRallyPointChanged))] protected Vector3 rallyPointPosition;
        [SyncVar] protected bool hasRallyPoint = false;

        // --- Properties ---
        public string BuildingName => buildingName;
        public float CurrentHealth => healthComponent != null ? healthComponent.CurrentHealth : 0;
        public float MaxHealth => healthComponent != null ? healthComponent.MaxHealth : 1;
        public bool IsDead => healthComponent != null ? healthComponent.IsDead : true;
        public int CostCredits => costCredits;
        // NYTT/ÄNDRAT: Exponerar Upkeep/Generation så ResourceManager (och ev. andra) kan läsa dem
        public int ManaUpkeep => manaUpkeep;
        public int ManaGeneration => manaGeneration;
        public int RequiredTier => requiredTier;
        public uint OwnerNetId => ownerNetId;
        public BuildingState CurrentState => currentState;
        public bool IsPowered => isPowered; // Använder den uppdaterade privata variabeln
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

        public string buildableId; // Eller gör den till [SyncVar] om klienten behöver veta det av andra skäl

        // public uint CapturingWorkerNetId => capturingWorkerNetId; // Exponerad via variabel ovan

        // --- Events ---
        [Header("Events")]
        public UnityEvent OnBuildingPlaced_Local;
        public UnityEvent OnConstructionStart_Local;
        public UnityEvent OnConstructionProgress_Local;
        public UnityEvent OnConstructionComplete_Local;
        public UnityEvent OnSold_Local;
        public UnityEvent OnPowerStateChanged_Local; // För när strömmen går/kommer tillbaka
        public UnityEvent OnSelected_Local;
        public UnityEvent OnDeselected_Local;
        public UnityEvent OnRallyPointSet_Local;
        public UnityEvent OnCaptureStart_Local;
        public UnityEvent OnCaptureComplete_Local;
        public UnityEvent OnCaptureCancel_Local;
        // Du kan lägga till fler events vid behov

        // --- Unity Methods & Mirror Callbacks ---

        protected virtual void Awake()
        {
            if (healthComponent == null) healthComponent = GetComponent<Health>();
            if (healthComponent == null) Debug.LogError($"Building {gameObject.name} is missing Health component!", this);

            // Prenumerera på Health-event för att veta när vi dör/säljs
            if (healthComponent != null)
            {
                // *** KORRIGERING HÄR: *** Använd korrekt eventnamn från Health.cs
                healthComponent.OnServerDied.AddListener(Server_OnDie);
            }

            if (selectionIndicator) selectionIndicator.SetActive(false);
            if (rallyPointLineRenderer) rallyPointLineRenderer.enabled = false;
            if (rallyPointVisualPrefab && rallyPointVisualPrefab.activeSelf) rallyPointVisualPrefab.SetActive(false);
            UpdateProgressBar();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            // Anropa Server_UpdateResourceManager om byggnaden spawnas som redan färdig/operational
            if (currentState == BuildingState.Operational)
            {
                Server_UpdateResourceManagerContribution(true); // Lägg till bidrag
            }
            // Nollställ capture state vid start
            isBeingCaptured = false;
            captureProgress = 0f;
            capturingWorkerNetId = 0;
            if (captureCoroutine != null) StopCoroutine(captureCoroutine); captureCoroutine = null;
        }

        public override void OnStopServer()
        {
            // NYTT: Meddela ResourceManager INNAN objektet förstörs på servern
            if (currentState == BuildingState.Operational || currentState == BuildingState.Disabled_NoPower)
            {
                Server_UpdateResourceManagerContribution(false); // Ta bort bidrag
            }
            base.OnStopServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Kör hooks manuellt vid start för att säkerställa korrekt state
            OnFactionChanged(0, originalFactionID);
            OnOwnerChanged(0, ownerNetId);
            OnCurrentStateChanged(currentState, currentState); // Använd nuvarande state som "gammalt" för att trigga logik
            OnConstructionProgressChanged(0, constructionProgress);
            OnCaptureStateChanged(false, isBeingCaptured);
            OnCaptureProgressChanged(0, captureProgress);
            OnRallyPointChanged(Vector3.zero, rallyPointPosition);
            if (healthComponent != null) UpdateHealthBarUI(0, healthComponent.CurrentHealth);
        }

        protected virtual void OnDestroy()
        {
            // Avprenumerera från Health-event
            if (healthComponent != null)
            {
                // *** KORRIGERING HÄR: *** Använd korrekt eventnamn från Health.cs
                healthComponent.OnServerDied.RemoveListener(Server_OnDie);
            }
            if (isServer && captureCoroutine != null) { StopCoroutine(captureCoroutine); captureCoroutine = null; }
            // Avregistrera från managers?
        }

        // --- SyncVar Hooks ---
        void OnFactionChanged(int oldId, int newId) { /* Update visuals based on faction? */ }
        void OnConstructionProgressChanged(float oldProgress, float newProgress) { UpdateProgressBar(); OnConstructionProgress_Local?.Invoke(); }
        void OnCaptureStateChanged(bool oldState, bool newState) { UpdateProgressBar(); if (newState) { OnCaptureStart_Local?.Invoke(); } else { if (captureProgress < 1f) { captureProgress = 0f; UpdateProgressBar(); OnCaptureCancel_Local?.Invoke(); } } }
        void OnCaptureProgressChanged(float oldProgress, float newProgress) { UpdateProgressBar(); if (oldProgress < 1f && newProgress >= 1f && !isBeingCaptured) { OnCaptureComplete_Local?.Invoke(); } } // OnCaptureComplete anropas när capture är klar (progress >= 1) men state-ändringen (isBeingCaptured=false) kanske inte skett än
        void OnOwnerChanged(uint oldOwnerNetId, uint newOwnerNetId) { UpdateColorBasedOnOwner(oldOwnerNetId, newOwnerNetId); /* Debug.Log($"{BuildingName} owner changed from {oldOwnerNetId} to {newOwnerNetId}"); */ }
        void OnCurrentStateChanged(BuildingState oldState, BuildingState newState)
        {
            // Debug.Log($"{BuildingName} state changed from {oldState} to {newState} (Owner: {OwnerNetId})");
            // Kolla specifikt efter ändring till/från Disabled_NoPower
            if ((oldState == BuildingState.Operational && newState == BuildingState.Disabled_NoPower) ||
                (oldState == BuildingState.Disabled_NoPower && newState == BuildingState.Operational))
            {
                OnPowerStateChanged_Local?.Invoke(); // Trigga lokalt event för UI/VFX
            }

            // Gamla events
            if (oldState == BuildingState.Constructing && newState == BuildingState.Operational) { OnConstructionComplete_Local?.Invoke(); }
            if (oldState == BuildingState.Placing && newState == BuildingState.Constructing) { OnConstructionStart_Local?.Invoke(); }
            UpdateProgressBar();
        }
        void OnRallyPointChanged(Vector3 oldPos, Vector3 newPos) { if (IsSelected()) { UpdateRallyPointVisuals(); } if (hasRallyPoint && oldPos == Vector3.zero && newPos != Vector3.zero) OnRallyPointSet_Local?.Invoke(); }

        // --- UI Uppdateringar (Klient) ---
        public virtual void UpdateHealthBarUI(float oldHealth, float newHealth)
        {
            if (healthBarSlider != null && healthComponent != null) { healthBarSlider.value = newHealth / healthComponent.MaxHealth; }
            else if (healthBarSlider != null) { healthBarSlider.value = 0; }
        }

        // --- Server-Side Logic ---

        [Server]
        public void Server_InitializeBuilding(uint ownerId, int factionId, BuildingState initialState = BuildingState.Operational)
        {
            ownerNetId = ownerId;
            originalFactionID = factionId;
            healthComponent?.Server_SetInitialHealth(); // Initiera hälsa

            // Sätt initial state och progress
            currentState = initialState;
            if (initialState == BuildingState.Constructing) { constructionProgress = 0f; }
            else if (initialState == BuildingState.Operational) { constructionProgress = 1f; }
            else { constructionProgress = 0f; } // Default för Ghost/Placing

            // Uppdatera ResourceManager om den startar som färdig
            if (initialState == BuildingState.Operational)
            {
                Server_UpdateResourceManagerContribution(true); // Lägg till bidrag direkt
            }
        }

        [Server]
        public bool Server_AssignBuilder(uint workerNetId)
        {
            if (CanAssignBuilder && !currentBuilderNetIds.Contains(workerNetId))
            {
                if (currentBuilderNetIds.Count == 0 && currentState == BuildingState.Placing)
                {
                    currentState = BuildingState.Constructing; // Gå från Placing -> Constructing när första byggaren börjar
                }
                currentBuilderNetIds.Add(workerNetId);
                return true;
            }
            return false;
        }

        [Server]
        public void Server_RemoveBuilder(uint workerNetId)
        {
            currentBuilderNetIds.Remove(workerNetId);
            // Om ingen bygger längre, men den inte är klar, pausas den? Eller låter vi den bara stå? Designval.
        }

        [Server]
        public void Server_ContributeConstruction(float workAmount)
        {
            if (currentState != BuildingState.Constructing || constructionProgress >= 1f) return;

            constructionProgress = Mathf.Clamp01(constructionProgress + workAmount / constructionDuration);

            // Gradvis hälsoökning (om Health.cs har metoden Server_SetHealthDirectly)
            if (healthComponent != null)
            {
                // Om du har implementerat Server_SetHealthDirectly i Health.cs och vill ha gradvis hälsa:
                // healthComponent.Server_SetHealthDirectly(Mathf.Lerp(1, healthComponent.MaxHealth, constructionProgress));

                // Om du INTE har metoden eller föredrar att hälsan sätts till max när klar (enklare):
                // Låt denna rad vara utkommenterad eller ta bort den.
            }

            if (constructionProgress >= 1f)
            {
                Server_MarkAsFunctional();
            }
        }

        [Server]
        private void Server_MarkAsFunctional()
        {
            if (currentState == BuildingState.Operational || currentState == BuildingState.Destroyed) return;

            BuildingState previousState = currentState;
            currentState = BuildingState.Operational;
            constructionProgress = 1f;
            if (healthComponent != null) healthComponent.Server_Heal(healthComponent.MaxHealth); // Säkerställ full hälsa

            // NYTT: Meddela ResourceManager när byggnaden blir funktionell
            if (previousState != BuildingState.Operational) // Endast om den inte redan var det
            {
                Server_UpdateResourceManagerContribution(true); // Lägg till bidrag
            }

            // Meddela byggare
            List<uint> buildersToNotify = new List<uint>(currentBuilderNetIds);
            currentBuilderNetIds.Clear(); // Rensa listan direkt
            foreach (uint workerNetId in buildersToNotify)
            {
                if (NetworkServer.spawned.TryGetValue(workerNetId, out NetworkIdentity id))
                {
                    // *** KORRIGERING HÄR: *** Använd ConstructionWorker istället för HarvesterUnit
                    ConstructionWorker worker = id.GetComponent<ConstructionWorker>();
                    if (worker != null)
                    {
                        worker.Target_ConstructionComplete(this.netIdentity); // Skicka med byggnadens ID
                    }
                    else
                    {
                        Debug.LogWarning($"Unit {id.netId} was listed as a builder for {this.BuildingName} but is not a ConstructionWorker!");
                    }
                }
            }
        }

        // NYTT: Implementering av SetPoweredState (anropas av ResourceManager)
        [Server]
        public void Server_SetPoweredState(bool hasPower)
        {
            // Debug.Log($"{BuildingName} Server_SetPoweredState called with: {hasPower}. Current state: {currentState}");
            if (hasPower)
            {
                // Försök slå på strömmen
                if (currentState == BuildingState.Disabled_NoPower)
                {
                    currentState = BuildingState.Operational;
                    ResumeFunctionality(); // Kör logik för att återuppta funktioner
                    Debug.Log($"{BuildingName} powered ON.");
                }
            }
            else
            {
                // Försök stänga av strömmen
                if (currentState == BuildingState.Operational)
                {
                    currentState = BuildingState.Disabled_NoPower;
                    HaltFunctionality(); // Kör logik för att pausa funktioner
                    Debug.Log($"{BuildingName} powered OFF due to low mana.");
                }
            }
        }

        [Server]
        public void Server_Sell(uint sellingPlayerNetId)
        {
            if (IsDead) return;
            if (sellingPlayerNetId != OwnerNetId) return;

            int creditsReturned = Mathf.FloorToInt(CostCredits * sellReturnPercentage);
            ResourceManager.Instance?.Server_AddCredits(OwnerNetId, creditsReturned);
            Debug.Log($"{BuildingName} sold by player {OwnerNetId} for {creditsReturned} credits.");

            // RpcInformSold();
            Server_HandleDestruction(true); // Förstör byggnaden (isSold=true)
        }

        // --- Capture Logic (Server-Side) ---
        [Server]
        public bool Server_StartCaptureAttempt(NetworkIdentity workerIdentity)
        {
            // TODO: Implementera logik här!
            // 1. Validera: Är byggnaden capturable? Är worker av rätt typ? Är worker fiende? Är den redan fångad?
            if (currentState != BuildingState.Operational || isBeingCaptured || IsDead) return false;
            if (workerIdentity.netId == this.ownerNetId) return false; // Kan inte fånga egna

            // 2. Sätt state
            isBeingCaptured = true;
            capturingWorkerNetId = workerIdentity.netId;
            captureProgress = 0f;

            // 3. Starta timer/coroutine
            if (captureCoroutine != null) StopCoroutine(captureCoroutine);
            captureCoroutine = StartCoroutine(CaptureTimer(workerIdentity, captureDuration));
            Debug.Log($"{BuildingName} capture started by worker {workerIdentity.netId}");
            return true;
        }

        private IEnumerator CaptureTimer(NetworkIdentity workerIdentity, float duration)
        {
            float timer = 0f;
            uint initialWorkerId = workerIdentity.netId; // Spara ID ifall workerIdentity blir null

            while (timer < duration && isBeingCaptured && capturingWorkerNetId == initialWorkerId)
            {
                // Kolla om worker fortfarande finns och är nära nog? (Kan bli komplext)
                timer += Time.deltaTime;
                captureProgress = Mathf.Clamp01(timer / duration);
                yield return null;
            }

            // Om timern slutfördes och vi fortfarande capturear med samma worker
            if (timer >= duration && isBeingCaptured && capturingWorkerNetId == initialWorkerId)
            {
                Server_CompleteCapture(initialWorkerId);
            }
            captureCoroutine = null; // Coroutine är klar
        }

        [Server]
        private void Server_CompleteCapture(uint newOwnerWorkerNetId)
        {
            if (!NetworkServer.spawned.TryGetValue(newOwnerWorkerNetId, out NetworkIdentity workerIdentity))
            {
                Debug.LogWarning($"Capture failed for {BuildingName}. Worker {newOwnerWorkerNetId} not found.");
                Server_CancelCaptureAttempt("Capturing worker disappeared");
                return;
            }

            uint newOwnerPlayerNetId = workerIdentity.connectionToClient.identity.netId; // Hämta spelarens NetId från workerns connection
            Debug.Log($"{BuildingName} captured by player {newOwnerPlayerNetId} (via worker {newOwnerWorkerNetId})");

            // Informera gamla Resource Manager (om den fanns och var igång)
            if (currentState == BuildingState.Operational || currentState == BuildingState.Disabled_NoPower)
            {
                Server_UpdateResourceManagerContribution(false); // Tar bort från gamla ägaren
            }

            // Byt ägare och state
            uint oldOwner = ownerNetId;
            ownerNetId = newOwnerPlayerNetId; // Byt ägare till spelaren som äger workern
            isBeingCaptured = false;
            capturingWorkerNetId = 0;
            captureProgress = 0f; // Nollställ progress (hook körs)
            currentState = BuildingState.Operational; // Sätt till Operational för nya ägaren

            // Informera nya Resource Manager (om den nu är igång)
            Server_UpdateResourceManagerContribution(true); // Lägger till för nya ägaren

            // Meddela workern att capture är klar
            ConstructionWorker worker = workerIdentity.GetComponent<ConstructionWorker>();
            worker?.Target_CaptureComplete(this.netIdentity);
        }


        [Server]
        public void Server_CancelCaptureAttempt(string reason)
        {
            if (!isBeingCaptured) return;
            Debug.Log($"{BuildingName} capture cancelled. Reason: {reason}");

            uint workerIdToNotify = capturingWorkerNetId;

            isBeingCaptured = false;
            capturingWorkerNetId = 0;
            captureProgress = 0f; // Nollställ progress (hook körs)
            if (captureCoroutine != null) { StopCoroutine(captureCoroutine); captureCoroutine = null; }

            // Meddela workern som försökte capture att det avbröts
            if (NetworkServer.spawned.TryGetValue(workerIdToNotify, out NetworkIdentity workerIdentity))
            {
                ConstructionWorker worker = workerIdentity.GetComponent<ConstructionWorker>();
                worker?.Target_CaptureInterrupted(this.netIdentity);
            }
        }


        [Server] public void Server_ChangeOwner(uint newOwnerNetId) { /* Används inte om capture sköter det */ }
        [Server] private void Server_UpdateColorBasedOnOwner() { /* TODO: Implementera färguppdatering */ }
        [ClientRpc] private void RpcUpdateVisualColor(Color newColor) { /* TODO: Implementera färguppdatering */ }

        // --- Rally Point Logic ---
        [Command] public void CmdSetRallyPoint(Vector3 position) { if (IsOwner(connectionToClient)) Server_SetRallyPoint(position); }
        [Command] public void CmdClearRallyPoint() { if (IsOwner(connectionToClient)) Server_ClearRallyPoint(); }
        [Server] public void Server_SetRallyPoint(Vector3 position) { rallyPointPosition = position; hasRallyPoint = true; }
        [Server] public void Server_ClearRallyPoint() { hasRallyPoint = false; }
        public virtual Vector3 GetRallyPointPosition() { return hasRallyPoint ? rallyPointPosition : (transform.position + transform.forward * 5.0f); } // Ge en default-position framför
        protected virtual void UpdateRallyPointVisuals() { /* TODO: Implementera rally point visuals */ }
        protected virtual void PositionRallyMarker() { /* TODO: Implementera rally point visuals */ }

        // --- Selection Methods (Klient-sida) ---
        public virtual void Select() { bool isMy = (OwnerNetId != 0 && NetworkClient.active && NetworkClient.localPlayer != null && OwnerNetId == NetworkClient.localPlayer.netId); if (selectionIndicator) selectionIndicator.SetActive(true); if (healthBarSlider) healthBarSlider.gameObject.SetActive(isMy); if (isMy && hasRallyPoint) { /* Rally point visuals */ } UpdateProgressBarVisibility(true); OnSelected_Local?.Invoke(); }
        public virtual void Deselect() { if (selectionIndicator) selectionIndicator.SetActive(false); if (healthBarSlider) healthBarSlider.gameObject.SetActive(false); if (progressBarSlider) progressBarSlider.gameObject.SetActive(false); /* Rally point visuals off */ OnDeselected_Local?.Invoke(); }

        // --- Protected Helper & Override Methods ---

        // NYTT: Metod som anropas när hälsan når noll (via event från Health.cs)
        [Server]
        protected virtual void Server_OnDie()
        {
            Server_HandleDestruction(false); // Hantera förstörelse (isSold = false)
        }

        // ÄNDRAD: Hanterar förstörelse och meddelar ResourceManager
        [Server]
        protected virtual void Server_HandleDestruction(bool isSold)
        {
            if (currentState == BuildingState.Destroyed) return; // Redan förstörd

            BuildingState previousState = currentState;
            currentState = BuildingState.Destroyed; // Sätt state direkt

            // NYTT: Meddela ResourceManager att ta bort bidraget
            if (previousState == BuildingState.Operational || previousState == BuildingState.Disabled_NoPower)
            {
                Server_UpdateResourceManagerContribution(false); // Ta bort bidrag
            }

            // Stoppa eventuell capture
            if (isBeingCaptured) Server_CancelCaptureAttempt("Building destroyed/sold");

            // TODO: Skapa explosion/ruin-effekt?

            // Rensa byggare (lista rensas i Server_MarkAsFunctional, men bra att ha här med?)
            currentBuilderNetIds.Clear();

            // Sätt inaktiv och förstör efter fördröjning
            RpcSetVisualsActive(false); // Göm direkt på klienter
            StartCoroutine(Server_DestroyAfterDelay(0.1f)); // Kort delay för att RPC ska hinna fram?
        }

        // NYTT: Centraliserad funktion för att uppdatera ResourceManager
        [Server]
        protected void Server_UpdateResourceManagerContribution(bool isAdding)
        {
            if (ResourceManager.Instance == null || OwnerNetId == 0) return;

            int upkeepDelta = isAdding ? ManaUpkeep : -ManaUpkeep;
            int generationDelta = isAdding ? ManaGeneration : -ManaGeneration;

            if (upkeepDelta != 0)
            {
                ResourceManager.Instance.Server_AddOrRemoveManaUpkeep(OwnerNetId, upkeepDelta);
            }
            if (generationDelta != 0)
            {
                ResourceManager.Instance.Server_AddOrRemoveManaGeneration(OwnerNetId, generationDelta);
            }
        }


        private IEnumerator Server_DestroyAfterDelay(float delay) { yield return new WaitForSeconds(delay); NetworkServer.Destroy(gameObject); }
        [ClientRpc] private void RpcSetVisualsActive(bool active) { /* TODO: Implementera detta */ }
        protected void UpdateProgressBar() { /* TODO: Implementera progress bar logik */ }
        protected void UpdateProgressBarVisibility(bool isSelected) { /* TODO: Implementera progress bar logik */ }
        protected bool IsSelected() { return SelectionManager.Instance != null && SelectionManager.Instance.IsSelected(this.gameObject); }
        protected void UpdateColorBasedOnOwner(uint oldOwnerNetId, uint newOwnerNetId) { /* TODO: Implementera detta */ }

        // ÄNDRAD: Gör dessa virtual så subklasser MÅSTE tänka på dem
        // Anropas när strömmen bryts (currentState -> Disabled_NoPower)
        [Server] protected virtual void HaltFunctionality() { Debug.Log($"{BuildingName} halting functionality due to lack of power."); /* Subclasses implementerar specifik pauslogik */ }
        // Anropas när strömmen kommer tillbaka (currentState -> Operational)
        [Server] protected virtual void ResumeFunctionality() { Debug.Log($"{BuildingName} resuming functionality as power is restored."); /* Subclasses implementerar specifik återupptagningslogik */ }

        // Helper för ägarkoll
        protected bool IsOwner(NetworkConnection conn)
        {
            // Kontrollera om den anslutning som skickar kommandot äger detta objekt
            // Fungerar om objektet har Network Authority satt till klienten ELLER om vi kollar mot OwnerNetId.
            return conn != null && OwnerNetId != 0 && conn.identity != null && conn.identity.netId == OwnerNetId;
        }

        /// <summary>
        /// [Server] Försöker lägga till ett objekt i produktionskön.
        /// Basklassens implementation returnerar alltid false (kan ej köa).
        /// Överskuggas (override) i klasser som kan producera (t.ex. ProductionBuilding).
        /// </summary>
        [Server]
        public virtual bool Server_QueueItem(string buildableId, int quantity)
        {
            // Byggnader kan inte köa saker som standard
            Debug.LogWarning($"Attempted to queue item '{buildableId}' at building '{this.BuildingName}' which is not a production building.");
            return false;
        }

        /// <summary>
        /// [Server] Hanterar högerklick på ett objekt i produktionskön.
        /// Basklassens implementation gör ingenting.
        /// Överskuggas (override) i klasser som har en kö (t.ex. ProductionBuilding).
        /// </summary>
        /// <param name="queueIndex">Indexet i kön som klickades på (-1 om det var det aktiva objektet?).</param>
        [Server]
        public virtual void Server_HandleRightClickOnQueue(int queueIndex)
        {
            // Gör ingenting som standard för byggnader som inte har en kö.
            Debug.LogWarning($"Server_HandleRightClickOnQueue called on building '{this.BuildingName}' which does not handle queues this way.");
        }

    } // End class Building
} // End namespace RTSGAME