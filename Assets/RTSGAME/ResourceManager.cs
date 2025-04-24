// Assets/RTSGAME/Scripts/Managers/ResourceManager.cs
using Mirror;
using System.Collections.Generic;
using System.Linq; // För att kunna använda LINQ
using UnityEngine;

namespace RTSGAME
{
    public class ResourceManager : NetworkBehaviour // Singleton NetworkBehaviour
    {
        // --- Singleton Setup ---
        public static ResourceManager Instance { get; private set; }

        // Struct för att hålla resursdata per spelare/AI internt på servern
        private struct PlayerResourceData
        {
            public int credits;
            // Behåll mana/maxMana om du vill ha en mana-pool som buffert/för annat,
            // men själva power-logiken baseras nu på Generation vs Upkeep.
            // public int mana;
            // public int maxMana;

            // NYTT: För Mana Upkeep-systemet
            public int manaGeneration; // Total generering per tidsenhet
            public int manaUpkeep;     // Totalt underhållsbehov per tidsenhet
            public bool hasSufficientPower; // Statusflagga
        }

        // Server-side dictionary för att hålla den auktoritativa resursdatan
        private readonly Dictionary<uint, PlayerResourceData> server_resourceData = new Dictionary<uint, PlayerResourceData>();

        // --- Unity & Mirror Callbacks ---

        public override void OnStartServer()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("Duplicate ResourceManager instance detected on server, disabling self.");
                enabled = false; // Inaktivera scriptet istället för Destroy
                return;
            }
            Instance = this;
            Debug.Log("ResourceManager Server Instance Initialized.");
            // DontDestroyOnLoad(gameObject); // Överväg om den ska överleva scenbyten
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // --- Server-Only Methods ---

        [Server]
        public void Server_RegisterPlayer(uint ownerNetId, int startCredits /*, int startMana, int startMaxMana */) // Start-Mana inte lika relevant?
        {
            if (ownerNetId == 0) return;
            if (!server_resourceData.ContainsKey(ownerNetId))
            {
                server_resourceData.Add(ownerNetId, new PlayerResourceData
                {
                    credits = startCredits,
                    // mana = startMana, // Initiera om du behåller mana-pool
                    // maxMana = startMaxMana, // Initiera om du behåller mana-pool
                    manaGeneration = 0, // NYTT: Startvärden för upkeep-systemet
                    manaUpkeep = 0,     // NYTT:
                    hasSufficientPower = true // NYTT: Anta att man har ström från start
                });
                Debug.Log($"ResourceManager: Registered player {ownerNetId} with {startCredits}C.");
                // Uppdatera NetworkPlayer SyncVars direkt
                // Beräkna initial generation/upkeep baserat på startbyggnader?
                Server_UpdateSinglePlayerPowerAndMana(ownerNetId); // Beräkna och synka initial status
            }
            else
            {
                Debug.LogWarning($"ResourceManager: Player {ownerNetId} already registered.");
            }
        }

        [Server]
        public void Server_UnregisterPlayer(uint ownerNetId)
        {
            if (server_resourceData.Remove(ownerNetId))
            {
                Debug.Log($"ResourceManager: Unregistered player {ownerNetId}.");
            }
        }

        // --- Credits (Oförändrat) ---
        [Server]
        public void Server_AddCredits(uint ownerNetId, int amount)
        {
            if (amount <= 0) return;
            if (server_resourceData.TryGetValue(ownerNetId, out PlayerResourceData data))
            {
                data.credits += amount;
                server_resourceData[ownerNetId] = data;
                Server_UpdateClientResourceValue(ownerNetId, "credits", data.credits); // Synka specifik variabel
            }
        }

        [Server]
        public bool Server_TrySpendCredits(uint ownerNetId, int amount)
        {
            if (amount < 0) return false;
            if (amount == 0) return true;
            if (server_resourceData.TryGetValue(ownerNetId, out PlayerResourceData data))
            {
                if (data.credits >= amount)
                {
                    data.credits -= amount;
                    server_resourceData[ownerNetId] = data;
                    Server_UpdateClientResourceValue(ownerNetId, "credits", data.credits); // Synka specifik variabel
                    return true;
                }
            }
            return false;
        }

        // --- Mana (Ej för kostnader, men kan finnas som pool/buffert?) ---
        // Behåll om du behöver en mana-pool för annat, annars kan de tas bort.
        // [Server] public void Server_AddMana(uint ownerNetId, int amount) { ... }
        // [Server] public bool Server_TrySpendMana(uint ownerNetId, int amount) { ... } // Används EJ för bygg/unit/upgrade-kostnad
        // [Server] public void Server_SetMaxMana(uint ownerNetId, int newMax) { ... }


        // --- NYTT: Metoder för Mana Upkeep & Generation ---

        /// <summary>
        /// Adds or removes mana upkeep cost for a player. Called by Buildings on server.
        /// </summary>
        [Server]
        public void Server_AddOrRemoveManaUpkeep(uint ownerNetId, int upkeepChange)
        {
            if (!server_resourceData.ContainsKey(ownerNetId)) return; // Spelaren måste vara registrerad

            PlayerResourceData data = server_resourceData[ownerNetId];
            data.manaUpkeep += upkeepChange;
            if (data.manaUpkeep < 0) data.manaUpkeep = 0; // Upkeep kan inte vara negativt
            server_resourceData[ownerNetId] = data; // Skriv tillbaka struct

            Debug.Log($"ResourceManager: Player {ownerNetId} Upkeep changed by {upkeepChange}. New Upkeep: {data.manaUpkeep}");

            // Beräkna om och synka power status direkt efter ändring
            Server_UpdateSinglePlayerPowerAndMana(ownerNetId);
        }

        /// <summary>
        /// Adds or removes mana generation for a player. Called by Buildings/Upgrades on server.
        /// </summary>
        [Server]
        public void Server_AddOrRemoveManaGeneration(uint ownerNetId, int generationChange)
        {
            if (!server_resourceData.ContainsKey(ownerNetId)) return;

            PlayerResourceData data = server_resourceData[ownerNetId];
            data.manaGeneration += generationChange;
            if (data.manaGeneration < 0) data.manaGeneration = 0; // Generation kan inte vara negativ
            server_resourceData[ownerNetId] = data;

            Debug.Log($"ResourceManager: Player {ownerNetId} Generation changed by {generationChange}. New Generation: {data.manaGeneration}");

            // Beräkna om och synka power status direkt efter ändring
            Server_UpdateSinglePlayerPowerAndMana(ownerNetId);
        }


        // --- NYTT/ÄNDRAT: Metod för att uppdatera Power Status ---

        /// <summary>
        /// Calculates and updates the power status (Upkeep vs Generation) for a single player.
        /// Also updates building states and syncs relevant values to the client.
        /// </summary>
        [Server]
        private void Server_UpdateSinglePlayerPowerAndMana(uint ownerNetId)
        {
            if (!server_resourceData.ContainsKey(ownerNetId)) return;

            PlayerResourceData data = server_resourceData[ownerNetId];
            int previousGeneration = data.manaGeneration; // Behåll gamla värden för jämförelse
            int previousUpkeep = data.manaUpkeep;
            bool previouslyHadPower = data.hasSufficientPower;

            // --- Återställ och Beräkna om Generation & Upkeep ---
            // Detta är den mest kritiska delen och kräver en extern källa
            // för att veta vilka byggnader spelaren äger och deras värden.
            int currentTotalGeneration = 0;
            int currentTotalUpkeep = 0;
            List<Building> buildingsAffectingPower = new List<Building>();

            // **************************************************************************
            // TODO: HÄMTA ALLA SPELARENS AKTIVA BYGGNADER HÄR!
            // Kräver en BuildingManager, lista på NetworkPlayer, eller liknande.
            // Loopen nedan är PSEUDOKOD tills du har detta system.
            // **************************************************************************
            // Exempel på hur loopen KAN se ut när du har listan:
            // List<Building> playerBuildings = BuildingManager.Instance.GetBuildingsForPlayer(ownerNetId);
            // if (playerBuildings != null) {
            //     foreach (Building building in playerBuildings)
            //     {
            //         if (building == null) continue; // Säkerhetskoll
            //
            //         // Endast byggnader som är klara räknas (inte under konstruktion)
            //         // Även de som är Disabled_NoPower måste räknas med för att se om de KAN slås på.
            //         if (building.CurrentState == BuildingState.Operational || building.CurrentState == BuildingState.Disabled_NoPower)
            //         {
            //             int upkeep = building.ManaUpkeep; // Antag att Building.cs har denna property/fält
            //             int generation = building.ManaGeneration; // Antag att Building.cs har denna
            //
            //             currentTotalUpkeep += upkeep;
            //             currentTotalGeneration += generation;
            //
            //             // Lägg till i listan om den drar ström eller var avstängd (för att kunna slå på/av den)
            //             if (upkeep > 0 || building.CurrentState == BuildingState.Disabled_NoPower)
            //             {
            //                  buildingsAffectingPower.Add(building);
            //             }
            //         }
            //     }
            // }
            // **************************************************************************
            // SLUT PÅ PSEUDOKOD - Ersätt med din faktiska byggnads-iteration
            // **************************************************************************

            // Spara de nyberäknade värdena
            data.manaGeneration = currentTotalGeneration;
            data.manaUpkeep = currentTotalUpkeep;

            // Bestäm ny power status
            data.hasSufficientPower = data.manaGeneration >= data.manaUpkeep;

            // Spara tillbaka uppdaterad data
            server_resourceData[ownerNetId] = data;

            // --- Uppdatera Byggnaders Status & Synka ---
            bool powerStatusChanged = data.hasSufficientPower != previouslyHadPower;

            // Om strömstatusen ändrats, eller om värdena ändrats (för UI), uppdatera klienten
            if (powerStatusChanged || data.manaGeneration != previousGeneration || data.manaUpkeep != previousUpkeep)
            {
                Debug.Log($"ResourceManager: Player {ownerNetId} Power Update. Gen: {data.manaGeneration}, Upkeep: {data.manaUpkeep}, HasPower: {data.hasSufficientPower}");

                // Uppdatera klientens SyncVars via NetworkPlayer
                Server_UpdateClientPowerStatus(ownerNetId, data.manaGeneration, data.manaUpkeep, data.hasSufficientPower);

                // Om strömstatus specifikt ändrades, uppdatera byggnaderna
                if (powerStatusChanged)
                {
                    Debug.Log($"ResourceManager: Player {ownerNetId} Power status changed to {data.hasSufficientPower}. Updating buildings.");
                    foreach (Building building in buildingsAffectingPower) // Använd listan från iterationen ovan
                    {
                        // Antag att Building har en metod för detta
                        building.Server_SetPoweredState(data.hasSufficientPower);
                    }
                }
            }
        }


        // --- ÄNDRAT: Metod för att synka data till NetworkPlayer ---

        /// <summary>Updates relevant SyncVars on a NetworkPlayer object if it exists.</summary>
        [Server]
        private void Server_UpdateClientResourceValue(uint ownerNetId, string resourceType, int value)
        {
            // Försök hitta NetworkPlayer-objektet.
            if (NetworkServer.spawned.TryGetValue(ownerNetId, out NetworkIdentity playerIdentity))
            {
                NetworkPlayer playerScript = playerIdentity.GetComponent<NetworkPlayer>();
                if (playerScript != null)
                {
                    // Uppdatera specifik SyncVar baserat på typ
                    switch (resourceType)
                    {
                        case "credits": playerScript.credits = value; break;
                            // case "mana": playerScript.mana = value; break; // Om du behåller mana-pool
                            // case "maxMana": playerScript.maxMana = value; break; // Om du behåller mana-pool
                    }
                }
            }
        }

        /// <summary>Updates power-related SyncVars on a NetworkPlayer object if it exists.</summary>
        [Server]
        private void Server_UpdateClientPowerStatus(uint ownerNetId, int generation, int upkeep, bool hasPower)
        {
            if (NetworkServer.spawned.TryGetValue(ownerNetId, out NetworkIdentity playerIdentity))
            {
                NetworkPlayer playerScript = playerIdentity.GetComponent<NetworkPlayer>();
                if (playerScript != null)
                {
                    // Uppdatera nya SyncVars på NetworkPlayer-scriptet
                    // Dessa behöver du lägga till i NetworkPlayer.cs!
                    playerScript.manaGeneration = generation; // Antag att denna SyncVar finns
                    playerScript.manaUpkeep = upkeep;         // Antag att denna SyncVar finns
                    playerScript.hasSufficientPower = hasPower; // Antag att denna SyncVar finns
                }
            }
        }


        // --- Getters (Server-side access) ---

        public int GetCurrentCredits(uint ownerNetId)
        {
            return server_resourceData.TryGetValue(ownerNetId, out PlayerResourceData data) ? data.credits : 0;
        }
        // public int GetCurrentMana(uint ownerNetId) { ... } // Om du behåller mana-pool
        // public int GetMaxMana(uint ownerNetId) { ... } // Om du behåller mana-pool

        public int GetCurrentManaGeneration(uint ownerNetId)
        {
            return server_resourceData.TryGetValue(ownerNetId, out PlayerResourceData data) ? data.manaGeneration : 0;
        }
        public int GetCurrentManaUpkeep(uint ownerNetId)
        {
            return server_resourceData.TryGetValue(ownerNetId, out PlayerResourceData data) ? data.manaUpkeep : 0;
        }
        public bool GetHasSufficientPower(uint ownerNetId)
        {
            return server_resourceData.TryGetValue(ownerNetId, out PlayerResourceData data) ? data.hasSufficientPower : true; // Default till true?
        }
    }
}