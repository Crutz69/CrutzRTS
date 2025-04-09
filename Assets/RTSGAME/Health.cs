// Assets/RTSGAME/Scripts/Shared/Health.cs (eller i Components/ o.s.v.)
using Mirror;
using System.Collections;
using UnityEngine;
using UnityEngine.Events; // För UnityEvents

namespace RTSGAME
{
    [RequireComponent(typeof(NetworkIdentity))]
    public class Health : NetworkBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float maxHealth = 100f;

        // SyncVar för att synka aktuell hälsa till alla klienter
        // Hook anropas på klienter när värdet ändras
        [SyncVar(hook = nameof(OnHealthChanged_Hook))]
        private float currentHealth;

        // Property för att komma åt maxhälsa utifrån (read-only)
        public float MaxHealth => maxHealth;
        // Property för att läsa aktuell hälsa (alla kan läsa, bara servern ändrar via metoder)
        public float CurrentHealth => currentHealth;
        // Property för att enkelt kolla om död
        public bool IsDead => currentHealth <= 0;

        // Events för att koppla effekter/logik
        [Header("Events")]
        public UnityEvent OnServerDied = new UnityEvent(); // Triggas på servern när hälsa når 0
        public UnityEvent OnClientDied = new UnityEvent(); // Triggas på klienter via RPC när död inträffar
        public UnityEvent OnClientDamaged = new UnityEvent(); // Triggas på klienter via RPC vid skada

        // Referens tillbaka till huvudscriptet (Unit/Building) för att meddela UI-uppdatering
        private Unit unitReference; // Antag att vi är på en Unit
        private Building buildingReference; // Eller en Building

        private void Awake()
        {
            // Försök hitta Unit eller Building på samma GameObject
            unitReference = GetComponent<Unit>();
            buildingReference = GetComponent<Building>();
        }


        // --- Server Logic ---

        public override void OnStartServer()
        {
            base.OnStartServer();
            // Servern sätter initial hälsa
            Server_SetInitialHealth();
        }

        /// <summary>
        /// Sets initial health on the server. Typically called when the object spawns.
        /// </summary>
        [Server]
        public void Server_SetInitialHealth()
        {
            currentHealth = maxHealth;
            // Se till att eventuella döds-flaggor är återställda om objektet återanvänds (pooling)
        }

        /// <summary>
        /// Applies damage to this object. ONLY callable on the server.
        /// </summary>
        /// <param name="amount">Amount of damage to apply.</param>
        /// <returns>True if damage was taken (object was not already dead).</returns>
        [Server]
        public bool Server_TakeDamage(float amount)
        {
            // Ignorera skada om redan död eller om skadan är ogiltig
            if (currentHealth <= 0 || amount <= 0)
            {
                return false;
            }

            // Applicera skada, säkerställ att den inte går under 0
            currentHealth = Mathf.Max(currentHealth - amount, 0f);
            Debug.Log($"{gameObject.name} (NetId: {netId}) took {amount} damage. Health: {currentHealth}/{maxHealth}");

            // Trigga skadeeffekt på klienter
            Rpc_DamageEffect();

            // Kolla om objektet dog
            if (currentHealth <= 0)
            {
                Server_Die();
            }

            return true;
        }

        /// <summary>
        /// Heals this object. ONLY callable on the server.
        /// </summary>
        [Server]
        public void Server_Heal(float amount)
        {
            if (currentHealth <= 0 || amount <= 0 || currentHealth >= maxHealth)
            {
                return; // Kan inte hela döda, negativa värden, eller om redan full hälsa
            }
            currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
            Debug.Log($"{gameObject.name} (NetId: {netId}) healed {amount}. Health: {currentHealth}/{maxHealth}");
            // Behövs RPC för heal-effekt? Kanske inte, UI uppdateras via hook.
        }


        /// <summary>
        /// Handles the death logic on the server.
        /// </summary>
        [Server]
        private void Server_Die()
        {
            Debug.Log($"{gameObject.name} (NetId: {netId}) Died.");
            // Trigga server-specifika händelser (t.ex. ge poäng, meddela GameManager)
            OnServerDied?.Invoke();

            // Trigga visuella/ljud-effekter på alla klienter
            Rpc_DieEffect();

            // TODO: Servern bör inaktivera relevanta komponenter direkt för att stoppa logik
            Collider col = GetComponent<Collider>(); if (col != null) col.enabled = false;
            UnitMovement mv = GetComponent<UnitMovement>(); if (mv != null) mv.Server_StopMovement(); // Stoppa rörelse
                                                                                                      // Stoppa ev. stridskomponent

            // Starta förstörelseprocessen (med fördröjning för effekter)
            // Mirror hanterar att objektet tas bort från klienter när NetworkServer.Destroy anropas.
            // Använd en Coroutine på en manager eller en separat komponent för fördröjningen.
            // Detta enkla exempel förstör direkt (mindre snyggt):
            // NetworkServer.Destroy(gameObject);
            // Bättre:
            StartCoroutine(Server_DestroyAfterDelay(3.0f)); // Starta Coroutine på detta objekt (eller flytta till manager)
        }

        [Server]
        private IEnumerator Server_DestroyAfterDelay(float delay)
        {
            // Skicka RPC för att dölja direkt på klienter? Eller lita på att Destroy löser det?
            // Rpc_HideOnDeath();
            yield return new WaitForSeconds(delay);
            Debug.Log($"Destroying {gameObject.name} (NetId: {netId}) on server.");
            NetworkServer.Destroy(gameObject);
        }


        // --- Client Logic (RPCs & Hooks) ---

        /// <summary>SyncVar Hook called on clients when currentHealth changes.</summary>
        private void OnHealthChanged_Hook(float oldHealth, float newHealth)
        {
            // Uppdatera UI (via Unit/Building scriptet)
            if (unitReference != null)
            {
                unitReference.OnHealthUpdated(oldHealth, newHealth); // Meddela Unit-scriptet
            }
            else if (buildingReference != null)
            {
                // buildingReference.OnHealthUpdated(oldHealth, newHealth); // Meddela Building-scriptet (om det behöver veta)
                // Kanske uppdatera Building's health bar direkt?
                buildingReference.SendMessage("UpdateHealthBarUI", new object[] { oldHealth, newHealth }, SendMessageOptions.DontRequireReceiver);
            }

            // Spela eventuellt skade-ljud lokalt om hälsan minskade?
            // Kan vara svårt att skilja från RPC, använd Rpc_DamageEffect för det.
        }

        /// <summary>ClientRpc called by server when damage is taken.</summary>
        [ClientRpc]
        private void Rpc_DamageEffect()
        {
            // Trigga lokala effekter (ljud, partiklar, skärmskakning?)
            // Debug.Log($"Client {netId}: Received damage effect RPC.");
            OnClientDamaged?.Invoke();
        }

        /// <summary>ClientRpc called by server when the object dies.</summary>
        [ClientRpc]
        private void Rpc_DieEffect()
        {
            // Trigga lokala dödseffekter (explosion, ljud, ragdoll?)
            Debug.Log($"Client {netId}: Received die effect RPC.");
            OnClientDied?.Invoke();
            // Dölj objektet visuellt direkt på klienten?
            Collider col = GetComponent<Collider>(); if (col != null) col.enabled = false;
            Renderer rend = GetComponentInChildren<Renderer>(); if (rend != null) rend.enabled = false;
            // Stäng av health bar
            if (unitReference) unitReference.Deselect(); // För att dölja UI via Unit
            if (buildingReference) buildingReference.Deselect(); // För att dölja UI via Building
        }

        /* // Valfri RPC för att dölja objektet helt på klienten innan Destroy
        [ClientRpc]
        private void Rpc_HideOnDeath() {
             gameObject.SetActive(false); // Eller inaktivera renderers/colliders
        } */
    }
}