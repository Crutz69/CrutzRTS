// Assets/RTSGAME/Scripts/Units/Unit.cs
using Mirror;
using UnityEngine;
using UnityEngine.UI; // För Slider i Health Bar prefab

namespace RTSGAME
{
    // Kräver nödvändiga komponenter för nätverk, hälsa, rörelse, synkning
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(UnitMovement))]
    [RequireComponent(typeof(NetworkTransformBase))] // NetworkTransform eller NetworkTransformChild
    [RequireComponent(typeof(Collider))]
    public class Unit : NetworkBehaviour // Ärv från NetworkBehaviour
    {
        [Header("Unit Info")]
        [SerializeField] private string unitDisplayName = "Unit";
        // TODO: Lägg till UnitType Enum om det behövs för identifiering

        [Header("Ownership & Team")]
        // Ägaren sätts av servern vid spawn. Hook uppdaterar färg etc.
        [SyncVar(hook = nameof(OnOwnerNetIdChanged))]
        public uint ownerNetId = 0; // 0 = Neutral/Server?

        [Header("Component References")]
        [SerializeField] protected Health healthComponent;
        [SerializeField] protected UnitMovement movementComponent;
        [SerializeField] protected NetworkTransformBase networkTransform;
        [SerializeField] protected Renderer mainRenderer; // För färg/highlight
        [SerializeField] protected Collider unitCollider;
        [SerializeField] protected Animator animator; // Om animationer används

        [Header("Visuals")]
        [SerializeField] protected GameObject selectionIndicator;
        [SerializeField] protected Slider healthBarSlider; // Koppla till Slidern i prefabens HealthBar Canvas
        [SerializeField] protected GameObject healthBarCanvasGO; // Koppla till Canvas GO i prefaben

        // --- State är nu borttaget från basklassen ---
        // public enum UnitState { Idle, Moving, Attacking, Building, Repairing, Capturing, Dead } // BORTTAGEN
        // [SyncVar(hook = nameof(OnStateChanged))] protected UnitState currentState = UnitState.Idle; // BORTTAGEN

        // Property för TeamID (hämtas via PlayerManager)
        public int TeamID
        {
            get
            {
                if (ownerNetId == 0 || PlayerManager.Instance == null) return 0; // Neutral/Ingen manager
                // OBS: Anpassa GetPlayer för att hantera uint netId korrekt!
                NetworkPlayer ownerPlayer = PlayerManager.Instance.GetPlayer(ownerNetId);
                return ownerPlayer != null ? ownerPlayer.teamID : 0;
            }
        }
        public string UnitDisplayName => unitDisplayName;
        // public UnitState CurrentState => currentState; // BORTTAGEN

        // --- Unity & Mirror Callbacks ---

        protected virtual void Awake()
        {
            if (healthComponent == null) healthComponent = GetComponent<Health>();
            if (movementComponent == null) movementComponent = GetComponent<UnitMovement>();
            if (networkTransform == null) networkTransform = GetComponent<NetworkTransformBase>();
            if (mainRenderer == null) mainRenderer = GetComponentInChildren<Renderer>();
            if (unitCollider == null) unitCollider = GetComponent<Collider>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (selectionIndicator) selectionIndicator.SetActive(false);
            if (healthBarCanvasGO) healthBarCanvasGO.SetActive(false);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            healthComponent?.Server_SetInitialHealth();
            // currentState = UnitState.Idle; // BORTTAGEN - State hanteras i subklasser
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            OnOwnerNetIdChanged(0, ownerNetId); // Tvinga färg-uppdatering
            // OnStateChanged(currentState, currentState); // BORTTAGEN
            if (healthComponent != null) { UpdateHealthBarUI(0, healthComponent.CurrentHealth); }
        }

        // --- SyncVar Hooks ---

        protected virtual void OnOwnerNetIdChanged(uint oldOwnerNetId, uint newOwnerNetId)
        {
            UpdateColorBasedOnOwner();
        }

        // protected virtual void OnStateChanged(UnitState oldState, UnitState newState) { /* BORTTAGEN */ }

        // Hook anropas från Health.cs när hälsan ändras
        public virtual void OnHealthUpdated(float oldHealth, float newHealth)
        {
            UpdateHealthBarUI(oldHealth, newHealth);
        }


        // --- Public Methods ---

        [Server]
        public virtual void Server_InitializeUnit(uint ownerId, int factionIdIfRelevant = 0)
        {
            ownerNetId = ownerId;
            healthComponent?.Server_SetInitialHealth();
            // currentState = UnitState.Idle; // BORTTAGEN
        }

        // Anropas av SelectionManager (Klient-sida)
        public virtual void Select()
        {
            bool isMyUnit = (ownerNetId != 0 && NetworkClient.active && NetworkClient.localPlayer != null && ownerNetId == NetworkClient.localPlayer.netId);
            if (selectionIndicator) selectionIndicator.SetActive(true);
            if (healthBarCanvasGO && isMyUnit)
            { // Exempel: Visa bara för egna enheter
                healthBarCanvasGO.SetActive(true);
                if (healthComponent) UpdateHealthBarUI(0, healthComponent.CurrentHealth);
            }
            // TODO: Annan highlight-effekt?
        }

        // Anropas av SelectionManager (Klient-sida)
        public virtual void Deselect()
        {
            if (selectionIndicator) selectionIndicator.SetActive(false);
            if (healthBarCanvasGO) healthBarCanvasGO.SetActive(false);
            // TODO: Stäng av highlight-effekt?
        }


        // --- Metod som anropas av UnitMovement ---
        [Server]
        public virtual void OnMovementArrival()
        {
            // Basklassen gör kanske ingenting specifikt här nu när state är borttaget.
            // Subklasser som ConstructionWorker måste overridea denna (eller hantera via sin egen state)
            // om de behöver veta när en generell rörelse är klar för att gå till Idle.
            Debug.Log($"Unit {netId} arrived at destination (Called from UnitMovement).");
            // Kanske anropa en generell Idle-metod om en sådan finns?
            // Server_GoToIdleState(); // Denna behöver definieras om och användas av subklasser
        }

        // Exempel på Idle-metod (kanske inte behövs i basklass)
        // [Server]
        // public virtual void Server_GoToIdleState() {
        //      if (movementComponent != null) { movementComponent.Server_StopMovement(); }
        //      Debug.Log($"Unit {netId} requested to go idle.");
        //      // Subklasser sätter sitt eget state här
        // }


        // --- Helper Methods ---

        protected void UpdateColorBasedOnOwner()
        {
            Color newColor = Color.grey;
            if (ownerNetId != 0)
            {
                if (NetworkClient.spawned.TryGetValue(ownerNetId, out NetworkIdentity ownerIdentity))
                {
                    NetworkPlayer ownerPlayer = ownerIdentity.GetComponent<NetworkPlayer>();
                    if (ownerPlayer != null) newColor = ownerPlayer.playerColor;
                }
                else if (isServer && NetworkServer.spawned.TryGetValue(ownerNetId, out NetworkIdentity ownerIdentitySrv))
                {
                    NetworkPlayer ownerPlayer = ownerIdentitySrv.GetComponent<NetworkPlayer>();
                    if (ownerPlayer != null) newColor = ownerPlayer.playerColor;
                }
            }
            if (mainRenderer != null) { mainRenderer.material.color = newColor; } // Använd MaterialPropertyBlock!
        }

        public virtual void UpdateHealthBarUI(float oldHealth, float newHealth)
        {
            if (healthBarSlider != null && healthComponent != null)
            {
                healthBarSlider.value = newHealth / healthComponent.MaxHealth;
            }
            else if (healthBarSlider != null) { healthBarSlider.value = 0; }
        }

        // Animation uppdateras nu i subklassens state hook baserat på dess egna state enum
        // protected virtual void UpdateAnimation(UnitState oldState, UnitState newState) { /* BORTTAGEN */ }


        // --- Ägarskapskontroll ---
        /// <summary>
        /// Checks if the provided network connection owns this unit.
        /// MUST be called on the server.
        /// </summary>
        [Server]
        protected bool IsOwner(NetworkConnection requestingConnection)
        {
            if (ownerNetId == 0) return false;
            if (requestingConnection == null) return false;
            if (NetworkServer.spawned.TryGetValue(ownerNetId, out NetworkIdentity ownerIdentity))
            {
                return ownerIdentity.connectionToClient == requestingConnection;
            }
            else { Debug.LogError($"Could not find owner object with netId {ownerNetId} for unit {this.netId}"); return false; }
        }

    }
}