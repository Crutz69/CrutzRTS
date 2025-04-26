// Fil: ConstructionSite.cs
using UnityEngine;
using Mirror;

namespace RTSGAME
{
    // Kräver NetworkIdentity för att kunna spawnas på nätverket
    [RequireComponent(typeof(NetworkIdentity))]
    public class ConstructionSite : NetworkBehaviour // Viktigt att den ärver NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private Health healthComponent; // Byggarbetsplatser kan oftast attackeras

        [Header("Construction State")]
        [SyncVar] private uint ownerNetId; // Vem äger denna byggarbetsplats?
        public uint OwnerNetId => ownerNetId;
        [SyncVar] private string finalBuildingId; // Vilket BuildableData ID ska byggas klart?

        // SyncVar för att synka progressen till klienter (för progress bar etc.)
        [SyncVar(hook = nameof(OnBuildProgressChanged))]
        private float buildProgress = 0f; // Går från 0.0 till 1.0

        // Server-side cache för data om byggnaden som byggs
        private BuildableData buildingDataCache;
        private float requiredBuildTime = 10f; // Default, bör hämtas från BuildableData

        // Metod som anropas av NetworkPlayer.CmdPlaceBuilding när denna spawnas
        [Server]
        public void InitializeSite(uint ownerId, BuildableData data)
        {
            if (data == null || data.itemType != BuildableItemType.Building)
            {
                Debug.LogError($"[Server] ConstructionSite initialiserad med ogiltig BuildableData (ID: {data?.buildableId}) på objekt {gameObject.name}", gameObject);
                // Förstör denna ogiltiga site direkt
                NetworkServer.Destroy(gameObject);
                return;
            }

            ownerNetId = ownerId;
            finalBuildingId = data.buildableId; // Spara ID:t för den slutliga byggnaden
            buildingDataCache = data; // Spara datan på servern
            requiredBuildTime = data.buildTime; // Hämta byggtiden från datan
            buildProgress = 0f; // Nollställ progress

            // Initiera hälsa om komponenten finns
            if (healthComponent == null) healthComponent = GetComponent<Health>();
            healthComponent?.Server_SetInitialHealth();

            Debug.Log($"[Server] ConstructionSite för '{data.displayName}' (ID: {finalBuildingId}) initialiserad för ägare {ownerId}.");
        }

        // --- TODO: Server-metoder för Byggprocessen ---

        // Anropas av ConstructionWorker när den jobbar på siten
        [Server]
        public void Server_ContributeWork(float workAmount)
        {
            if (buildProgress >= 1f) return; // Redan klar

            if (requiredBuildTime <= 0) requiredBuildTime = 0.1f; // Undvik division med noll
            buildProgress = Mathf.Clamp01(buildProgress + workAmount / requiredBuildTime);

            // Uppdatera ev. hälsa gradvis (om du vill ha det)
            // healthComponent?.Server_SetHealthDirectly(Mathf.Lerp(1, healthComponent.MaxHealth, buildProgress));

            if (buildProgress >= 1f)
            {
                Server_CompleteConstruction();
            }
        }

        // Anropas när buildProgress når 1.0
        [Server]
        private void Server_CompleteConstruction()
        {
            Debug.Log($"[Server] Konstruktion klar för {finalBuildingId} på site {netId}!");

            // Hämta data igen (cache kan ha försvunnit?) och prefab för den färdiga byggnaden
            BuildableData finalData = buildingDataCache ?? RTSNetworkManager.singleton.BuildableDB?.GetDataById(finalBuildingId);
            if (finalData == null || finalData.prefabToBuild == null) // prefabToBuild är den färdiga byggnaden
            {
                Debug.LogError($"[Server] Kan inte slutföra konstruktion för {finalBuildingId}. BuildableData eller final prefab saknas!", gameObject);
                NetworkServer.Destroy(gameObject); // Förstör siten om datan är ogiltig
                return;
            }

            // Skapa den FÄRDIGA byggnaden
            GameObject finalBuildingGO = Instantiate(finalData.prefabToBuild, transform.position, transform.rotation);

            // Initiera den färdiga byggnaden
            Building finalBuildingScript = finalBuildingGO.GetComponent<Building>();
            if (finalBuildingScript != null)
            {
                // Initiera som Operational direkt, faction ID kan behöva justeras
                finalBuildingScript.Server_InitializeBuilding(ownerNetId, 0, BuildingState.Operational);
            }
            else { Debug.LogError($"[Server] Slutlig byggnadsprefab '{finalData.prefabToBuild.name}' saknar Building script!"); }

            // Spawna den färdiga byggnaden på nätverket för ägaren
            // Använd connectionToClient från ConstructionSite (som sattes när den spawnades)
            NetworkServer.Spawn(finalBuildingGO, connectionToClient);

            // Förstör denna byggarbetsplats
            NetworkServer.Destroy(gameObject);
        }


        // --- Hook för klienter ---
        private void OnBuildProgressChanged(float oldProgress, float newProgress)
        {
            // Klient-sida: Uppdatera utseende baserat på progress
            // T.ex. byt modell, visa progress bar, spela ljudeffekt
            // Debug.Log($"[Client] Site {netId} progress: {newProgress:P0}");
            // UpdateVisualsBasedOnProgress(newProgress); // Exempel på metod
        }

        // TODO: Lägg till metod för att uppdatera visuellt på klienten
        // private void UpdateVisualsBasedOnProgress(float progress) { ... }

    } // Slut på klass ConstructionSite
} // Slut på namespace RTSGAME