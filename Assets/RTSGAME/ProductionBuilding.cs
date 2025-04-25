// Filnamn: ProductionBuilding.cs
using UnityEngine;
using Mirror;
using System.Collections.Generic;

namespace RTSGAME
{
    // En abstrakt klass för byggnader som KAN producera/köa enheter eller uppgraderingar.
    // Ärver från den vanliga Building-klassen.
    public abstract class ProductionBuilding : Building
    {
        [Header("Production Queue Settings")]
        [Tooltip("Maximum number of items allowed in the queue.")]
        [SerializeField] private int maxQueueSize = 5;

        [Tooltip("Current pause state for the production queue.")]
        [SyncVar(hook = nameof(OnPauseStateChanged))] // Hook för att ev. uppdatera UI direkt
        public BuildPauseState syncCurrentPauseState = BuildPauseState.None;

        [Tooltip("List of buildable IDs currently in the production queue.")]
        // Använd SyncListStruct för struct-baserade köobjekt om du behöver mer data per köplats
        public readonly SyncList<string> syncBuildQueueIds = new SyncList<string>();

        [Tooltip("The ID of the item currently being produced.")]
        [SyncVar(hook = nameof(OnCurrentlyBuildingChanged))] // Hook för att ev. uppdatera UI direkt
        public string syncCurrentlyBuildingId = string.Empty;

        [Tooltip("Progress (0-1) of the item currently being produced.")]
        [SyncVar(hook = nameof(OnProductionProgressChanged))]
        public float syncCurrentBuildProgress = 0f;

        // --- Properties för kön ---
        public int MaxQueueSize => maxQueueSize;
        public int CurrentQueueCount => syncBuildQueueIds.Count + (string.IsNullOrEmpty(syncCurrentlyBuildingId) ? 0 : 1); // Aktiv + köade
        public bool IsQueueFull => CurrentQueueCount >= maxQueueSize;

        // --- Viktig Metod ---
        /// <summary>
        /// Indikerar att denna byggnadstyp kan köa objekt.
        /// </summary>
        /// <returns>Alltid true för ProductionBuilding.</returns>
        public virtual bool CanQueueItems() // Gör den virtual om en subklass mot förmodan INTE skulle kunna köa
        {
            return true;
        }

        // --- SyncVar Hooks (Exempel - implementera efter behov) ---
        protected virtual void OnPauseStateChanged(BuildPauseState oldState, BuildPauseState newState)
        {
            // Uppdatera UI / Partikeleffekter etc. baserat på paus-status
            // Detta kan behöva anropa UIManager eller ett lokalt UI-skript
            // T.ex. if (UIManager.Instance != null && IsSelected()) UIManager.Instance.UpdateSelectionPanel();
            UpdateProgressBar(); // Uppdatera progress bar för att visa paus?
        }

        protected virtual void OnCurrentlyBuildingChanged(string oldId, string newId)
        {
            // Uppdatera UI när ett nytt objekt börjar produceras
            // T.ex. if (UIManager.Instance != null && IsSelected()) UIManager.Instance.UpdateSelectionPanel();
            UpdateProgressBar();
        }

        protected virtual void OnProductionProgressChanged(float oldProgress, float newProgress)
        {
            // Uppdatera UI (progress bar för produktion)
            UpdateProgressBar(); // Kanske behöver en separat progress bar?
            // T.ex. if (UIManager.Instance != null && IsSelected()) UIManager.Instance.UpdateSelectionPanel(); // Kan vara ineffektivt
        }

        // --- Server-Side Queue Logic (Platzhållare - Måste implementeras!) ---
        [Server]
        public virtual bool Server_TryQueueItem(string buildableId, int quantity)
        {
            // TODO: Implementera logik här!
            // 1. Kolla om buildableId är giltig och kan byggas här.
            // 2. Kolla om kön är full (CurrentQueueCount < maxQueueSize).
            // 3. Kolla om spelaren har råd (hämta kostnad från BuildableDatabase).
            // 4. Dra av kostnad från spelaren (via ResourceManager eller NetworkPlayer).
            // 5. Lägg till buildableId i syncBuildQueueIds (quantity gånger, eller hantera stacks).
            // 6. Starta produktionsprocessen om kön var tom och byggnaden är operational.
            Debug.LogWarning($"Server_TryQueueItem({buildableId}, {quantity}) needs implementation in {this.GetType().Name}!");
            return false; // Returnera true om lyckades, false annars
        }

        [Server]
        public virtual void Server_CancelQueueItem(int queueIndex)
        {
            // TODO: Implementera logik här!
            // 1. Kolla om index är giltigt (0 <= queueIndex < syncBuildQueueIds.Count).
            // 2. Hämta buildableId från syncBuildQueueIds[queueIndex].
            // 3. Ge tillbaka resurser till spelaren (hämta kostnad från BuildableDatabase).
            // 4. Ta bort objektet från syncBuildQueueIds.
            Debug.LogWarning($"Server_CancelQueueItem({queueIndex}) needs implementation in {this.GetType().Name}!");
        }

        [Server]
        protected virtual void Server_TickProduction()
        {
            // TODO: Implementera logik som körs varje sekund/tick på servern
            // 1. Kolla om vi producerar något (!string.IsNullOrEmpty(syncCurrentlyBuildingId)).
            // 2. Kolla om vi är operational och inte pausade (CurrentState == BuildingState.Operational && syncCurrentPauseState == BuildPauseState.None).
            // 3. Öka syncCurrentBuildProgress baserat på tid (Time.deltaTime / produktionstid).
            // 4. Om syncCurrentBuildProgress >= 1:
            //    a. Slutför produktionen (skapa enheten/applicera uppgraderingen).
            //    b. Nollställ syncCurrentBuildProgress.
            //    c. Hämta nästa objekt från syncBuildQueueIds till syncCurrentlyBuildingId.
            //    d. Om kön är tom, sätt syncCurrentlyBuildingId till string.Empty.
        }

        // Se till att anropa Server_TickProduction regelbundet från Update() på servern
        protected virtual void Update()
        {
            if (isServer)
            {
                Server_TickProduction();
            }
        }

        // Override Halt/Resume för att även pausa/återuppta produktion
        [Server]
        protected override void HaltFunctionality()
        {
            base.HaltFunctionality(); // Kör basklassens logik
                                      // TODO: Pausa produktionen om den inte redan är pausad manuellt?
                                      // if (syncCurrentPauseState == BuildPauseState.None)
                                      // {
                                      //    syncCurrentPauseState = BuildPauseState.Resource; // Eller liknande
                                      // }
            Debug.Log($"{BuildingName} halting production due to lack of power.");
        }

        [Server]
        protected override void ResumeFunctionality()
        {
            base.ResumeFunctionality(); // Kör basklassens logik
            // TODO: Återuppta produktionen om den pausades pga resursbrist?
            // if (syncCurrentPauseState == BuildPauseState.Resource)
            // {
            //     syncCurrentPauseState = BuildPauseState.None;
            // }
            Debug.Log($"{BuildingName} resuming production as power is restored.");
        }

    }
}