// Assets/RTSGAME/Scripts/Managers/ResourceManager.cs
using Mirror;
using System.Collections.Generic;
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
            public int mana;
            public int maxMana;
            // Lägg till ev. andra resurser här (t.ex. befolkningsgräns?)
        }

        // Server-side dictionary för att hålla den auktoritativa resursdatan
        // Nyckel: ownerNetId (från NetworkIdentity), Värde: Resursdata
        private readonly Dictionary<uint, PlayerResourceData> server_resourceData = new Dictionary<uint, PlayerResourceData>();

        // --- Unity & Mirror Callbacks ---

        public override void OnStartServer()
        {
            // Sätt upp Singleton på servern
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("Duplicate ResourceManager instance detected on server, destroying self.");
                // Inaktivera detta objekt istället för att förstöra om det är NetworkManager-objektet
                if (gameObject != NetworkManager.singleton.gameObject)
                {
                    Destroy(gameObject);
                }
                else
                {
                    enabled = false; // Inaktivera scriptet
                }
            }
            else
            {
                Instance = this;
                Debug.Log("ResourceManager Server Instance Initialized.");
                // DontDestroyOnLoad(gameObject); // Överväg om den ska överleva scenbyten
                // Starta eventuell regelbunden uppdatering av mana/power
                // InvokeRepeating(nameof(Server_UpdateAllPlayerPowerAndMana), 1.0f, 1.0f); // Uppdatera varje sekund
            }
        }

        void OnDestroy()
        {
            // Rensa Singleton referens
            if (Instance == this)
            {
                Instance = null;
            }
            // Stoppa InvokeRepeating om den används
            // CancelInvoke(nameof(Server_UpdateAllPlayerPowerAndMana));
        }

        // --- Server-Only Methods ---

        /// <summary>
        /// Registers a player or AI with the resource manager. Called by server.
        /// </summary>
        [Server]
        public void Server_RegisterPlayer(uint ownerNetId, int startCredits, int startMana, int startMaxMana)
        {
            if (ownerNetId == 0)
            { // 0 är ofta reserverat för servern/neutral
                Debug.LogWarning("ResourceManager: Attempted to register player with NetId 0.");
                return;
            }
            if (!server_resourceData.ContainsKey(ownerNetId))
            {
                server_resourceData.Add(ownerNetId, new PlayerResourceData
                {
                    credits = startCredits,
                    mana = startMana,
                    maxMana = startMaxMana
                });
                Debug.Log($"ResourceManager: Registered player {ownerNetId} with {startCredits}C / {startMana}M (Max: {startMaxMana}).");
                // Uppdatera NetworkPlayer SyncVars direkt så klienten får startvärden
                Server_UpdateClientResources(ownerNetId);
            }
            else
            {
                Debug.LogWarning($"ResourceManager: Player {ownerNetId} already registered.");
            }
        }

        /// <summary>
        /// Unregisters a player or AI. Called by server (e.g., on disconnect or defeat).
        /// </summary>
        [Server]
        public void Server_UnregisterPlayer(uint ownerNetId)
        {
            if (server_resourceData.Remove(ownerNetId))
            {
                Debug.Log($"ResourceManager: Unregistered player {ownerNetId}.");
            }
        }

        /// <summary>
        /// Adds credits to a player/AI. Called by server.
        /// </summary>
        [Server]
        public void Server_AddCredits(uint ownerNetId, int amount)
        {
            if (amount <= 0) return; // Lägg bara till positiva värden
            if (server_resourceData.TryGetValue(ownerNetId, out PlayerResourceData data))
            {
                data.credits += amount;
                server_resourceData[ownerNetId] = data; // Structs måste skrivas tillbaka
                Debug.Log($"ResourceManager: Added {amount} credits to {ownerNetId}. New total: {data.credits}");
                Server_UpdateClientResources(ownerNetId); // Synka till klienten
            }
            else { Debug.LogWarning($"ResourceManager: Player {ownerNetId} not found for AddCredits."); }
        }

        /// <summary>
        /// Attempts to spend credits for a player/AI. Called by server. Returns true on success.
        /// </summary>
        [Server]
        public bool Server_TrySpendCredits(uint ownerNetId, int amount)
        {
            if (amount < 0) { Debug.LogWarning($"ResourceManager: Tried to spend negative credits ({amount}) for {ownerNetId}."); return false; }
            if (amount == 0) return true; // Kostar inget = lyckas alltid

            if (server_resourceData.TryGetValue(ownerNetId, out PlayerResourceData data))
            {
                if (data.credits >= amount)
                { // Kollar om tillräckligt finns
                    data.credits -= amount; // Drar av
                    server_resourceData[ownerNetId] = data; // Sparar tillbaka
                    Debug.Log($"ResourceManager: Spent {amount} credits from {ownerNetId}. Remaining: {data.credits}");
                    Server_UpdateClientResources(ownerNetId); // Synka till klienten
                    return true; // Lyckades
                }
                else
                {
                    // Inte tillräckligt med resurser
                    Debug.Log($"ResourceManager: Player {ownerNetId} failed to spend {amount} credits (Has: {data.credits}).");
                    return false; // Misslyckades
                }
            }
            else
            {
                Debug.LogWarning($"ResourceManager: Player {ownerNetId} not found for TrySpendCredits.");
                return false; // Misslyckades (spelaren finns inte)
            }
        }

        // --- Motsvarande metoder för Mana ---

        /// <summary>Adds mana to a player/AI, clamping to their maxMana. Called by server.</summary>
        [Server]
        public void Server_AddMana(uint ownerNetId, int amount)
        {
            if (server_resourceData.TryGetValue(ownerNetId, out PlayerResourceData data))
            {
                int oldMana = data.mana;
                data.mana = Mathf.Clamp(data.mana + amount, 0, data.maxMana);
                if (data.mana != oldMana)
                { // Bara uppdatera om värdet ändrades
                    server_resourceData[ownerNetId] = data;
                    // Logga kanske inte varje tick här, blir spammy
                    Server_UpdateClientResources(ownerNetId);
                }
            } // Ingen varning om spelaren inte finns? Mana-ticks kan ske efter spelaren lämnat.
        }

        /// <summary>Attempts to spend mana for a player/AI. Called by server. Returns true on success.</summary>
        [Server]
        public bool Server_TrySpendMana(uint ownerNetId, int amount)
        {
            if (amount < 0) return false;
            if (amount == 0) return true;

            if (server_resourceData.TryGetValue(ownerNetId, out PlayerResourceData data))
            {
                if (data.mana >= amount)
                {
                    data.mana -= amount;
                    server_resourceData[ownerNetId] = data;
                    Server_UpdateClientResources(ownerNetId);
                    return true;
                }
            }
            // Ingen varning här heller? Kan vara OK att försök misslyckas tyst.
            return false;
        }

        /// <summary>Sets the maximum mana for a player/AI. Clamps current mana. Called by server.</summary>
        [Server]
        public void Server_SetMaxMana(uint ownerNetId, int newMax)
        {
            if (newMax < 0) return;
            if (server_resourceData.TryGetValue(ownerNetId, out PlayerResourceData data))
            {
                data.maxMana = newMax;
                data.mana = Mathf.Clamp(data.mana, 0, newMax); // Clampa nuvarande mana
                server_resourceData[ownerNetId] = data;
                Server_UpdateClientResources(ownerNetId);
            }
            else { Debug.LogWarning($"ResourceManager: Player {ownerNetId} not found for SetMaxMana."); }
        }


        // --- Metod för att uppdatera Mana Delta & Power Status (bör köras regelbundet på servern) ---

        /// <summary>Iterates through all registered players/AIs and updates their mana generation/consumption and power status.</summary>
        [Server]
        public void Server_UpdateAllPlayerPowerAndMana()
        {
            // Skapa en temporär lista med nycklar för att undvika problem om dictionaryn ändras under iteration
            List<uint> playerIds = new List<uint>(server_resourceData.Keys);
            foreach (uint ownerNetId in playerIds)
            {
                Server_UpdateSinglePlayerPowerAndMana(ownerNetId);
            }
        }

        /// <summary>Updates mana delta and building power status for a single player/AI.</summary>
        [Server]
        private void Server_UpdateSinglePlayerPowerAndMana(uint ownerNetId)
        {
            if (!server_resourceData.ContainsKey(ownerNetId)) return;

            int currentTotalUpkeep = 0;
            int currentTotalGeneration = 0;
            List<Building> ownedBuildings = new List<Building>(); // Byggnader att ev. stänga av/sätta på

            // TODO: Hitta alla byggnader för spelaren EFFEKTIVT.
            // Detta är den svåra delen. Kräver att byggnader registreras någonstans
            // (t.ex. i en lista på NetworkPlayer, eller i en global BuildingManager)
            // så att ResourceManager snabbt kan hitta dem. Sökning via NetworkServer.spawned är för långsamt.

            // --- START Pseudokod för byggnads-iteration ---
            // List<Building> playerBuildings = BuildingManager.Instance.GetBuildingsForPlayer(ownerNetId);
            // foreach (Building building in playerBuildings)
            // {
            //      // Kolla state - endast Operational/Disabled påverkar/behöver ström
            //      if (building.CurrentState == BuildingState.Operational || building.CurrentState == BuildingState.Disabled_NoPower)
            //      {
            //            int upkeep = building.ManaUpkeep > 0 ? building.ManaUpkeep : 0;
            //            int generation = building.ManaGeneration > 0 ? building.ManaGeneration : (building.ManaUpkeep < 0 ? -building.ManaUpkeep : 0);
            //
            //            currentTotalUpkeep += upkeep;
            //            currentTotalGeneration += generation;
            //
            //            // Lägg till i listan om den potentiellt behöver ändra power state
            //            if (upkeep > 0 || building.CurrentState == BuildingState.Disabled_NoPower) {
            //                 ownedBuildings.Add(building);
            //            }
            //      }
            // }
            // --- SLUT Pseudokod ---

            // Beräkna nettoförändring per sekund (eller per tick)
            // Antag att denna metod körs en gång per sekund
            int deltaManaPerSecond = currentTotalGeneration - currentTotalUpkeep;

            // Applicera förändring
            Server_AddMana(ownerNetId, deltaManaPerSecond);

            // Kolla strömstatus efter mana-uppdateringen
            PlayerResourceData currentData = server_resourceData[ownerNetId]; // Hämta uppdaterad data
            bool hasEnoughPower = currentData.mana >= currentTotalUpkeep; // Har vi nog för nästa tick?

            // Uppdatera byggnaders strömstatus
            if (hasEnoughPower)
            {
                // Slå på alla byggnader som var avstängda pga strömbrist
                foreach (Building building in ownedBuildings)
                {
                    if (building.CurrentState == BuildingState.Disabled_NoPower)
                    {
                        building.Server_SetPoweredState(true);
                    }
                }
            }
            else
            {
                // Stäng av byggnader!
                // TODO: Implementera prioriterad avstängning. Stäng av de som drar mest först?
                // Eller stäng av produktion före försvar? Exempel: Stäng av alla som drar ström.
                Debug.LogWarning($"Player {ownerNetId} has insufficient power! (Mana: {currentData.mana}, Upkeep: {currentTotalUpkeep})");
                foreach (Building building in ownedBuildings)
                {
                    if (building.ManaUpkeep > 0 && building.CurrentState == BuildingState.Operational)
                    {
                        building.Server_SetPoweredState(false);
                    }
                }
            }
        }


        // --- Metod för att synka data till NetworkPlayer (om det är en mänsklig spelare) ---

        /// <summary>Updates the SyncVars on a NetworkPlayer object if it exists.</summary>
        [Server]
        private void Server_UpdateClientResources(uint ownerNetId)
        {
            // Försök bara uppdatera om spelaren faktiskt finns i resursdatan
            if (!server_resourceData.TryGetValue(ownerNetId, out PlayerResourceData data)) return;

            // Försök hitta NetworkPlayer-objektet. Använd PlayerManager om möjligt.
            // NetworkPlayer playerScript = PlayerManager.Instance?.GetPlayer(ownerNetId);
            // if (playerScript != null) { ... }

            // Alternativ: Direkt via Mirror (mindre flexibelt)
            if (NetworkServer.spawned.TryGetValue(ownerNetId, out NetworkIdentity playerIdentity))
            {
                NetworkPlayer playerScript = playerIdentity.GetComponent<NetworkPlayer>();
                if (playerScript != null)
                {
                    // Uppdatera SyncVars på NetworkPlayer-scriptet
                    // Detta triggar hooks på klienten för UI-uppdateringar
                    playerScript.credits = data.credits;
                    playerScript.mana = data.mana;
                    playerScript.maxMana = data.maxMana;
                }
                else
                {
                    // Det är ett nätverksobjekt, men inte en NetworkPlayer (kanske en AI Controller?)
                    // Ingen klient-synk behövs för denna AI (om inte för spectator UI)
                }
            }
            // Om NetworkServer.spawned inte hittar ID:t, är det troligen en AI utan NetworkIdentity
            // eller en spelare som precis lämnat. Ingen klient-synk behövs.
        }

        // --- Getters (Kan anropas från server-kod) ---

        /// <summary>Gets the current credits for a player/AI. Server-side only access recommended.</summary>
        public int GetCurrentCredits(uint ownerNetId)
        {
            if (server_resourceData.TryGetValue(ownerNetId, out PlayerResourceData data))
            {
                return data.credits;
            }
            Debug.LogWarning($"ResourceManager: Tried to get credits for unregistered player {ownerNetId}.");
            return 0;
        }
        /// <summary>Gets the current mana for a player/AI. Server-side only access recommended.</summary>
        public int GetCurrentMana(uint ownerNetId)
        {
            if (server_resourceData.TryGetValue(ownerNetId, out PlayerResourceData data))
            {
                return data.mana;
            }
            Debug.LogWarning($"ResourceManager: Tried to get mana for unregistered player {ownerNetId}.");
            return 0;
        }
        /// <summary>Gets the maximum mana for a player/AI. Server-side only access recommended.</summary>
        public int GetMaxMana(uint ownerNetId)
        {
            if (server_resourceData.TryGetValue(ownerNetId, out PlayerResourceData data))
            {
                return data.maxMana;
            }
            Debug.LogWarning($"ResourceManager: Tried to get max mana for unregistered player {ownerNetId}.");
            return 1; // Undvik division med noll, returnera minst 1?
        }
    }
}