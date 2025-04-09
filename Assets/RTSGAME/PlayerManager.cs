// Assets/RTSGAME/Scripts/Managers/PlayerManager.cs
using Mirror;
using System.Collections.Generic;
using UnityEngine;

namespace RTSGAME
{
    // Struct för att eventuellt synka grundläggande spelarinfo till klienter (för UI etc.)
    public struct SyncedPlayerData
    {
        public uint netId;
        public string playerName;
        public int teamId;
        public Color color;
        public PlayerStatus status;
        // Lägg till fler fält om nödvändigt
    }


    public class PlayerManager : NetworkBehaviour // NetworkBehaviour för att enkelt vara server-auktoritativ
    {
        public static PlayerManager Instance { get; private set; }

        // Server-side dictionary: netId -> NetworkPlayer script
        private readonly Dictionary<uint, NetworkPlayer> server_Players = new Dictionary<uint, NetworkPlayer>();

        // Synkad lista för att skicka grundläggande info till klienter (valfritt men ofta användbart)
        // OBS: Structs i SyncList kräver custom read/write funktioner i Mirror
        // public readonly SyncList<SyncedPlayerData> client_SyncedPlayers = new SyncList<SyncedPlayerData>();

        // Event som triggas på servern när spelare ansluter/lämnar
        public static event System.Action<NetworkPlayer> ServerOnPlayerAdded;
        public static event System.Action<NetworkPlayer> ServerOnPlayerRemoved;

        void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }


        // --- Server-Side Logic ---

        // Anropas av NetworkManagerCustom när en spelare är redo i spelet
        [Server]
        public void Server_RegisterPlayer(NetworkPlayer newPlayer)
        {
            if (newPlayer == null || newPlayer.netId == 0) return;

            uint netId = newPlayer.netId;
            if (!server_Players.ContainsKey(netId))
            {
                server_Players.Add(netId, newPlayer);
                // TODO: Tilldela TeamID och Färg här (eller i GameManager vid matchstart)
                // Server_AssignInitialTeamAndColor(newPlayer);

                // Uppdatera den synkade listan för klienterna (om den används)
                // UpdateSyncedPlayerList();

                ServerOnPlayerAdded?.Invoke(newPlayer); // Trigga server-event
                Debug.Log($"PlayerManager: Registered player {newPlayer.playerName} (NetId: {netId}). Total players: {server_Players.Count}");

                // Registrera hos ResourceManager
                // ResourceManager.Instance?.Server_RegisterPlayer(netId, startCredits, startMana, maxMana);
            }
        }

        [Server]
        public void Server_UnregisterPlayer(uint netId)
        {
            if (server_Players.TryGetValue(netId, out NetworkPlayer removedPlayer))
            {
                server_Players.Remove(netId);
                // UpdateSyncedPlayerList(); // Uppdatera synkad lista

                ServerOnPlayerRemoved?.Invoke(removedPlayer); // Trigga server-event
                Debug.Log($"PlayerManager: Unregistered player {removedPlayer.playerName} (NetId: {netId}). Remaining players: {server_Players.Count}");

                // Avregistrera från ResourceManager
                // ResourceManager.Instance?.Server_UnregisterPlayer(netId);
            }
        }

        // Hämta ett specifikt NetworkPlayer script (server-side)
        [Server]
        public NetworkPlayer GetPlayer(uint netId)
        {
            server_Players.TryGetValue(netId, out NetworkPlayer player);
            return player;
        }

        // Hämta alla NetworkPlayer scripts (server-side)
        [Server]
        public List<NetworkPlayer> GetAllPlayers()
        {
            return new List<NetworkPlayer>(server_Players.Values);
        }


        // Exempel på funktion för att tilldela lag/färg (anropas av GameManager?)
        [Server]
        public void Server_AssignInitialTeamsAndColors()
        {
            // TODO: Implementera logik för lagtilldelning och färgtilldelning
            // Gå igenom server_Players listan och sätt teamID/playerColor på varje NetworkPlayer
            // Exempel: var playersList = GetAllPlayers(); for(int i=0; i<playersList.Count; i++) { ... }
            Debug.Log("Assigning teams and colors...");
            // Glöm inte att uppdatera den synkade listan om den används
            // UpdateSyncedPlayerList();
        }


        // --- Synkronisering till Klienter (Exempel med SyncList) ---
        /*
        [Server]
        private void UpdateSyncedPlayerList() {
             client_SyncedPlayers.Clear();
             foreach(var player in server_Players.Values) {
                  client_SyncedPlayers.Add(new SyncedPlayerData {
                       netId = player.netId,
                       playerName = player.playerName,
                       teamId = player.teamID,
                       color = player.playerColor,
                       status = player.status
                  });
             }
        }

        // Klienter kan sedan läsa från client_SyncedPlayers för att visa info i UI
        // Behöver en metod på klienten för att hämta listan eller lyssna på SyncList changes
        public List<SyncedPlayerData> GetClientPlayerList() {
             // Returnera en kopia eller gör listan publik (med försiktighet)
             return new List<SyncedPlayerData>(client_SyncedPlayers);
        }
        */

    }
}