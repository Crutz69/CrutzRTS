// Assets/RTSGAME/Scripts/World/ResourceFieldController.cs
using UnityEngine;
using System.Collections.Generic; // Behövs för List<>
using Mirror; // <-- Lägg till Mirror

namespace RTSGAME // <-- Lägg till Namespace
{
    // Ärver från NetworkBehaviour nu
    public class ResourceFieldController : NetworkBehaviour
    {
        [Header("Field Settings")]
        [Tooltip("Radien för fältet där kristaller kan spawnas.")]
        [SerializeField] private float fieldRadius = 10f;
        [Tooltip("Maximalt antal kristaller som kan finnas i fältet samtidigt.")]
        [SerializeField] private int maxCrystals = 15;
        [Tooltip("Tid i sekunder innan en ny kristall försöker spawnas om det finns plats.")]
        [SerializeField] private float respawnDelay = 5f;
        [Tooltip("Vilken kristall-prefab ska detta fält spawna? MÅSTE ha NetworkIdentity och vara registrerad i NetworkManager.")]
        [SerializeField] private GameObject crystalPrefab; // Måste vara en registrerad nätverks-prefab!
        [Tooltip("Hur många kristaller ska finnas när spelet startar?")]
        [SerializeField] private int initialSpawnCount = 7;

        // --- Server-Side Variables ---
        // Denna lista och timer hanteras bara på servern
        private readonly List<GameObject> server_spawnedCrystals = new List<GameObject>();
        private float server_respawnTimer = 0f;

        // --- Mirror Callbacks ---

        public override void OnStartServer() // Använd OnStartServer istället för Start för server-logik
        {
            base.OnStartServer();

            // Validera prefab (bara servern behöver göra detta)
            if (crystalPrefab == null || crystalPrefab.GetComponent<HarvestableCrystal>() == null || crystalPrefab.GetComponent<NetworkIdentity>() == null)
            {
                Debug.LogError($"ResourceFieldController on '{gameObject.name}' is missing a valid Crystal Prefab with HarvestableCrystal, NetworkIdentity, and registration in NetworkManager!", this);
                enabled = false; // Stäng av scriptet om prefaben är fel
                return;
            }

            initialSpawnCount = Mathf.Min(initialSpawnCount, maxCrystals);
            SpawnInitialCrystals(); // Servern spawnar initiala kristaller

            server_respawnTimer = Random.Range(0f, respawnDelay * 0.5f);
        }

        // Update körs på server och klient, men logiken här är bara för servern
        void Update()
        {
            // All logik här ska bara köras på servern!
            if (!isServer) return;

            Server_Update();
        }

        // --- Server-Only Logic ---

        [Server] // Markera tydligt att detta är server-logik
        void Server_Update()
        {
            // Rensa listan från förstörda kristaller
            // Viktigt: Servern vet när objekt förstörs via NetworkServer.Destroy
            server_spawnedCrystals.RemoveAll(item => item == null); // Effektivare sätt att rensa null-referenser

            // Kolla om vi behöver spawna fler
            if (server_spawnedCrystals.Count < maxCrystals)
            {
                server_respawnTimer += Time.deltaTime;
                if (server_respawnTimer >= respawnDelay)
                {
                    server_respawnTimer = 0f;
                    Server_TrySpawnCrystal(); // Försök spawna en ny
                }
            }
        }

        [Server]
        void SpawnInitialCrystals()
        {
            // Debug.Log($"'{gameObject.name}' spawning initial {initialSpawnCount} crystals on server.");
            for (int i = 0; i < initialSpawnCount; i++)
            {
                bool spawned = false;
                int attempts = 0;
                while (!spawned && attempts < 10)
                {
                    spawned = Server_TrySpawnCrystal();
                    attempts++;
                }
                if (!spawned) { Debug.LogWarning($"'{gameObject.name}' could not find suitable spot for initial crystal {i + 1} after {attempts} attempts."); }
            }
        }

        [Server]
        bool Server_TrySpawnCrystal()
        {
            if (crystalPrefab == null) return false; // Redan kollat i OnStartServer, men extra säkerhet
            if (server_spawnedCrystals.Count >= maxCrystals) return false;

            // Hitta slumpmässig position inom radien
            Vector3 spawnPos = transform.position + Random.insideUnitSphere * fieldRadius;
            spawnPos.y = transform.position.y; // Håll den på samma höjd som controllern? Eller sök NavMesh pos.

            // Försök hitta giltig position på NavMesh
            if (UnityEngine.AI.NavMesh.SamplePosition(spawnPos, out UnityEngine.AI.NavMeshHit hit, fieldRadius, UnityEngine.AI.NavMesh.AllAreas)) // Öka sökradien för SamplePosition
            {
                spawnPos = hit.position;

                // Kolla om platsen är blockerad (använd lämplig LayerMask)
                float checkRadius = 1.0f; // Hur nära får kristaller spawna varandra/andra objekt?
                                          // TODO: Definiera blockingLayersMask, exkludera ev. marklager
                LayerMask blockingLayersMask = ~LayerMask.GetMask("Ground"); // Exempel: Allt utom Ground

                Collider[] hitColliders = Physics.OverlapSphere(spawnPos, checkRadius, blockingLayersMask);

                if (hitColliders.Length == 0) // Platsen är ledig!
                {
                    // Skapa kristallen på servern först
                    GameObject newCrystal = Instantiate(crystalPrefab, spawnPos, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
                    newCrystal.transform.SetParent(this.transform, true); // Gör den till barn av fältet? (Valfritt)

                    // Lägg till i serverns lista
                    server_spawnedCrystals.Add(newCrystal);

                    // *** Spawna objektet på nätverket för alla klienter ***
                    NetworkServer.Spawn(newCrystal);

                    // Valfritt: Sätt typ/värde på kristallen här om det ska variera?
                    // HarvestableCrystal crystalScript = newCrystal.GetComponent<HarvestableCrystal>();
                    // crystalScript.Server_Initialize(CrystalType.Blue, 250); // Kräver metod på kristallen

                    // Debug.Log($"'{gameObject.name}' spawned crystal {newCrystal.GetComponent<NetworkIdentity>().netId} at {spawnPos}. Count: {server_spawnedCrystals.Count}/{maxCrystals}");
                    return true;
                }
                else { /* Plats blockerad */ }
            }
            else { /* Hittade ingen giltig NavMesh-position */ }

            return false; // Misslyckades att spawna
        }

        // --- Gizmos ---
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, fieldRadius);
#if UNITY_EDITOR
            // Försök visa aktuell spawn count (fungerar bara i Play mode på server/host)
            string countText = isServer ? $"Count: {server_spawnedCrystals.Count}/{maxCrystals}" : "Count: (Server Only)";
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f, $"Crystal Field [{countText}]\nRadius: {fieldRadius}\nDelay: {respawnDelay}s\nPrefab: {crystalPrefab?.name ?? "None"}");
            UnityEditor.Handles.color = Color.cyan;
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, fieldRadius);
#endif
        }
    }
}