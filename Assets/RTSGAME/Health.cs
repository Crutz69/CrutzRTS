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

        [SyncVar(hook = nameof(OnHealthChanged_Hook))]
        private float currentHealth;

        // Properties
        public float MaxHealth => maxHealth;
        public float CurrentHealth => currentHealth;
        public bool IsDead => currentHealth <= 0;

        // Events
        [Header("Events")]
        public UnityEvent OnServerDied = new UnityEvent();
        public UnityEvent OnClientDied = new UnityEvent();
        public UnityEvent OnClientDamaged = new UnityEvent();

        // Referenser
        private Unit unitReference;
        private Building buildingReference;

        private void Awake()
        {
            unitReference = GetComponent<Unit>();
            buildingReference = GetComponent<Building>();
        }

        // --- Server Logic ---

        public override void OnStartServer()
        {
            base.OnStartServer();
            Server_SetInitialHealth();
        }

        [Server]
        public void Server_SetInitialHealth()
        {
            currentHealth = maxHealth;
        }

        // --- Skada & Reparation/Läkning (Server) ---

        [Server]
        public bool Server_TakeDamage(float baseDamage, DamageType damageType, GameObject damageDealer = null) // Uppdaterad för DamageType
        {
            if (currentHealth <= 0 || baseDamage <= 0) return false;

            // Hämta pansartyp
            ArmorType myArmorType = ArmorType.Unarmored; // Default
            Unit unit = GetComponent<Unit>();
            if (unit != null) { myArmorType = unit.ArmorType; } // Antag Unit har ArmorType
            else
            {
                Building building = GetComponent<Building>();
                if (building != null) { myArmorType = building.ArmorType; } // Antag Building har ArmorType
            }

            // Beräkna slutlig skada
            float finalDamage = DamageCalculator.CalculateDamage(baseDamage, damageType, myArmorType); // Använd kalkylatorn

            // Applicera skada
            currentHealth = Mathf.Max(currentHealth - finalDamage, 0f);
            Debug.Log($"{gameObject.name} took {finalDamage} ({damageType} vs {myArmorType}). Health: {currentHealth}/{maxHealth}");

            Rpc_DamageEffect();

            if (currentHealth <= 0)
            {
                Server_Die(/* Skicka ev. med damageDealer eller damageType */);
            }
            return true;
        }

        [Server]
        public void Server_Heal(float amount)
        {
            if (currentHealth <= 0 || amount <= 0 || currentHealth >= maxHealth) return;
            currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
            // Debug.Log($"{gameObject.name} healed {amount}. Health: {currentHealth}/{maxHealth}");
        }

        // ---- NYLIGEN TILLAGD METOD ----
        /// <summary>
        /// Repairs (heals) this object by a certain amount. ONLY callable on the server.
        /// Clamps health at maximum. Does nothing if already dead or at max health.
        /// </summary>
        [Server]
        public void Server_Repair(float amount)
        {
            // Kan inte reparera döda objekt eller om skadan är ogiltig/negativ
            if (currentHealth <= 0 || amount <= 0)
            {
                return;
            }
            // Kan inte reparera om redan full hälsa
            if (currentHealth >= maxHealth)
            {
                return;
            }

            // Applicera reparation (läkning), se till att den inte går över max
            currentHealth = Mathf.Min(currentHealth + amount, maxHealth);

            // Logga eventuellt (kan bli spammy vid kontinuerlig reparation)
            // Debug.Log($"{gameObject.name} (NetId: {netId}) repaired {amount}. Health: {currentHealth}/{maxHealth}");

            // SyncVar-hooken OnHealthChanged_Hook körs automatiskt på klienter.
        }
        // ---- SLUT PÅ NY METOD ----


        // --- Dödshantering (Server) ---
        [Server]
        private void Server_Die(/* DamageType killingBlowType = DamageType.Normal */)
        { // Kan ta emot dödsorsak
            Debug.Log($"{gameObject.name} (NetId: {netId}) Died.");
            OnServerDied?.Invoke();

            // TODO: Skicka RPC baserat på dödsorsak?
            // RpcPlayElementalDeath(killingBlowType);
            // RpcPlaySinkEffect(); etc.
            Rpc_DieEffect(); // Generell RPC för nu

            // Inaktivera komponenter
            Collider col = GetComponent<Collider>(); if (col != null) col.enabled = false;
            UnitMovement mv = GetComponent<UnitMovement>(); if (mv != null) mv.Server_StopMovement();
            // Stoppa andra komponenter...

            // Förstör objektet efter fördröjning
            StartCoroutine(Server_DestroyAfterDelay(3.0f));
        }

        [Server]
        private IEnumerator Server_DestroyAfterDelay(float delay)
        {
            // Rpc_HideOnDeath(); // Dölj visuellt direkt?
            yield return new WaitForSeconds(delay);
            Debug.Log($"Destroying {gameObject.name} (NetId: {netId}) on server.");
            NetworkServer.Destroy(gameObject);
        }


        // --- Client Logic (RPCs & Hooks) ---

        private void OnHealthChanged_Hook(float oldHealth, float newHealth)
        {
            // Uppdatera UI via referens till Unit/Building
            // Gör UpdateHealthBarUI public på Unit/Building
            if (unitReference != null)
            {
                unitReference.UpdateHealthBarUI(oldHealth, newHealth);
            }
            else if (buildingReference != null)
            {
                buildingReference.UpdateHealthBarUI(oldHealth, newHealth);
            }

            // Spela skade-ljud om vi tog skada? OnClientDamaged är kanske bättre.
            // if (newHealth < oldHealth && isLocalPlayer) { /* Spela ljud */ }
        }

        [ClientRpc]
        private void Rpc_DamageEffect()
        {
            // Trigga lokala effekter
            // Debug.Log($"Client {netId}: Received damage effect RPC.");
            OnClientDamaged?.Invoke();
        }

        [ClientRpc]
        private void Rpc_DieEffect()
        {
            // Trigga lokala dödseffekter
            Debug.Log($"Client {netId}: Received die effect RPC.");
            OnClientDied?.Invoke();
            // Dölj objektet visuellt direkt på klienten
            Collider col = GetComponent<Collider>(); if (col != null) col.enabled = false;
            Renderer rend = GetComponentInChildren<Renderer>(); if (rend != null) rend.enabled = false;
            // Stäng av health bar via Unit/Building
            unitReference?.Deselect();
            buildingReference?.Deselect();
            // Inaktivera hela objektet lokalt? Kan ge problem om servern fortfarande refererar det kort.
            // gameObject.SetActive(false);
        }
    }
}