// Unit.cs (Inkluderar GetHealthBarCanvas och kör Setup i Awake)
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI; // Viktigt för Slider

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Collider))] // Viktigt för OnMouseEnter/Exit och Raycasting
public class Unit : MonoBehaviour
{
    [Header("Stats")]
    public float maxHealth = 100f;
    public float currentHealth;
    public float attackDamage = 10f;
    public float attackRange = 2f;
    public float attackCooldown = 1.5f;
    public int teamID = 0; // 0 = Spelare, 1 = Fiende, etc.

    [Header("State (Internal)")]
    public bool isSelected = false; // Styrs av PlayerUnitController
    private NavMeshAgent agent;
    private Renderer unitRenderer;
    private Color originalColor;
    private Transform currentTarget = null;
    private float lastAttackTime = -100f;
    public enum UnitState { Idle, MovingToDestination, MovingToAttackTarget, Attacking }
    public UnitState currentState = UnitState.Idle;

    [Header("Health Bar")]
    public GameObject healthBarPrefab; // Dra din Slider-prefab hit
    public Transform healthBarSpawnPoint; // Dra ditt spawn point-objekt hit
    public Vector3 healthBarOffset = new Vector3(0, 1.5f, 0); // Fallback
    private Slider healthBarSlider = null;
    private Canvas healthBarCanvas = null; // Denna skapas och hanteras här
    private Camera mainCameraForBillboard;

    void Awake()
    {
        // Hämta komponenter tidigt
        agent = GetComponent<NavMeshAgent>();
        unitRenderer = GetComponentInChildren<Renderer>();
        if (unitRenderer != null)
        {
            originalColor = unitRenderer.material.color;
        }
        else
        {
            Debug.LogWarning($"Unit '{gameObject.name}' could not find Renderer!", this);
        }
        currentHealth = maxHealth;

        // Hitta kameran tidigt (Camera.main kan vara långsam att anropa ofta)
        mainCameraForBillboard = Camera.main;
        if (mainCameraForBillboard == null)
        {
            Debug.LogError("Could not find Main Camera for Billboard!", this);
        }

        // *** Skapa Health Bar och dess Canvas redan i Awake ***
        // Detta säkerställer att canvasen finns när andra scripts (som HarvesterUnit)
        // kör sin Start()-metod och anropar GetHealthBarCanvas().
        SetupHealthBar();
    }

    void Start()
    {
        // Uppdatera värdet initialt
        UpdateHealthBar();

        // Starta alltid med health bar dold (om inte vald och spelarens)
        if (healthBarCanvas != null)
        {
            bool shouldStartVisible = (isSelected && teamID == 0);
            healthBarCanvas.gameObject.SetActive(shouldStartVisible);
        }
    }

    void SetupHealthBar()
    {
        Debug.Log($"[{gameObject.name}] Running SetupHealthBar..."); // Felsökningslogg
        if (healthBarPrefab == null)
        {
            Debug.LogError($"Health Bar Prefab is not assigned on Unit '{gameObject.name}'. Cannot create health bar.", this);
            return;
        }

        // Bestäm förälder och position
        Transform spawnParentTransform = (healthBarSpawnPoint != null) ? healthBarSpawnPoint : this.transform;
        Vector3 spawnPos = spawnParentTransform.position;
        if (healthBarSpawnPoint == null)
        {
            spawnPos = transform.position + healthBarOffset;
        }

        // Skapa Canvas GameObject
        GameObject canvasGO = new GameObject(gameObject.name + "_HealthBarCanvas");
        canvasGO.transform.SetParent(spawnParentTransform, false);
        if (healthBarSpawnPoint == null) { canvasGO.transform.position = spawnPos; }
        else { canvasGO.transform.localPosition = Vector3.zero; }
        canvasGO.transform.localRotation = Quaternion.identity;

        // Lägg till och konfigurera Canvas-komponenten
        healthBarCanvas = canvasGO.AddComponent<Canvas>();
        Debug.Log($"[{gameObject.name}] healthBarCanvas assigned: {(healthBarCanvas != null)}"); // Felsökningslogg
        healthBarCanvas.renderMode = RenderMode.WorldSpace;
        healthBarCanvas.worldCamera = mainCameraForBillboard;

        RectTransform canvasRect = canvasGO.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(100, 20); // Storlek på själva canvas-ytan (justera vid behov)

        // *** SKALNING AV WORLD SPACE CANVAS ***
        // Denna lokala skala multipliceras med skalan på föräldraobjektet (Unit/SpawnPoint).
        // Om dina olika Unit-prefabs har olika storlek (Scale i deras Transform),
        // kommer health baren också att se olika stor ut i världen.
        // Justera detta värde tills baren ser lagom stor ut för en "normalstor" enhet.
        // Acceptera att den blir större/mindre på större/mindre enheter, eller byt till Screen Space Canvas.
        canvasRect.localScale = new Vector3(0.01f, 0.01f, 0.01f);
        // *** SLUT PÅ SKALNINGSKOMMENTAR ***

        // Instansiera själva Health Bar Slidern som barn till Canvas
        GameObject healthBarInstance = Instantiate(healthBarPrefab, canvasGO.transform);
        healthBarSlider = healthBarInstance.GetComponent<Slider>();

        if (healthBarSlider == null)
        {
            Debug.LogError($"Instantiated Health Bar Prefab for '{gameObject.name}' lacks Slider component!", healthBarInstance);
            Destroy(canvasGO); return;
        }

        // Positionera slidern inom canvasen (t.ex. centrerad)
        RectTransform sliderRect = healthBarInstance.GetComponent<RectTransform>();
        sliderRect.anchoredPosition = Vector2.zero;

        // Lägg till Billboard-scriptet
        if (mainCameraForBillboard != null)
        {
            Billboard billBoardScript = canvasGO.AddComponent<Billboard>();
            billBoardScript.SetCameraToFace(mainCameraForBillboard);
        }

        Debug.Log($"[{gameObject.name}] Finished SetupHealthBar. Canvas is null? {(healthBarCanvas == null)}"); // Felsökningslogg
    }

    // Metod för att HarvesterUnit ska kunna få tag i canvasen
    public Canvas GetHealthBarCanvas()
    {
        return healthBarCanvas;
    }


    // --- Metoder för synlighet och uppdatering ---

    // UpdateHealthBar uppdaterar BARA värdet
    void UpdateHealthBar()
    {
        if (healthBarSlider == null || healthBarCanvas == null) return;
        float healthPercent = (maxHealth > 0) ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;
        healthBarSlider.value = healthPercent;
    }

    public void Select()
    {
        isSelected = true;
        if (unitRenderer != null) { unitRenderer.material.color = Color.green; }
        if (teamID == 0 && healthBarCanvas != null) { UpdateHealthBar(); healthBarCanvas.gameObject.SetActive(true); }
    }

    public void Deselect()
    {
        isSelected = false;
        if (unitRenderer != null) { unitRenderer.material.color = originalColor; }
        if (teamID == 0 && healthBarCanvas != null) { healthBarCanvas.gameObject.SetActive(false); }
    }

    void OnMouseEnter()
    {
        if (teamID != 0 && healthBarCanvas != null) { UpdateHealthBar(); healthBarCanvas.gameObject.SetActive(true); }
    }

    void OnMouseExit()
    {
        if (teamID != 0 && healthBarCanvas != null) { healthBarCanvas.gameObject.SetActive(false); }
    }

    // --- Skada och Död ---

    public void TakeDamage(float damageAmount)
    {
        if (currentHealth <= 0) return;
        currentHealth -= damageAmount;
        currentHealth = Mathf.Max(currentHealth, 0); // Gå inte under noll
        UpdateHealthBar(); // Uppdatera värdet
        if (currentHealth <= 0) { Die(); }
    }

    private void Die()
    {
        if (currentState == UnitState.Idle && currentHealth <= 0 && agent.enabled == false) return;
        // Debug.Log(gameObject.name + " died!"); // Behövs kanske inte längre
        currentState = UnitState.Idle;
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.isStopped = true; agent.ResetPath(); agent.enabled = false;
        }
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
        if (healthBarCanvas != null) { Destroy(healthBarCanvas.gameObject); } // Förstör canvasen
        Destroy(gameObject, 3f); // Förstör enheten efter fördröjning
    }

    // --- State Machine och Kommandon (Oförändrade) ---

    void Update()
    {
        if (currentHealth <= 0 || !agent.enabled) return;
        switch (currentState)
        {
            case UnitState.Idle: break;
            case UnitState.MovingToDestination:
                if (!agent.pathPending && agent.hasPath && agent.remainingDistance <= agent.stoppingDistance)
                {
                    if (agent.velocity.sqrMagnitude < 0.01f) { currentState = UnitState.Idle; } // Kolla om den faktiskt stannat
                }
                break;
            case UnitState.MovingToAttackTarget: HandleMovingToAttackTarget(); break;
            case UnitState.Attacking: HandleAttackingState(); break;
        }
    }

    void HandleMovingToAttackTarget()
    { /* ... som tidigare ... */
        if (currentTarget == null || !currentTarget.gameObject.activeInHierarchy) { currentState = UnitState.Idle; agent.ResetPath(); return; }
        agent.SetDestination(currentTarget.position);
        if (Vector3.Distance(transform.position, currentTarget.position) <= attackRange)
        {
            agent.ResetPath(); currentState = UnitState.Attacking; transform.LookAt(currentTarget);
        }
    }
    void HandleAttackingState()
    { /* ... som tidigare ... */
        if (currentTarget == null || !currentTarget.gameObject.activeInHierarchy) { currentState = UnitState.Idle; return; }
        if (Vector3.Distance(transform.position, currentTarget.position) > attackRange)
        {
            currentState = UnitState.MovingToAttackTarget; return;
        }
        transform.LookAt(currentTarget);
        if (Time.time >= lastAttackTime + attackCooldown) { PerformAttack(); lastAttackTime = Time.time; }
    }
    void PerformAttack()
    { /* ... som tidigare ... */
        if (currentTarget == null) return;
        Unit targetUnit = currentTarget.GetComponent<Unit>();
        if (targetUnit != null && targetUnit.currentHealth > 0) { targetUnit.TakeDamage(attackDamage); }
        else { currentTarget = null; currentState = UnitState.Idle; }
    }
    public void OrderMoveTo(Vector3 destination)
    { /* ... som tidigare ... */
        if (currentHealth <= 0) return; currentTarget = null; if (!agent.enabled) agent.enabled = true; agent.isStopped = false;
        agent.stoppingDistance = 0.5f; agent.SetDestination(destination); currentState = UnitState.MovingToDestination;
    }
    public void OrderAttackTarget(Transform target)
    { /* ... som tidigare ... */
        if (currentHealth <= 0 || target == null) return; Unit targetUnit = target.GetComponent<Unit>();
        if (targetUnit != null && targetUnit.teamID != this.teamID)
        {
            currentTarget = target; if (!agent.enabled) agent.enabled = true; agent.isStopped = false;
            agent.stoppingDistance = attackRange * 0.8f; currentState = UnitState.MovingToAttackTarget;
        }
        else { /* Ignorera/logga */ }
    }
} // End of class