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
    [RequireComponent(typeof(NetworkTransformUnreliable))] // <-- ÄNDRAD till konkret klass
    [RequireComponent(typeof(Collider))]
    public class Unit : NetworkBehaviour // Ärver från NetworkBehaviour
    {
        [Header("Unit Info")]
        [SerializeField] private string unitDisplayName = "Unit";
        // TODO: Lägg till UnitType Enum om det behövs för identifiering

        [Header("Combat Stats")] // Lade till header för ArmorType
        [SerializeField] private ArmorType armorType = ArmorType.Medium; // Sätt ett defaultvärde
        public ArmorType ArmorType => armorType; // Gör den läsbar utifrån

        [Header("Ownership & Team")]
        [SyncVar(hook = nameof(OnOwnerNetIdChanged))]
        public uint ownerNetId = 0; // 0 = Neutral/Server?

        [Header("Component References")]
        [SerializeField] protected Health healthComponent;
        [SerializeField] protected UnitMovement movementComponent;
        public float CurrentHealth => healthComponent != null ? healthComponent.CurrentHealth : 0f;
        public float MaxHealth => healthComponent != null ? healthComponent.MaxHealth : 1f;
        [SerializeField] protected NetworkTransformUnreliable networkTransform; // <-- ÄNDRAD till konkret klass
        [SerializeField] protected Renderer mainRenderer; // För färg/highlight
        [SerializeField] protected Collider unitCollider;
        [SerializeField] protected Animator animator; // Om animationer används


        [Header("Visuals")]
        [SerializeField] protected GameObject selectionIndicator;
        [SerializeField] protected Slider healthBarSlider;
        [SerializeField] protected GameObject healthBarCanvasGO;

        // --- State är borttaget från basklassen ---

        // Property för TeamID
        public int TeamID
        {
            get
            {
                if (ownerNetId == 0 || PlayerManager.Instance == null) return 0;
                NetworkPlayer ownerPlayer = PlayerManager.Instance.GetPlayer(ownerNetId);
                return ownerPlayer != null ? ownerPlayer.teamID : 0;
            }
        }
        public string UnitDisplayName => unitDisplayName;

        // --- Unity & Mirror Callbacks ---

        protected virtual void Awake()
        {
            if (healthComponent == null) healthComponent = GetComponent<Health>();
            if (movementComponent == null) movementComponent = GetComponent<UnitMovement>();
            if (networkTransform == null) networkTransform = GetComponent<NetworkTransformUnreliable>(); // <-- ÄNDRAD till konkret klass
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
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            OnOwnerNetIdChanged(0, ownerNetId); // Tvinga färg-uppdatering
            if (healthComponent != null) { UpdateHealthBarUI(0, healthComponent.CurrentHealth); }
        }

        // --- SyncVar Hooks ---

        protected virtual void OnOwnerNetIdChanged(uint oldOwnerNetId, uint newOwnerNetId)
        {
            UpdateColorBasedOnOwner();
        }

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
            // TODO: Sätt ev. faction-specifika saker om det behövs
        }

        // Anropas av SelectionManager (Klient-sida)
        public virtual void Select()
        {
            bool isMyUnit = (ownerNetId != 0 && NetworkClient.active && NetworkClient.localPlayer != null && ownerNetId == NetworkClient.localPlayer.netId);
            if (selectionIndicator) selectionIndicator.SetActive(true);
            if (healthBarCanvasGO && isMyUnit)
            {
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
            // Basklassen gör ingenting specifikt vid ankomst nu.
            // Subklasser måste hantera detta via sin egen state machine om de behöver det.
            // Debug.Log($"Unit {netId} arrived at destination.");
        }

        // --- Helper Methods ---

        protected void UpdateColorBasedOnOwner()
        {
            Color newColor = Color.grey;
            if (ownerNetId != 0)
            {
                NetworkPlayer ownerPlayer = null;
                if (NetworkClient.spawned.TryGetValue(ownerNetId, out var ownerIdentity))
                { // Försök på klient
                    ownerPlayer = ownerIdentity?.GetComponent<NetworkPlayer>();
                }
                else if (isServer && NetworkServer.spawned.TryGetValue(ownerNetId, out var ownerIdentitySrv))
                { // Försök på server
                    ownerPlayer = ownerIdentitySrv?.GetComponent<NetworkPlayer>();
                }
                if (ownerPlayer != null) newColor = ownerPlayer.playerColor;
            }
            if (mainRenderer != null)
            {
                // TODO: Använd MaterialPropertyBlock för bättre prestanda!
                mainRenderer.material.color = newColor;
            }
        }

        public virtual void UpdateHealthBarUI(float oldHealth, float newHealth)
        {
            if (healthBarSlider != null && healthComponent != null)
            {
                healthBarSlider.value = newHealth / healthComponent.MaxHealth;
            }
            else if (healthBarSlider != null) { healthBarSlider.value = 0; }
        }

        // --- Ägarskapskontroll ---
        [Server]
        protected bool IsOwner(NetworkConnection requestingConnection)
        {
            if (ownerNetId == 0) return false;
            if (requestingConnection == null) return false; // Kan inte vara ägaren om ingen connection skickas med
            if (NetworkServer.spawned.TryGetValue(ownerNetId, out NetworkIdentity ownerIdentity))
            {
                // Jämför connection som hör till ägarobjektet med connection som skickade kommandot
                return ownerIdentity.connectionToClient == requestingConnection;
            }
            else { Debug.LogError($"IsOwner Check: Could not find owner object with netId {ownerNetId} for unit {this.netId}"); return false; }
        }

    } // End class Unit
} // End namespace RTSGAME