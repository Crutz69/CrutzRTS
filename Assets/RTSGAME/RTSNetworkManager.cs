// Assets/RTSGAME/Scripts/Networking/RTSNetworkManager.cs
using Mirror;
using UnityEngine;
using System.Collections.Generic; // För List<>
using System.Linq; // För .ToList() och ev. Shuffle

namespace RTSGAME
{
    // Ärver från Mirror's NetworkManager
    public class RTSNetworkManager : NetworkManager
    {
        [Header("RTS Starting Units")] // Nu bara start-enheter/byggnader
        [Tooltip("Prefab för Townhall som spawnas vid start.")]
        [SerializeField] private GameObject townhallPrefab;
        [Tooltip("Prefab för Construction Worker som spawnas vid start.")]
        [SerializeField] private GameObject workerPrefab;

        [Header("Disconnect Handling")]
        [Tooltip("If true, attempts to replace disconnected players with AI (NOT IMPLEMENTED YET). If false (default), removes player's units.")]
        [SerializeField] private bool replaceWithAI = false; // Starta som false (Cleanup)

        // Lista för att hålla spawn points (bara på servern)
        private List<Transform> availableSpawnPoints = new List<Transform>();
        private List<Transform> allSpawnPoints = new List<Transform>(); // Bra att ha en kopia

        // --- Mirror Overrides ---

        public override void OnStartServer()
        {
            base.OnStartServer();
            Debug.Log("RTS Network Manager: Server Started!");

            // Hitta och förbered spawn points när servern startar
            PrepareSpawnPoints();

            // Ingen kristall-spawning här längre
        }

        public override void OnStopServer()
        {
            Debug.Log("RTS Network Manager: Server Stopped!");
            // TODO: Server shutdown cleanup
            base.OnStopServer();
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            // Hitta en startposition FÖRST
            Transform spawnPoint = GetNextSpawnPoint(conn);
            GameObject playerGO = spawnPoint != null
                ? Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation) // Använd spawn pointens pos/rot
                : Instantiate(playerPrefab); // Fallback om inga spawn points finns

            // Viktigt: Lägg till spelaren till anslutningen INNAN du fortsätter
            NetworkServer.AddPlayerForConnection(conn, playerGO);

            NetworkPlayer newPlayer = conn.identity?.GetComponent<NetworkPlayer>();
            if (newPlayer == null)
            {
                Debug.LogError($"Player object for connection {conn.connectionId} missing NetworkPlayer script! Destroying.");
                NetworkServer.Destroy(playerGO); // Städa upp om fel
                return;
            }

            Debug.Log($"Player {conn.connectionId} connected, NetworkPlayer object: {newPlayer.gameObject.name}. Initializing...");

            uint ownerId = newPlayer.netId;
            // TODO: Robust team/color assignment
            int teamId = (NetworkServer.connections.Count % 2) + 1;
            Color playerColor = (teamId == 1) ? Color.blue : Color.red;
            string playerName = $"Player [{ownerId}]";

            newPlayer.Server_SetName(playerName);
            newPlayer.Server_SetTeam(teamId);
            newPlayer.Server_SetColor(playerColor);

            // Registrera hos managers
            PlayerManager.Instance?.Server_RegisterPlayer(newPlayer);
            int startCredits = 4000; int startMana = 0; int startMaxMana = 100; // Startvärden
            ResourceManager.Instance?.Server_RegisterPlayer(ownerId, startCredits, startMana, startMaxMana);

            // Spawna Startenheter vid spelarens spawn point
            Server_SpawnStartingUnits(conn, newPlayer, spawnPoint != null ? spawnPoint.position : Vector3.zero);

            Debug.Log($"Initialization complete for {playerName} at {spawnPoint?.position ?? Vector3.zero}.");
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            Debug.Log($"Player {conn.connectionId} disconnected.");

            NetworkPlayer player = conn.identity?.GetComponent<NetworkPlayer>();
            uint playerNetId = player != null ? player.netId : 0;
            Transform usedSpawnPoint = player?.transform; // Spelarobjektets position är spawn point? Eller behöver vi lagra separat?

            // Avregistrera från managers
            if (playerNetId != 0)
            {
                PlayerManager.Instance?.Server_UnregisterPlayer(playerNetId);
                ResourceManager.Instance?.Server_UnregisterPlayer(playerNetId);
            }

            // Lägg tillbaka spawn point i poolen om den hittas
            if (usedSpawnPoint != null && allSpawnPoints.Contains(usedSpawnPoint) && !availableSpawnPoints.Contains(usedSpawnPoint))
            {
                availableSpawnPoints.Add(usedSpawnPoint);
                Debug.Log($"Added spawn point {usedSpawnPoint.name} back to available list.");
                // Blanda om igen om random?
                // Shuffle(availableSpawnPoints);
            }


            if (replaceWithAI)
            {
                Debug.LogWarning($"AI Replacement requested for player {playerNetId}, but AI is not implemented. Cleaning up player units instead.");
                Server_CleanupDisconnectedPlayer(conn, playerNetId);
            }
            else
            {
                Debug.Log($"Cleaning up units for disconnected player {playerNetId}.");
                Server_CleanupDisconnectedPlayer(conn, playerNetId);
            }

            // Anropa base sist
            base.OnServerDisconnect(conn);
        }


        // --- Custom Server Logic ---

        [Server]
        private void PrepareSpawnPoints()
        {
            allSpawnPoints = FindObjectsOfType<SpawnPointMarker>()
                                     .Select(marker => marker.transform)
                                     .ToList();
            // Skapa en kopia som vi kan ta bort ifrån
            availableSpawnPoints = new List<Transform>(allSpawnPoints);

            Debug.Log($"Found {allSpawnPoints.Count} spawn points.");

            // TODO: Implementera logik för att blanda listan om "Random" är valt
            Shuffle(availableSpawnPoints); // Blanda de tillgängliga direkt
        }

        [Server]
        private Transform GetNextSpawnPoint(NetworkConnectionToClient conn)
        {
            // TODO: Implementera logik för Fast vs Slumpmässig spawn / val från lobby

            if (availableSpawnPoints.Count == 0)
            {
                Debug.LogError("No available spawn points left!");
                // Fallback: använd en default position eller NetworkManager position
                return transform;
            }

            // Ta första lediga punkten från den (potentiellt blandade) listan
            Transform spawnPoint = availableSpawnPoints[0];
            availableSpawnPoints.RemoveAt(0);

            Debug.Log($"Assigning spawn point {spawnPoint.name} to connection {conn.connectionId}");
            return spawnPoint;
        }

        // Helper för att blanda en lista (Fisher-Yates shuffle)
        [Server]
        private void Shuffle<T>(List<T> list)
        {
            System.Random rng = new System.Random();
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        [Server]
        void Server_SpawnStartingUnits(NetworkConnectionToClient conn, NetworkPlayer player, Vector3 startPos)
        {
            if (player == null) return;
            uint ownerId = player.netId;
            int teamId = player.teamID; // Eller factionId om det finns separat

            // Spawna Townhall vid startPos
            if (townhallPrefab != null)
            {
                GameObject townhallGO = Instantiate(townhallPrefab, startPos, Quaternion.identity);
                Townhall townhallScript = townhallGO.GetComponent<Townhall>();
                if (townhallScript != null) townhallScript.Server_InitializeBuilding(ownerId, teamId, BuildingState.Operational);
                NetworkServer.Spawn(townhallGO, conn); // Ge ägarskap
            }
            else { Debug.LogError("Townhall Prefab not assigned!"); }

            // Spawna Worker bredvid Townhall
            if (workerPrefab != null)
            {
                Vector3 workerPos = startPos + Vector3.right * 3 + Vector3.forward * 1; // Exempel offset
                if (UnityEngine.AI.NavMesh.SamplePosition(workerPos, out UnityEngine.AI.NavMeshHit hit, 5.0f, UnityEngine.AI.NavMesh.AllAreas)) { workerPos = hit.position; }
                GameObject workerGO = Instantiate(workerPrefab, workerPos, Quaternion.identity);
                ConstructionWorker workerScript = workerGO.GetComponent<ConstructionWorker>();
                if (workerScript != null) workerScript.Server_InitializeUnit(ownerId); // Sätt ägare
                NetworkServer.Spawn(workerGO, conn); // Ge ägarskap
            }
            else { Debug.LogError("Worker Prefab not assigned!"); }
        }

        // --- Disconnect Cleanup Logic ---
        [Server]
        private void Server_CleanupDisconnectedPlayer(NetworkConnectionToClient conn, uint disconnectedPlayerNetId)
        {
            if (disconnectedPlayerNetId == 0 && conn == null) return;
            Debug.Log($"Executing cleanup for player NetId: {disconnectedPlayerNetId} / ConnId: {conn?.connectionId ?? -1}");

            List<NetworkIdentity> objectsToDestroy = new List<NetworkIdentity>();
            List<Building> neutralsToReset = new List<Building>();

            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
            {
                if (identity == null) continue;
                // Kolla om objektet ägs av den bortkopplade anslutningen ELLER har spelarens netId som ägare
                // (undantag: spelarobjektet själv förstörs av base.OnServerDisconnect)
                bool ownedByDisconnectedPlayer = (conn != null && identity.connectionToClient == conn && identity != conn.identity) || // Ägs av anslutningen (inte spelarobjektet)
                                                (disconnectedPlayerNetId != 0 && IsOwnedByNetId(identity, disconnectedPlayerNetId)); // Ägs via ownerNetId

                if (ownedByDisconnectedPlayer)
                {
                    Building building = identity.GetComponent<Building>();
                    if (building != null && building.originalFactionID == 0)
                    { // Antag Faction 0 = Neutral
                        neutralsToReset.Add(building);
                    }
                    else
                    {
                        objectsToDestroy.Add(identity);
                    }
                }
            }

            foreach (Building neutralBuilding in neutralsToReset)
            {
                Debug.Log($"Resetting neutral building {neutralBuilding.BuildingName} (NetId: {neutralBuilding.netId})");
                neutralBuilding.Server_ChangeOwner(0); // Kräver public Server_ChangeOwner
            }
            foreach (NetworkIdentity objToDestroy in objectsToDestroy)
            {
                if (objToDestroy != null)
                {
                    Debug.Log($"Destroying object {objToDestroy.gameObject.name} (NetId: {objToDestroy.netId}) owned by disconnected player {disconnectedPlayerNetId}.");
                    NetworkServer.Destroy(objToDestroy.gameObject);
                }
            }
            Debug.Log($"Cleanup finished for player {disconnectedPlayerNetId}.");
        }

        // Helper för att kolla ägarskap via ownerNetId på komponenter
        [Server]
        private bool IsOwnedByNetId(NetworkIdentity identity, uint checkNetId)
        {
            if (checkNetId == 0) return false;
            Unit unit = identity.GetComponent<Unit>();
            // Unit.ownerNetId ÄR public, så denna rad är OK
            if (unit != null && unit.ownerNetId == checkNetId) return true;

            Building building = identity.GetComponent<Building>();
            // ÄNDRA HÄR: Använd publik property OwnerNetId
            if (building != null && building.OwnerNetId == checkNetId) return true; // <-- ÄNDRAD TILL OwnerNetId

            // Lägg till fler typer om det behövs
            return false;
        }

    } // End class RTSNetworkManager
} // End namespace RTSGAME