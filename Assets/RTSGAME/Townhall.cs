// Assets/RTSGAME/Scripts/Buildings/Townhall.cs
using Mirror;
using UnityEngine;
// using System.Collections.Generic; // Borttagen då List<> inte används för spawn points längre

// Lägg till andra using-satser vid behov

namespace RTSGAME // Samma namespace som Building, NetworkPlayer etc.
{
    // Townhall ärver från vår Building-basklass
    public class Townhall : Building
    {
        [Header("Townhall Specifics")]
        [Tooltip("The current tech tier of this Townhall.")]
        [SyncVar(hook = nameof(OnTierChanged))]
        private int currentTier = 1;

        [Tooltip("Maximum tier this Townhall can reach.")]
        [SerializeField] private int maxTier = 3;
        // TODO: Lägg till kostnader och tider för tier-uppgraderingar

        [Header("Worker Production")]
        [Tooltip("Prefab for the worker unit to spawn.")]
        [SerializeField] private GameObject workerPrefab; // Dra Worker-prefaben hit i Inspektorn

        [Tooltip("The specific empty GameObject used as the spawn point.")]
        [SerializeField] private Transform spawnPoint; // Dra ditt tomma spawn-punktsobjekt hit i Inspektorn (ENDAST EN)


        // --- Properties ---
        public int CurrentTier => currentTier;
        public int MaxTier => maxTier;

        // --- Unity Lifecycle & Server Start ---
        public override void OnStartServer()
        {
            base.OnStartServer(); // Anropa basklassens metod om den finns

            // Validera att workerPrefab är satt
            if (workerPrefab == null)
            {
                Debug.LogError($"Worker Prefab är inte satt på Townhall {BuildingName}!", this);
            }
            // Validera att den enda spawnPoint är satt
            if (spawnPoint == null)
            {
                Debug.LogError($"Spawn Point är inte satt på Townhall {BuildingName}! Kan inte spawna workers.", this);
            }
        }


        // --- SyncVar Hooks ---
        void OnTierChanged(int oldTier, int newTier)
        {
            Debug.Log($"Townhall {BuildingName} tier changed to {newTier}");
            // TODO: Uppdatera UI eller lås upp saker på klienten?
        }


        // --- Server-Side Logic ---

        // Metod som anropas av NetworkPlayer.CmdUpgradeTier
        [Server]
        public void Server_AttemptTierUpgrade(NetworkPlayer requestingPlayer)
        {
            // Kontrollera ägarskap och byggnadsstatus
            if (requestingPlayer.netId != OwnerNetId)
            {
                Debug.LogWarning($"Player {requestingPlayer.netId} attempted to upgrade Townhall {BuildingName} they don't own.");
                return;
            }

            if (CurrentState != BuildingState.Operational || IsBeingCaptured)
            {
                Debug.LogWarning($"Townhall {BuildingName} cannot upgrade tier while not operational or being captured.");
                return;
            }
            if (currentTier >= maxTier)
            {
                Debug.Log($"Townhall {BuildingName} is already at max tier ({maxTier}).");
                return;
            }

            int nextTier = currentTier + 1;
            int upgradeCostCredits = 500 * nextTier; // Exempel
            int upgradeCostMana = 100 * nextTier;    // Exempel

            if (ResourceManager.Instance != null &&
                ResourceManager.Instance.Server_TrySpendCredits(requestingPlayer.netId, upgradeCostCredits) &&
                ResourceManager.Instance.Server_TrySpendMana(requestingPlayer.netId, upgradeCostMana))
            {
                Debug.Log($"Player {requestingPlayer.netId} started upgrading Townhall {BuildingName} to Tier {nextTier}.");
                // TODO: Starta uppgraderingstimer...
            }
            else
            {
                Debug.LogWarning($"Player {requestingPlayer.netId} failed to upgrade Townhall {BuildingName} to Tier {nextTier}. Insufficient resources?");
            }
        }

        // --- Worker Production (Using a Single Predefined Spawn Point) ---

        [Server]
        public void Server_ProduceWorker(NetworkPlayer requestingPlayer)
        {
            // --- Förutsättningar ---
            if (workerPrefab == null)
            {
                Debug.LogError($"Townhall {BuildingName} cannot produce worker, prefab not set!", this);
                return;
            }
            if (spawnPoint == null) // Kontrollera den enskilda spawn-punkten
            {
                Debug.LogError($"Townhall {BuildingName} cannot produce worker, the 'spawnPoint' variable is not assigned!", this);
                return; // Kan inte spawna utan punkten
            }

            if (requestingPlayer.netId != OwnerNetId)
            {
                Debug.LogWarning($"Player {requestingPlayer.netId} attempted to produce worker from Townhall {BuildingName} they don't own.");
                return;
            }

            if (CurrentState != BuildingState.Operational || IsBeingCaptured)
            {
                Debug.LogWarning($"Townhall {BuildingName} cannot produce worker while not operational or being captured.");
                return;
            }

            // TODO: Lägg till eventuell supply/unit cap check här

            // --- Resurskostnad ---
            int workerCostCredits = 50; // Exempelkostnad
            int workerCostMana = 0;     // Exempelkostnad

            if (ResourceManager.Instance == null ||
                !ResourceManager.Instance.Server_TrySpendCredits(requestingPlayer.netId, workerCostCredits) ||
                !ResourceManager.Instance.Server_TrySpendMana(requestingPlayer.netId, workerCostMana))
            {
                Debug.LogWarning($"Player {requestingPlayer.netId} failed to produce worker from Townhall {BuildingName}. Insufficient resources?");
                // requestingPlayer.Target_NotifyInsufficientResources("Worker Cost");
                return;
            }

            // --- Hitta Spawn-Position (Från den enda spawnPoint) ---

            // Hämta position och rotation direkt från den tilldelade spawnPoint
            Vector3 positionToSpawn = spawnPoint.position;
            Quaternion rotationToSpawn = spawnPoint.rotation; // Använd rotationen från spawn-punkten


            // --- Skapa och Spawna Worker ---
            // Skapa worker-instansen på den valda positionen och rotationen
            GameObject workerInstance = Instantiate(workerPrefab, positionToSpawn, rotationToSpawn);

            // Spawna objektet över nätverket, ge ägarskap/kontroll till spelaren som begärde det
            NetworkServer.Spawn(workerInstance, requestingPlayer.connectionToClient);

            Debug.Log($"Townhall {BuildingName} produced worker for player {requestingPlayer.netId} at the designated spawn point.");

            // TODO: Potentiellt anropa en metod på den nyskapade workern (t.ex. gå till rally point)
        }

    } // End class Townhall
} // End namespace RTSGAME