// Assets/RTSGAME/Scripts/World/ResourceFieldController.cs
using UnityEngine;
using System.Collections.Generic;
using Mirror;
#if UNITY_EDITOR
using UnityEditor; // Behövs för Handles
#endif

namespace RTSGAME
{
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
        [SerializeField] private GameObject crystalPrefab;
        [Tooltip("Hur många kristaller ska finnas när spelet startar?")]
        [SerializeField] private int initialSpawnCount = 7;

        [Header("Placement Settings")]
        [Tooltip("How close to check for obstructions when spawning a crystal.")]
        [SerializeField] private float placementCheckRadius = 1.0f;
        [Tooltip("Layers that prevent a crystal from spawning (e.g., other resources, buildings, impassable terrain). Set in Inspector!")]
        [SerializeField] private LayerMask placementBlockingLayers; // <-- Nytt, mer konfigurerbart fält

        // Server-Side Variables
        private readonly List<GameObject> server_spawnedCrystals = new List<GameObject>();
        private float server_respawnTimer = 0f;

        // --- Mirror Callbacks ---

        public override void OnStartServer()
        {
            base.OnStartServer();

            if (crystalPrefab == null || crystalPrefab.GetComponent<HarvestableCrystal>() == null || crystalPrefab.GetComponent<NetworkIdentity>() == null)
            {
                Debug.LogError($"ResourceFieldController '{gameObject.name}' has invalid crystalPrefab!", this);
                enabled = false; return;
            }
            // Kolla om spawnable prefabs listan i NetworkManager innehåller denna prefab
            if (!NetworkManager.singleton.spawnPrefabs.Contains(crystalPrefab))
            {
                Debug.LogError($"ResourceFieldController '{gameObject.name}': Crystal Prefab '{crystalPrefab.name}' is not registered in NetworkManager's Spawnable Prefabs list!", this);
                enabled = false; return;
            }
            // Kolla om LayerMask är satt
            if (placementBlockingLayers.value == 0)
            { // Varning om ingen blockerande layer är vald
                Debug.LogWarning($"ResourceFieldController '{gameObject.name}': Placement Blocking Layers mask is not set. Crystals might spawn anywhere!", this);
                // Sätt en rimlig default? T.ex. Default layer? Eller bara "Resources"?
                // placementBlockingLayers = LayerMask.GetMask("Resources", "Buildings"); // Exempel
            }


            initialSpawnCount = Mathf.Min(initialSpawnCount, maxCrystals);
            SpawnInitialCrystals();

            server_respawnTimer = Random.Range(0f, respawnDelay * 0.5f);
        }

        void Update()
        {
            if (!isServer) return;
            Server_Update();
        }

        // --- Server-Only Logic ---

        [Server]
        void Server_Update()
        {
            server_spawnedCrystals.RemoveAll(item => item == null);

            if (server_spawnedCrystals.Count < maxCrystals)
            {
                server_respawnTimer += Time.deltaTime;
                if (server_respawnTimer >= respawnDelay)
                {
                    server_respawnTimer = 0f;
                    Server_TrySpawnCrystal();
                }
            }
        }

        [Server]
        void SpawnInitialCrystals()
        {
            int spawnedCount = 0;
            for (int i = 0; i < initialSpawnCount; i++)
            {
                int attempts = 0;
                while (spawnedCount < initialSpawnCount && attempts < 20)
                { // Försök lite mer intensivt vid start
                    if (Server_TrySpawnCrystal())
                    {
                        spawnedCount++;
                        break; // Gå till nästa kristall
                    }
                    attempts++;
                }
                if (attempts >= 20)
                {
                    Debug.LogWarning($"'{gameObject.name}' failed to spawn initial crystal {i + 1} after many attempts.");
                }
            }
            Debug.Log($"'{gameObject.name}' finished spawning initial crystals. Count: {server_spawnedCrystals.Count}");
        }

        [Server]
        bool Server_TrySpawnCrystal()
        {
            if (server_spawnedCrystals.Count >= maxCrystals) return false;

            // Försök hitta en slumpmässig position på NavMesh inom radien
            for (int tryNum = 0; tryNum < 5; tryNum++) // Försök hitta NavMesh pos några gånger
            {
                Vector3 randomPos = transform.position + Random.insideUnitSphere * fieldRadius;
                randomPos.y = transform.position.y; // Håll på samma Y-nivå initialt

                if (UnityEngine.AI.NavMesh.SamplePosition(randomPos, out UnityEngine.AI.NavMeshHit hit, fieldRadius, UnityEngine.AI.NavMesh.AllAreas))
                {
                    Vector3 spawnPos = hit.position;

                    // Kolla om platsen är blockerad med den konfigurerade LayerMasken
                    // Använd NonAlloc för lite bättre prestanda
                    Collider[] hitColliders = new Collider[1]; // Behöver bara veta OM den träffar något
                    int hits = Physics.OverlapSphereNonAlloc(spawnPos, placementCheckRadius, hitColliders, placementBlockingLayers);

                    if (hits == 0) // Platsen är ledig från blockerande objekt!
                    {
                        GameObject newCrystal = Instantiate(crystalPrefab, spawnPos, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
                        newCrystal.transform.SetParent(this.transform, true);
                        server_spawnedCrystals.Add(newCrystal);
                        NetworkServer.Spawn(newCrystal); // Spawna på nätverket!
                        return true; // Lyckades!
                    }
                    // Platsen var blockerad, loopen fortsätter och försöker hitta en ny slump-position
                }
                // SamplePosition misslyckades, loopen fortsätter
            }

            // Debug.LogWarning($"'{gameObject.name}' failed to find a valid, unblocked spawn position after multiple attempts.");
            return false; // Misslyckades att hitta en bra position efter flera försök
        }

        // --- Gizmos ---
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.3f); // Cyan transparent
            Gizmos.DrawSphere(transform.position, fieldRadius);
#if UNITY_EDITOR
            string countText = "Count: N/A (Not Playing)";
            if (Application.isPlaying)
            {
                countText = isServer && server_spawnedCrystals != null ? $"Count: {server_spawnedCrystals.Count}/{maxCrystals}" : "Count: (Client Only)";
            }
            string label = $"Crystal Field [{countText}]\nRadius: {fieldRadius}\nMax: {maxCrystals}\nDelay: {respawnDelay}s\nPrefab: {crystalPrefab?.name ?? "NONE"}";
            Handles.Label(transform.position + Vector3.up * 1.5f, label);
            Handles.color = Color.cyan;
            Handles.DrawWireDisc(transform.position, Vector3.up, fieldRadius);
#endif
        }
    }
}