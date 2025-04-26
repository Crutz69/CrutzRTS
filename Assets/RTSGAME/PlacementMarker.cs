// Fil: PlacementMarker.cs
using UnityEngine;
using Mirror;

namespace RTSGAME
{
    // Kräver NetworkIdentity för att spawnas och Health för att kunna attackeras/avbrytas?
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(Health))]
    public class PlacementMarker : NetworkBehaviour
    {
        [Header("Data (Set by Server)")]
        [SyncVar] private uint ownerNetId;
        [SyncVar] private string finalBuildingId; // BuildableData ID för den färdiga byggnaden

        [Header("Component Refs")]
        [SerializeField] private Health healthComponent;

        // Properties för enkel åtkomst (valfritt)
        public uint OwnerNetId => ownerNetId;
        public string FinalBuildingId => finalBuildingId;

        void Awake()
        {
            if (healthComponent == null) healthComponent = GetComponent<Health>();
            // TODO: Prenumerera på healthComponent.OnServerDied för att städa upp om markören förstörs?
        }

        /// <summary>
        /// [Server] Anropas av NetworkPlayer.CmdPlaceBuilding när denna markör spawnas.
        /// </summary>
        [Server]
        public void Server_InitializeMarker(uint owner, string buildableId)
        {
            ownerNetId = owner;
            finalBuildingId = buildableId;
            healthComponent?.Server_SetInitialHealth(); // Ge den hälsa direkt
            Debug.Log($"[Server] PlacementMarker initialized for building '{buildableId}' by owner {owner}.");
        }

        /// <summary>
        /// [Server] Anropas av en ConstructionWorker när den anländer och är redo att börja bygga.
        /// Hanterar transformationen från PlacementMarker till ConstructionSite.
        /// </summary>
        [Server]
        public void Server_WorkerArrivedToBuild(NetworkIdentity workerIdentity) // Ta emot worker Identity
        {
            if (workerIdentity == null)
            {
                Debug.LogError($"[Server] Server_WorkerArrivedToBuild called with null worker identity on marker {netId}.");
                return;
            }
            uint workerNetId = workerIdentity.netId; // Hämta worker ID
            Debug.Log($"[Server] Worker {workerNetId} arrived at PlacementMarker {netId} for building {finalBuildingId}.");

            // --- Kärnan i transformationen ---

            // 1. Hämta BuildableData för den slutliga byggnaden
            if (RTSNetworkManager.singleton.BuildableDB == null) { Debug.LogError("[Server] BuildableDatabase missing!"); return; }
            BuildableData data = RTSNetworkManager.singleton.BuildableDB.GetDataById(finalBuildingId);
            if (data == null || data.constructionSitePrefab == null)
            {
                Debug.LogError($"[Server] Cannot find BuildableData or constructionSitePrefab for ID '{finalBuildingId}'. Aborting construction start.", gameObject);
                // TODO: Meddela worker att det misslyckades?
                NetworkServer.Destroy(gameObject); // Förstör markören om datan är felaktig
                return;
            }

            // 2. Instansiera den RIKTIGA ConstructionSite-prefaben
            GameObject siteInstance = Instantiate(data.constructionSitePrefab, transform.position, transform.rotation);

            // 3. Initiera ConstructionSite-scriptet direkt till 'Constructing'
            ConstructionSite siteScript = siteInstance.GetComponent<ConstructionSite>();
            if (siteScript == null)
            {
                Debug.LogError($"[Server] ConstructionSite prefab for '{finalBuildingId}' is missing the ConstructionSite script!", siteInstance);
                Destroy(siteInstance); // Städa upp felaktig instans
                NetworkServer.Destroy(gameObject); // Förstör även markören
                                                   // TODO: Meddela worker?
                return;
            }
            // **VIKTIGT:** Vi hoppar över 'Placing' och går direkt till 'Constructing' här!
            // Notera: InitializeSite behöver ta emot state som parameter om den inte redan gör det.
            siteScript.InitializeSite(this.ownerNetId, data); // Skicka med ägare och data
            // Om InitializeSite sätter state till Placing måste vi ändra det manuellt:
            // siteScript.Server_SetState(BuildingState.Constructing); // Antagande om metod finns

            // 4. Spawna ConstructionSite på nätverket för rätt ägare
            // Använd markörens connectionToClient (som sattes när NetworkPlayer spawnade den)
            NetworkServer.Spawn(siteInstance, this.connectionToClient);
            NetworkIdentity newSiteIdentity = siteInstance.GetComponent<NetworkIdentity>();
            Debug.Log($"[Server] Spawned actual ConstructionSite (NetId: {newSiteIdentity.netId}) for {finalBuildingId}.");


            // 5. Säg åt workern att börja bygga på den *nya* siten
            // Hitta worker-objektet igen (det kan ha flyttat sig lite)
            if (NetworkServer.spawned.TryGetValue(workerNetId, out NetworkIdentity currentWorkerIdentity))
            {
                ConstructionWorker workerScript = currentWorkerIdentity.GetComponent<ConstructionWorker>();
                if (workerScript != null)
                {
                    Debug.Log($"[Server] Ordering worker {workerNetId} to start building new site {newSiteIdentity.netId}");
                    workerScript.Cmd_StartBuilding(newSiteIdentity); // Be workern bygga på den nya siten
                }
                else { Debug.LogWarning($"[Server] Could not find ConstructionWorker script on worker {workerNetId} to give new build order."); }
            }
            else { Debug.LogWarning($"[Server] Could not find worker {workerNetId} to give new build order."); }


            // 6. Förstör denna PlacementMarker
            Debug.Log($"[Server] Destroying PlacementMarker {netId}.");
            NetworkServer.Destroy(gameObject);
        }

        // Hook för att ev. ändra utseende om owner/building ändras (mindre troligt för denna)
        // void OnOwnerChanged(...) {}
        // void OnFinalBuildingIdChanged(...) {}

    } // Slut på klass PlacementMarker
} // Slut på namespace RTSGAME