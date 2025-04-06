// Unit.cs
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI; // *** Viktigt: Lägg till detta för UI-element som Slider ***

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Collider))]
public class Unit : MonoBehaviour
{
    [Header("Stats")]
    public float maxHealth = 100f;
    public float currentHealth;
    public float attackDamage = 10f;
    public float attackRange = 2f;
    public float attackCooldown = 1.5f;
    public int teamID = 0;

    [Header("State (Internal)")]
    public bool isSelected = false;
    private NavMeshAgent agent;
    private Renderer unitRenderer;
    private Color originalColor;
    private Transform currentTarget = null;
    private float lastAttackTime = -100f;
    public enum UnitState { Idle, MovingToDestination, MovingToAttackTarget, Attacking }
    public UnitState currentState = UnitState.Idle;

    // *** NYA VARIABLER FÖR HEALTH BAR ***
    [Header("Health Bar")]
    public GameObject healthBarPrefab; // Dra din Slider-prefab hit i Inspektorn
    public Transform healthBarSpawnPoint; // Ett tomt GameObject ovanför enheten där baren ska sitta
    public Vector3 healthBarOffset = new Vector3(0, 1.5f, 0); // Fallback om spawn point saknas
    private Slider healthBarSlider = null;
    private Canvas healthBarCanvas = null;
    private Camera mainCameraForBillboard; // Referens till huvudkameran för billboard

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        unitRenderer = GetComponentInChildren<Renderer>();
        if (unitRenderer != null)
        {
            originalColor = unitRenderer.material.color;
        }
        currentHealth = maxHealth;
    }

    void Start() // *** Bättre att skapa UI i Start, efter Awake ***
    {
        mainCameraForBillboard = Camera.main; // Hitta huvudkameran
        SetupHealthBar();
        UpdateHealthBar(); // Uppdatera direkt vid start
    }


    void SetupHealthBar()
    {
        if (healthBarPrefab == null)
        {
            Debug.LogError("Health Bar Prefab is not assigned on " + gameObject.name);
            return;
        }

        // Bestäm positionen
        Transform spawnParent = (healthBarSpawnPoint != null) ? healthBarSpawnPoint : this.transform;
        Vector3 spawnPos = (healthBarSpawnPoint != null) ? healthBarSpawnPoint.position : transform.position + healthBarOffset;

        // 1. Skapa Canvas-objektet som ska hålla health bar
        GameObject canvasGO = new GameObject(gameObject.name + "_HealthBarCanvas");
        // Gör canvas till barn av enheten ELLER spawn point för att följa med
        canvasGO.transform.SetParent(spawnParent, false); // false = behåll lokal position/rotation/skala
        canvasGO.transform.position = spawnPos; // Sätt initial världsposition

        // Lägg till Canvas-komponent och konfigurera för World Space
        healthBarCanvas = canvasGO.AddComponent<Canvas>();
        healthBarCanvas.renderMode = RenderMode.WorldSpace;
        healthBarCanvas.worldCamera = mainCameraForBillboard; // Viktigt för World Space UI skalning/events

        // Justera Canvas RectTransform (viktigt!)
        RectTransform canvasRect = canvasGO.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(100, 20); // Exempelstorlek på canvas-arean
        canvasRect.localScale = new Vector3(0.01f, 0.01f, 0.01f); // ** SKALA NER DEN! ** World space canvas är ofta enorm först. Testa dig fram!

        // Lägg till en Graphic Raycaster om du *någonsin* vill klicka på den (behövs ej nu)
        // canvasGO.AddComponent<GraphicRaycaster>();

        // 2. Instansiera själva Health Bar (Slidern) från prefaben
        GameObject healthBarInstance = Instantiate(healthBarPrefab, canvasGO.transform); // Gör den till barn av Canvas

        // Hitta Slider-komponenten på den instansierade prefaben
        healthBarSlider = healthBarInstance.GetComponent<Slider>();
        if (healthBarSlider == null)
        {
            Debug.LogError("Instantiated Health Bar Prefab does not have a Slider component!", healthBarInstance);
            return;
        }

        // Nollställ Sliderns lokala position/rotation inom sin canvas (om den inte redan är det i prefaben)
        // healthBarInstance.GetComponent<RectTransform>().anchoredPosition3D = Vector3.zero;
        // healthBarInstance.GetComponent<RectTransform>().localRotation = Quaternion.identity;

        // Lägg till Billboard-scriptet på Canvas-objektet så det alltid tittar mot kameran
        if (mainCameraForBillboard != null)
        {
            canvasGO.AddComponent<Billboard>().SetCameraToFace(mainCameraForBillboard);
        }

    }

    // *** NY METOD FÖR ATT UPPDATERA HEALTH BAR ***
    void UpdateHealthBar()
    {
        if (healthBarSlider == null || healthBarCanvas == null) return; // Om något gick fel vid setup

        // Beräkna procent
        float healthPercent = 0f;
        if (maxHealth > 0) // Undvik division med noll
        {
            healthPercent = Mathf.Clamp01(currentHealth / maxHealth);
        }

        // Sätt värdet på Slidern
        healthBarSlider.value = healthPercent;

        // Valfritt: Dölj health bar om hälsan är full eller enheten är död
        bool shouldBeVisible = (currentHealth < maxHealth && currentHealth > 0);
        if (healthBarCanvas.gameObject.activeSelf != shouldBeVisible)
        {
            healthBarCanvas.gameObject.SetActive(shouldBeVisible);
        }
    }

    // --- Uppdatera befintliga metoder ---

    public void TakeDamage(float damageAmount)
    {
        if (currentHealth <= 0) return;
        currentHealth -= damageAmount;
        // Debug.Log(gameObject.name + " took " + damageAmount + " damage, health now: " + currentHealth);

        UpdateHealthBar(); // *** Anropa uppdatering efter skada ***

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        // ... (befintlig kod för Die) ...
        Debug.Log(gameObject.name + " died!");
        currentState = UnitState.Idle;
        agent.ResetPath();
        if (agent.isOnNavMesh) agent.enabled = false; // Kolla om agenten är på NavMesh innan den stängs av

        GetComponent<Collider>().enabled = false;

        // *** Dölj/förstör health bar när enheten dör ***
        if (healthBarCanvas != null)
        {
            Destroy(healthBarCanvas.gameObject);
        }

        Destroy(gameObject, 3f);
    }

    // --- Resten av Update, Select, Deselect, OrderMoveTo, OrderAttackTarget etc. ---
    // Ingen ändring behövs i dessa förutom att UpdateHealthBar anropas vid TakeDamage
    // ... (kopiera in resten av metoderna från föregående script) ...
    void Update()
    {
        // State machine logic
        switch (currentState)
        {
            case UnitState.Idle:
                break;
            case UnitState.MovingToDestination:
                if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
                {
                    if (!agent.hasPath || agent.velocity.sqrMagnitude == 0f)
                    {
                        currentState = UnitState.Idle;
                    }
                }
                break;
            case UnitState.MovingToAttackTarget:
                HandleMovingToAttackTarget();
                break;
            case UnitState.Attacking:
                HandleAttackingState();
                break;
        }
    }
    void HandleMovingToAttackTarget()
    { /* ... som tidigare ... */
        if (currentTarget == null) { currentState = UnitState.Idle; agent.ResetPath(); return; }
        agent.SetDestination(currentTarget.position);
        if (Vector3.Distance(transform.position, currentTarget.position) <= attackRange)
        {
            agent.ResetPath(); currentState = UnitState.Attacking; transform.LookAt(currentTarget);
        }
    }
    void HandleAttackingState()
    { /* ... som tidigare ... */
        if (currentTarget == null) { currentState = UnitState.Idle; return; }
        if (Vector3.Distance(transform.position, currentTarget.position) > attackRange)
        {
            currentState = UnitState.MovingToAttackTarget; return;
        }
        transform.LookAt(currentTarget);
        if (Time.time >= lastAttackTime + attackCooldown) { PerformAttack(); lastAttackTime = Time.time; }
    }
    void PerformAttack()
    { /* ... som tidigare ... */
        Debug.Log(gameObject.name + " attacks " + currentTarget.name);
        Unit targetUnit = currentTarget.GetComponent<Unit>();
        if (targetUnit != null) { targetUnit.TakeDamage(attackDamage); }
        else { currentTarget = null; currentState = UnitState.Idle; }
    }
    public void OrderMoveTo(Vector3 destination)
    { /* ... som tidigare ... */
        if (currentHealth <= 0) return;
        currentTarget = null; agent.SetDestination(destination); agent.stoppingDistance = 0.5f; currentState = UnitState.MovingToDestination;
    }
    public void OrderAttackTarget(Transform target)
    { /* ... som tidigare ... */
        if (currentHealth <= 0 || target == null) return;
        Unit targetUnit = target.GetComponent<Unit>();
        if (targetUnit != null && targetUnit.teamID != this.teamID)
        {
            currentTarget = target; agent.stoppingDistance = attackRange * 0.8f; currentState = UnitState.MovingToAttackTarget;
        }
        else { /* Ignorera/logga */ }
    }
    public void Select()
    { /* ... som tidigare ... */
        isSelected = true; if (unitRenderer != null) { unitRenderer.material.color = Color.green; }
    }
    public void Deselect()
    { /* ... som tidigare ... */
        isSelected = false; if (unitRenderer != null) { unitRenderer.material.color = originalColor; }
    }
}
