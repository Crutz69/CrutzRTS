// Assets/RTSGAME/Scripts/Buildings/Townhall.cs
using Mirror;
using UnityEngine;
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

        public int CurrentTier => currentTier;
        public int MaxTier => maxTier;


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
            // Använd CurrentState (stort C) här:
            if (CurrentState != BuildingState.Operational || IsBeingCaptured) // IsBeingCaptured är en property, redan korrekt
            {
                // Använd BuildingName (stort B) här:
                Debug.LogWarning($"Townhall {BuildingName} cannot upgrade tier while not operational or being captured.");
                // requestingPlayer.Target_NotifyUpgradeFailed("Townhall busy or not operational."); // Exempel RPC
                return;
            }
            if (currentTier >= maxTier)
            {
                // Använd BuildingName (stort B) här:
                Debug.Log($"Townhall {BuildingName} is already at max tier ({maxTier}).");
                // requestingPlayer.Target_NotifyUpgradeFailed("Already at max tier.");
                return;
            }

            int nextTier = currentTier + 1;
            // ... (hämta kostnad etc.) ...
            int upgradeCostCredits = 500 * nextTier; // Exempel
            int upgradeCostMana = 100 * nextTier;    // Exempel

            if (ResourceManager.Instance != null &&
                ResourceManager.Instance.Server_TrySpendCredits(requestingPlayer.netId, upgradeCostCredits) &&
                ResourceManager.Instance.Server_TrySpendMana(requestingPlayer.netId, upgradeCostMana))
            {
                // Använd BuildingName (stort B) här:
                Debug.Log($"Player {requestingPlayer.netId} started upgrading Townhall {BuildingName} to Tier {nextTier}.");
                // TODO: Starta uppgraderingstimer...
                // När klar: currentTier = nextTier;
            }
            else
            {
                // Använd BuildingName (stort B) här:
                Debug.LogWarning($"Player {requestingPlayer.netId} failed to upgrade Townhall {BuildingName} to Tier {nextTier}. Insufficient resources?");
                // requestingPlayer.Target_NotifyInsufficientResources("Upgrade Cost");
            }
        }

        // TODO: Implementera annan Townhall-specifik logik (t.ex. producera workers?)
    }
}