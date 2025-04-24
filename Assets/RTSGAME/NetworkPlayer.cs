// Assets/RTSGAME/Scripts/Player/NetworkPlayer.cs
using Mirror;
using UnityEngine;
using System.Collections.Generic; // För List<>
using System.Linq; // För .Select() i exempel

namespace RTSGAME
{
    // Flyttad till Enums.cs? Om inte, definiera den här eller där den används.
    // public enum PlayerStatus { Playing, Defeated, Spectating }

    public class NetworkPlayer : NetworkBehaviour
    {
        [Header("Player Info")]
        [SyncVar(hook = nameof(OnPlayerNameChanged))] public string playerName = "New Player";
        [SyncVar(hook = nameof(OnTeamIDChanged))] public int teamID = 0;
        [SyncVar(hook = nameof(OnColorChanged))] public Color playerColor = Color.grey;

        [Header("Resources (Synced from ResourceManager)")]
        [SyncVar(hook = nameof(OnCreditsChanged))] public int credits = 0; // Startvärde sätts via ResourceManager->RegisterPlayer
        // Mana-poolen kanske du vill behålla som buffert eller för andra system?
        // [SyncVar(hook = nameof(OnManaChanged))] public int mana = 0;
        // [SyncVar(hook = nameof(OnMaxManaChanged))] public int maxMana = 0;

        // NYTT: SyncVars för Mana Upkeep / Power System
        [Tooltip("Total Mana genererad per sekund/tick för denna spelare.")]
        [SyncVar(hook = nameof(OnManaGenerationChanged))] public int manaGeneration;
        [Tooltip("Total Mana upkeep per sekund/tick för denna spelare.")]
        [SyncVar(hook = nameof(OnManaUpkeepChanged))] public int manaUpkeep;
        [Tooltip("Har spelaren tillräckligt med Mana Generation för sin Upkeep?")]
        [SyncVar(hook = nameof(OnPowerStatusChanged))] public bool hasSufficientPower = true; // Startvärde

        [Header("Status")]
        [SyncVar(hook = nameof(OnStatusChanged))] public PlayerStatus status = PlayerStatus.Playing;

        // Referenser till lokala system (sätts i OnStartLocalPlayer)
        private InputManager inputManager;
        private SelectionManager selectionManager;
        private UIManager uiManager; // Antag att UIManager är en Singleton eller lättåtkomlig
        private ManaBarController manaBarController; // Specifik controller för mana-baren?

        // --- Mirror Callbacks ---

        public override void OnStartServer()
        {
            base.OnStartServer();
            // Servern registrerar spelaren hos ResourceManager när spelaren ansluter helt
            // Detta görs ofta via NetworkManager.OnServerAddPlayer -> Player spawn -> Register
            if (ResourceManager.Instance != null)
            {
                // TODO: Hämta korrekta startvärden från t.ex. GameSettings eller LobbyData
                int startCredits = 1000;
                // int startMana = 100;
                // int startMaxMana = 100;
                ResourceManager.Instance.Server_RegisterPlayer(netId, startCredits /*, startMana, startMaxMana*/);
            }
            else
            {
                Debug.LogError($"ResourceManager Instance not found on server when registering player {netId}!");
            }
            Debug.Log($"Player {playerName} (NetId: {netId}) initialized on server.");
        }

        public override void OnStopServer()
        {
            // Avregistrera från ResourceManager när spelaren lämnar
            ResourceManager.Instance?.Server_UnregisterPlayer(netId);
            Debug.Log($"Player {playerName} (NetId: {netId}) stopped on server.");
            base.OnStopServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Debug.Log($"Player {playerName} (NetId: {netId}, Team {teamID}) loaded on client.");
            // Initial UI-uppdatering triggas av SyncVar Hooks när värdena synkas från servern
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            Debug.Log($"Player {playerName} (NetId: {netId}) removed from client.");
            if (isLocalPlayer)
            {
                Debug.Log("Local player disconnected.");
                // Ladda meny scen...
            }
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            gameObject.name = $"LOCAL Player - {playerName} ({netId})";
            Debug.Log($"OnStartLocalPlayer: {playerName} (Team {teamID}, Color {playerColor})");

            // Hitta managers
            inputManager = InputManager.Instance;
            selectionManager = SelectionManager.Instance;
            uiManager = UIManager.Instance; // Antag att denna hanterar Credits etc.
            manaBarController = FindObjectOfType<ManaBarController>(); // Hitta ManaBarController specifikt?

            if (inputManager != null) inputManager.AssignLocalPlayer(this); else Debug.LogError("NetworkPlayer could not find InputManager Instance!");
            if (uiManager != null) uiManager.SetLocalPlayer(this); else Debug.LogError("NetworkPlayer could not find UIManager Instance!");
            if (selectionManager == null) Debug.LogError("NetworkPlayer could not find SelectionManager Instance!");
            if (manaBarController == null) Debug.LogWarning("NetworkPlayer could not find ManaBarController Instance!"); // Varning, kanske OK om mana bar inte finns i alla scener

            // Tvinga en initial uppdatering av UI via hooks, ifall värdena redan är satta
            OnCreditsChanged(0, credits);
            OnManaGenerationChanged(0, manaGeneration);
            OnManaUpkeepChanged(0, manaUpkeep);
            OnPowerStatusChanged(true, hasSufficientPower);
            OnPlayerNameChanged("", playerName);
            OnTeamIDChanged(0, teamID);
            OnColorChanged(Color.clear, playerColor);
            OnStatusChanged(PlayerStatus.Spectating, status); // Använd ett start-state
        }

        // --- Input Handling (Conceptual - Anropas från InputManager / UI-knappar) ---

        // Process-metoderna kan tas bort om UI/InputManager anropar Commands direkt
        // public void ProcessPlaceBuildingRequest(...) { CmdPlaceBuilding(...); }
        // public void ProcessQueueItemRequest(...) { CmdQueueItem(...); }
        // public void ProcessRightClickBuild(...) { CmdHandleRightClickBuild(...); }


        // --- Commands (Called from Client, Run on Server) ---

        // ÄNDRAD: Bytt namn, tar buildableId (string), skapar ConstructionSite
        [Command]
        public void CmdPlaceBuilding(string buildableId, Vector3 position, Quaternion rotation)
        {
            Debug.Log($"Server received place request for {buildableId} from player {netId}");
            if (ResourceManager.Instance == null) { Debug.LogError("ResourceManager missing on server!"); return; }

            BuildableData data = ResourceManager.Instance.GetBuildableDataById(buildableId); // Använd ResourceManager för att hitta data
            if (data == null || data.itemType != BuildableItemType.Building)
            {
                Debug.LogWarning($"Invalid buildableId ({buildableId}) or not a building.");
                // Skicka TargetRpc med felmeddelande? Target_NotifyPlacementFailed("Invalid building type");
                return;
            }

            // TODO: Server-side validation:
            // 1. Har spelaren råd med credits? (ResourceManager.Instance.GetCurrentCredits(netId) >= data.creditCost)
            // OBS: Dra INTE credits här, det görs under konstruktion via pay-over-time på ConstructionSite.
            // 2. Är positionen giltig? (Physics checks, etc.)
            // 3. Uppfylls prerequisites (tech level etc.)?

            bool canAfford = ResourceManager.Instance.GetCurrentCredits(netId) >= data.creditCost; // Bara kolla, inte spendera
            bool positionValid = true; // TODO: Implementera validering
            bool requirementsMet = true; // TODO: Implementera krav-check

            if (canAfford && positionValid && requirementsMet)
            {
                // Hämta ConstructionSite-prefab (kanske via BuildableData eller en mappning)
                GameObject sitePrefab = GetConstructionSitePrefabFor(data); // TODO: Implementera denna funktion
                if (sitePrefab != null)
                {
                    GameObject siteInstance = Instantiate(sitePrefab, position, rotation);
                    // Ge ägarskap till spelaren som placerade
                    NetworkServer.Spawn(siteInstance, connectionToClient);

                    // Initiera byggarbetsplatsen
                    ConstructionSite siteScript = siteInstance.GetComponent<ConstructionSite>(); // Antag script finns
                    siteScript?.InitializeSite(netId, data); // Skicka med ägar-ID och vilken byggnad som ska byggas

                    Debug.Log($"Spawned ConstructionSite for {data.buildableName} for player {netId}.");
                }
                else
                {
                    Debug.LogError($"Could not find ConstructionSite prefab for {data.buildableName}");
                    // Target_NotifyPlacementFailed("Internal server error (prefab missing)");
                }
            }
            else
            {
                Debug.LogWarning($"Placement failed for {data.buildableName}. Afford: {canAfford}, ValidPos: {positionValid}, ReqsMet: {requirementsMet}");
                // Skicka TargetRpc med felmeddelande baserat på orsak
                // if (!canAfford) Target_NotifyInsufficientResources("Credits (for placement)");
                // else Target_NotifyPlacementFailed("Invalid location or requirements not met");
            }
        }


        // ÄNDRAD: Hanterar både Units och Upgrades, tar quantity
        [Command]
        public void CmdQueueItem(uint buildingNetId, string buildableId, int quantity)
        {
            if (quantity <= 0) return;
            if (!NetworkServer.spawned.TryGetValue(buildingNetId, out NetworkIdentity buildingIdentity)) { Debug.LogWarning($"CmdQueueItem: Building {buildingNetId} not found."); return; }

            // Validera ägarskap till byggnaden
            if (buildingIdentity.connectionToClient != connectionToClient)
            {
                Debug.LogWarning($"Player {netId} tried to queue item at building {buildingNetId} they don't own.");
                // TargetRpc_NotifyQueueFailed("Not your building");
                return;
            }

            Building building = buildingIdentity.GetComponent<Building>();
            BuildableData data = ResourceManager.Instance?.GetBuildableDataById(buildableId); // Använd ResourceManager

            if (building == null || data == null) { Debug.LogWarning("CmdQueueItem: Building or BuildableData not found."); return; }
            if (data.itemType == BuildableItemType.Building) { Debug.LogWarning("CmdQueueItem: Cannot queue a building."); return; } // Kan inte köa byggnader

            // TODO: Kolla om byggnaden KAN producera/forska denna itemType (t.ex. Townhall kan inte bygga tanks)
            // TODO: Kolla om spelaren uppfyller prerequisites för item (tech level etc.)

            // Kolla resurskostnad (ENDAST CREDITS)
            int totalCost = data.creditCost * quantity;
            if (!ResourceManager.Instance.Server_HasEnoughCredits(netId, totalCost)) // Kolla om råd med hela batchen direkt? Eller bara en? Designval.
            {
                Debug.LogWarning($"Player {netId} cannot afford to queue {quantity} of {data.buildableName} (Cost: {totalCost})");
                Target_NotifyInsufficientResources("Credits"); // Skicka notis till klienten
                return;
            }

            // Om allt ok, KÖA (kostnad dras antingen vid start av varje item eller pay-over-time för units)
            // Antag att Building har en metod för detta nu
            bool queuedOk = building.Server_QueueItem(buildableId, quantity); // Byggnaden hanterar sin egen kö

            if (!queuedOk)
            {
                Debug.LogWarning($"Building {buildingNetId} failed to queue item {buildableId}.");
                // TargetRpc_NotifyQueueFailed("Queue full or invalid item for building?");
            }
            else
            {
                Debug.Log($"Player {netId} queued {quantity} of {data.buildableName} at building {buildingNetId}.");
            }
        }


        // NYTT: Command för högerklick på bygg-knapp
        [Command]
        public void CmdHandleRightClickBuild(uint buildingNetId, int queueIndex) // queueIndex = -1 för aktiv, >=0 för köad
        {
            if (!NetworkServer.spawned.TryGetValue(buildingNetId, out NetworkIdentity buildingIdentity)) return;
            // Validera ägarskap
            if (buildingIdentity.connectionToClient != connectionToClient) return;

            Building building = buildingIdentity.GetComponent<Building>();
            if (building != null)
            {
                // Anropa server-metoden på byggnaden som hanterar högerklicket
                building.Server_HandleRightClickOnQueue(queueIndex); // Antag att denna metod finns på Building
            }
        }


        // --- Gamla Commands (Granska och anpassa vid behov) ---
        // Se till att ägarkoll och null-checks finns där de behövs

        [Command] void CmdMoveUnits(List<uint> unitNetIds, Vector3 destination) { /* ... ägarkoll + anropa UnitMovement ... */ }
        [Command] void CmdAttackTarget(List<uint> attackerNetIds, NetworkIdentity targetIdentity) { /* ... ägarkoll + anropa UnitCombat ... */ }
        [Command] void CmdSetRallyPoint(NetworkIdentity buildingIdentity, Vector3 position) { /* ... ägarkoll + anropa Building ... */ }
        [Command] void CmdClearRallyPoint(NetworkIdentity buildingIdentity) { /* ... ägarkoll + anropa Building ... */ }
        [Command] void CmdStartCapture(NetworkIdentity workerIdentity, NetworkIdentity targetBuildingIdentity) { /* ... ägarkoll + anropa Building ... */ }
        [Command] void CmdSellBuilding(NetworkIdentity buildingIdentity) { /* ... ägarkoll + anropa Building ... */ }
        [Command] void CmdUpgradeTier(NetworkIdentity townhallIdentity) { /* ... ägarkoll + anropa Townhall ... */ }


        // BORTTAGEN: Server-metoder för resurser finns nu i ResourceManager
        // [Server] public void Server_AwardCredits(int amount) { ... }
        // [Server] public bool Server_TrySpendCredits(int amount) { ... }
        // ... etc ...


        // --- Server Methods (Called by Server logic) ---
        [Server] public void Server_ChangeStatus(PlayerStatus newStatus) { status = newStatus; }
        [Server] public void Server_SetTeam(int newTeamID) { teamID = newTeamID; }
        [Server] public void Server_SetColor(Color newColor) { playerColor = newColor; }
        [Server] public void Server_SetName(string newName) { playerName = newName; }


        // --- ClientRpc & TargetRpc Examples ---
        [ClientRpc] public void RpcAnnounceMessage(string message) { /* ... */ }
        [TargetRpc] public void Target_NotifyInsufficientResources(string resourceName) { /* ... */ }
        // Lägg till fler TargetRpc för specifik feedback:
        // Target_NotifyPlacementFailed(string reason)
        // Target_NotifyQueueFailed(string reason)


        // --- SyncVar Hooks (Called on Clients when value changes) ---

        void OnPlayerNameChanged(string oldName, string newName) { if (isLocalPlayer) gameObject.name = $"LOCAL Player - {newName} ({netId})"; else gameObject.name = $"Remote Player - {newName} ({netId})"; /* Update UI */ }
        void OnTeamIDChanged(int oldTeamID, int newTeamID) { /* Update UI */ }
        void OnColorChanged(Color oldColor, Color newColor) { /* Update UI / Unit Colors? */ }
        void OnCreditsChanged(int oldCredits, int newCredits) { if (isLocalPlayer) uiManager?.UpdateCreditsDisplay(newCredits); }
        void OnStatusChanged(PlayerStatus oldStatus, PlayerStatus newStatus) { if (isLocalPlayer) { /* Hantera Defeat etc. */ } /* Uppdatera Scoreboard? */ }

        // NYTT: Hooks för Mana Upkeep System
        void OnManaGenerationChanged(int oldGen, int newGen)
        {
            if (isLocalPlayer) manaBarController?.UpdateGeneration(newGen); // Anropa din ManaBarController
        }
        void OnManaUpkeepChanged(int oldUpkeep, int newUpkeep)
        {
            if (isLocalPlayer) manaBarController?.UpdateUpkeep(newUpkeep); // Anropa din ManaBarController
        }
        void OnPowerStatusChanged(bool oldStatus, bool newStatus)
        {
            if (isLocalPlayer) manaBarController?.UpdatePowerStatus(newStatus); // Anropa din ManaBarController
                                                                                // Visa kanske en global varning på skärmen om hasSufficientPower blir false?
                                                                                // if (isLocalPlayer && !newStatus) uiManager?.ShowPowerWarning(true);
                                                                                // else if (isLocalPlayer && newStatus) uiManager?.ShowPowerWarning(false);
        }

        // Behåll om du har Mana som pool
        // void OnManaChanged(int oldMana, int newMana) { if (isLocalPlayer) uiManager?.UpdateManaDisplay(newMana, maxMana); }
        // void OnMaxManaChanged(int oldMax, int newMax) { if (isLocalPlayer) uiManager?.UpdateManaDisplay(mana, newMax); }


        // --- Helper Functions ---
        private GameObject GetConstructionSitePrefabFor(BuildableData buildingData)
        {
            // TODO: Implementera logik för att hitta rätt ConstructionSite prefab.
            // Kanske baserat på byggnadens storlek, ras, eller en referens i BuildableData?
            // Returnera null om ingen hittas.
            Debug.LogWarning("GetConstructionSitePrefabFor() needs implementation!");
            return buildingData.ghostPrefab; // Använd ghost som placeholder? Fel prefab dock.
        }
    }
}