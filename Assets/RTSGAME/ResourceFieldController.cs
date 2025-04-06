using UnityEngine;
using System.Collections.Generic; // Behövs för List<>

public class ResourceFieldController : MonoBehaviour
{
    [Header("Field Settings")]
    [Tooltip("Radien för fältet där kristaller kan spawnas.")]
    public float fieldRadius = 10f;
    [Tooltip("Maximalt antal kristaller som kan finnas i fältet samtidigt.")]
    public int maxCrystals = 15;
    [Tooltip("Tid i sekunder innan en ny kristall försöker spawnas om det finns plats.")]
    public float respawnDelay = 5f;
    [Tooltip("Vilken kristall-prefab ska detta fält spawna? Dra in Grön, Blå eller Röd kristall-prefab här.")]
    public GameObject crystalPrefab; // Måste ha HarvestableCrystal.cs på sig!
    [Tooltip("Hur många kristaller ska finnas när spelet startar?")]
    public int initialSpawnCount = 7;

    // Intern lista för att hålla reda på aktiva kristaller
    private List<GameObject> spawnedCrystals = new List<GameObject>();
    private float respawnTimer = 0f;

    void Start()
    {
        // Validera prefab
        if (crystalPrefab == null || crystalPrefab.GetComponent<HarvestableCrystal>() == null)
        {
            Debug.LogError($"ResourceFieldController on '{gameObject.name}' is missing a valid Crystal Prefab with HarvestableCrystal script!", this);
            return; // Avbryt om prefaben är fel
        }

        // Se till att initialSpawnCount inte är större än maxCrystals
        initialSpawnCount = Mathf.Min(initialSpawnCount, maxCrystals);

        // Spawna de första kristallerna
        SpawnInitialCrystals();

        // Starta respawn-timern lite slumpmässigt så inte alla fält spawnar exakt samtidigt
        respawnTimer = Random.Range(0f, respawnDelay * 0.5f);
    }

    void Update()
    {
        // Rensa listan från kristaller som har blivit förstörda (plockade av harvester)
        // Gå baklänges för att kunna ta bort element säkert under iteration
        for (int i = spawnedCrystals.Count - 1; i >= 0; i--)
        {
            if (spawnedCrystals[i] == null) // Har GameObjectet förstörts?
            {
                spawnedCrystals.RemoveAt(i); // Ta bort den null-referensen från listan
            }
        }

        // Kolla om vi behöver spawna fler kristaller
        if (spawnedCrystals.Count < maxCrystals)
        {
            respawnTimer += Time.deltaTime;
            if (respawnTimer >= respawnDelay)
            {
                respawnTimer = 0f; // Nollställ timer
                TrySpawnCrystal(); // Försök spawna en ny
            }
        }
    }

    void SpawnInitialCrystals()
    {
        Debug.Log($"'{gameObject.name}' spawning initial {initialSpawnCount} crystals.");
        for (int i = 0; i < initialSpawnCount; i++)
        {
            // Försök spawna, ge upp efter några försök om det är trångt
            bool spawned = false;
            int attempts = 0;
            while (!spawned && attempts < 10) // Max 10 försök att hitta en plats
            {
                spawned = TrySpawnCrystal();
                attempts++;
            }
            if (!spawned)
            {
                Debug.LogWarning($"'{gameObject.name}' could not find suitable spot for initial crystal {i + 1} after {attempts} attempts.");
            }
        }
    }

    bool TrySpawnCrystal()
    {
        if (crystalPrefab == null) return false;
        if (spawnedCrystals.Count >= maxCrystals) return false;

        float randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float randomDist = Random.Range(0f, fieldRadius);
        Vector3 spawnPos = transform.position + new Vector3(Mathf.Cos(randomAngle) * randomDist, 0, Mathf.Sin(randomAngle) * randomDist);

        UnityEngine.AI.NavMeshHit hit;
        if (UnityEngine.AI.NavMesh.SamplePosition(spawnPos, out hit, 1.0f, UnityEngine.AI.NavMesh.AllAreas))
        {
            spawnPos = hit.position;

            // --- ÄNDRING HÄR ---
            float checkRadius = 1.0f;

            // 1. Skapa en LayerMask i kod som inkluderar ALLT utom "Ground"-lagret
            //    (Förutsätter att ditt mark-lager heter exakt "Ground")
            LayerMask blockingLayersMask = LayerMask.GetMask("Default", "Unit", "EnemyUnit", "Building", "Crystal"); // Lägg till alla lager som ÄR hinder här!
            // Alternativt, om du vill ha ALLT utom Ground:
            // int groundLayerIndex = LayerMask.NameToLayer("Ground");
            // if (groundLayerIndex != -1) { // Kolla att lagret finns
            //     blockingLayersMask = ~(1 << groundLayerIndex); // Invertera bitmasken för Ground
            // } else {
            //     blockingLayersMask = -1; // Fallback till allt om Ground-lagret inte hittas
            //     Debug.LogWarning("Could not find 'Ground' layer for CheckSphere mask.");
            // }


            // 2. Använd LayerMasken i Physics.CheckSphere
            Collider[] hitColliders = Physics.OverlapSphere(spawnPos, checkRadius, blockingLayersMask); // Använd OverlapSphere istället, ger mer info

            // if (!Physics.CheckSphere(spawnPos, checkRadius, blockingLayersMask)) // <-- Gammal CheckSphere med mask
            if (hitColliders.Length == 0) // Om arrayen är tom = inga hinder hittades på de angivna lagren
            {
                // Platsen är ledig (från allt utom marken)!
                // Spawna kristallen!
                GameObject newCrystal = Instantiate(crystalPrefab, spawnPos, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
                newCrystal.transform.SetParent(this.transform, true);
                spawnedCrystals.Add(newCrystal);

                Debug.Log($"'{gameObject.name}' spawned a crystal at {spawnPos}. Current count: {spawnedCrystals.Count}/{maxCrystals}");
                return true;
            }
            else
            {
                // Platsen blockerad av något annat på de relevanta lagren
                // Logga vad vi träffade för debug:
                // foreach(var col in hitColliders) { Debug.Log($"Spawn at {spawnPos} blocked by: {col.gameObject.name} on layer {LayerMask.LayerToName(col.gameObject.layer)}"); }
                return false;
            }
            // --- Slut på ändring ---
        }
        else
        {
            return false; // Hittade ingen giltig markposition
        }
    }

    // Valfritt: Rita ut radien i Scene-vyn
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, fieldRadius);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f, $"Crystal Field\nRadius: {fieldRadius}\nMax: {maxCrystals}\nDelay: {respawnDelay}s\nType: {crystalPrefab?.name ?? "None"}");
        UnityEditor.Handles.color = Color.cyan;
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, fieldRadius);
#endif
    }

    // OBS: Denna klass hanterar INTE Faction-byggnaden som ökar spawn rate än.
    // Det kan läggas till senare genom att den byggnaden hittar detta script och ändrar `respawnDelay` (gör den kortare).
}