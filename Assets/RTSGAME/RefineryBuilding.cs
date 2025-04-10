// Assets/RTSGAME/Scripts/Buildings/RefineryBuilding.cs
using UnityEngine;
using Mirror;
using System.Collections;

namespace RTSGAME
{
    // Ärver från vår Building basklass
    public class RefineryBuilding : Building
    {
        [Header("Refinery Settings")]
        [Tooltip("Hur lång tid i sekunder tar avlastningsprocessen?")]
        [SerializeField] private float unloadDuration = 2.0f; // Kortare tid kanske?
        [Tooltip("Specifik punkt där harvestern ska docka.")]
        [SerializeField] public Transform dockingPoint; // Gör public så Harvester kan hitta den? Eller via metod?

        // Synkroniserad variabel för att visa om den är upptagen
        [SyncVar(hook = nameof(OnUnloadingStateChanged))]
        private bool isCurrentlyUnloading = false;

        // Referens till Harvester som just nu lastar av (server-side)
        private uint currentUnloaderNetId = 0;


        // --- Mirror Callbacks & Unity ---

        // Awake och Start från Building.cs körs också

        public override void OnStartServer()
        {
            base.OnStartServer();
            isCurrentlyUnloading = false; // Säkerställ startvärde
            currentUnloaderNetId = 0;
        }

        protected override void Awake()
        {
            base.Awake(); // Anropa basklassens Awake först!
            if (dockingPoint == null)
            {
                Debug.LogWarning($"RefineryBuilding {gameObject.name} saknar dockingPoint. Använder centrum.", this);
                dockingPoint = this.transform;
            }
        }

        // --- SyncVar Hook (Client-side) ---

        void OnUnloadingStateChanged(bool oldState, bool newState)
        {
            // Används för att styra animationer/visuella effekter på klienten
            // Debug.Log($"Refinery {buildingName} unloading state changed to: {newState}");
            // TODO: Starta/stoppa kran-animation? Visa "busy" ikon?
            // craneAnimator?.SetBool("IsUnloading", newState);
        }


        // --- Server-Side Interaction ---

        /// <summary>
        /// Called by the Server when a Harvester attempts to deposit resources.
        /// </summary>
        /// <param name="harvesterIdentity">The NetworkIdentity of the depositing harvester.</param>
        /// <param name="load">The amount of resources the harvester carries.</param>
        /// <param name="type">The type of crystal the harvester carries.</param>
        /// <returns>True if the deposit process was started, false if the refinery was busy.</returns>
        [Server]
        public bool Server_RequestDeposit(NetworkIdentity harvesterIdentity, int load, CrystalType type)
        {
            // Kan bara ta emot om vi är operationella och har ström
            if (CurrentState != BuildingState.Operational || isCurrentlyUnloading)
            {
                // Debug.Log($"Refinery {buildingName} is busy or not operational. Denying deposit from {harvesterIdentity?.netId ?? 0}.");
                return false; // Upptaget eller avstängt
            }
            if (harvesterIdentity == null || load <= 0)
            {
                return false; // Ogiltig förfrågan
            }

            // Acceptera förfrågan
            isCurrentlyUnloading = true; // Sätt upptagen-flagga (synkas till klienter)
            currentUnloaderNetId = harvesterIdentity.netId; // Spara vem som lastar av
            Debug.Log($"Refinery {BuildingName} (NetId: {netId}): Starting unload for Harvester {currentUnloaderNetId}. Load: {load}, Type: {type}");

            // Starta server-coroutine för urlastning
            StartCoroutine(Server_UnloadProcess(harvesterIdentity, load, type));

            // TODO: Starta kran-animation etc. via RPC? Eller via SyncVar hook?
            // Rpc_StartUnloadVisuals();

            return true; // Processen startad
        }

        [Server]
        private IEnumerator Server_UnloadProcess(NetworkIdentity harvesterIdentity, int load, CrystalType type)
        {
            uint harvesterNetId = harvesterIdentity.netId; // Spara ID ifall objektet förstörs

            // Vänta unloadDuration
            yield return new WaitForSeconds(unloadDuration);

            // Hitta harvester-objektet igen via ID för att säkerställa att det finns kvar
            HarvesterUnit harvesterScript = null;
            if (NetworkServer.spawned.TryGetValue(harvesterNetId, out NetworkIdentity currentHarvesterIdentity))
            {
                harvesterScript = currentHarvesterIdentity.GetComponent<HarvesterUnit>();
            }

            // Kolla om harvestern fortfarande finns och är i rätt state (Depositing)
            // Viktigt: Kontrollen av state kanske är onödig om Harvester bara väntar passivt.
            if (harvesterScript != null /*&& harvesterScript.currentState == HarvesterUnit.HarvesterState.Depositing*/)
            {
                // Beräkna värdet
                int valuePerCrystal = Server_GetValueForCrystalType(type); // Hjälpmetod
                int totalValue = load * valuePerCrystal;

                // Ge resurser till spelaren som äger harvestern via ResourceManager
                if (ResourceManager.Instance != null && totalValue > 0)
                {
                    // Hämta ägar-ID från harvestern
                    uint harvesterOwnerId = harvesterScript.ownerNetId; // Antag att Harvester (från Unit) har detta
                    if (harvesterOwnerId != 0)
                    {
                        ResourceManager.Instance.Server_AddCredits(harvesterOwnerId, totalValue);
                        Debug.Log($"Refinery {BuildingName} added {totalValue} credits to player {harvesterOwnerId}.");
                    }
                    else { Debug.LogWarning($"Refinery {BuildingName}: Depositing harvester {harvesterNetId} has no owner!"); }
                }
                else { Debug.LogError($"Refinery {BuildingName}: ResourceManager not found or value was zero!"); }

                // Meddela harvesterns server-objekt att den är klar
                harvesterScript.Server_AcknowledgeDepositComplete();
            }
            else
            {
                Debug.LogWarning($"Refinery {BuildingName}: Harvester {harvesterNetId} was not found or ready when unload finished.", this);
            }

            Debug.Log($"Refinery {BuildingName}: Unload complete for Harvester {harvesterNetId}.");
            isCurrentlyUnloading = false; // Bli ledig igen
            currentUnloaderNetId = 0; // Rensa vem som lastade av

            // TODO: Stoppa kran-animation via RPC? Eller hook.
            // Rpc_StopUnloadVisuals();
        }


        // --- Hjälpmetoder ---

        [Server] // Denna logik bör ligga på servern
        private int Server_GetValueForCrystalType(CrystalType type)
        {
            // Hämta från en central datakälla eller använd fasta värden
            switch (type)
            {
                case CrystalType.Green: return 100;
                case CrystalType.Blue: return 250;
                case CrystalType.Red: return 500;
                default: return 0;
            }
        }


        // --- Visuals / Gizmos ---
        void OnDrawGizmosSelected()
        {
            if (dockingPoint != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(dockingPoint.position, 0.5f);
                Gizmos.DrawLine(transform.position, dockingPoint.position);
#if UNITY_EDITOR
                UnityEditor.Handles.Label(dockingPoint.position + Vector3.up * 0.5f, "Docking Point");
#endif
            }
            // Visa om upptagen
            Gizmos.color = isCurrentlyUnloading ? Color.red : Color.green;
            Collider col = GetComponent<Collider>();
            if (col != null) { Gizmos.DrawWireCube(col.bounds.center, col.bounds.size * 1.1f); }
        }

        // TODO: RPCs för att starta/stoppa visuella effekter om nödvändigt
        // [ClientRpc] void Rpc_StartUnloadVisuals() { ... }
        // [ClientRpc] void Rpc_StopUnloadVisuals() { ... }
    }
}