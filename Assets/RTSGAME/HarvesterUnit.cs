// HarvesterUnit.cs (Med extra debug-loggar i CheckIfFullOrFindNextCrystal)
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

[RequireComponent(typeof(Unit))]
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class HarvesterUnit : MonoBehaviour
{
    [Header("Harvester Settings")]
    [Tooltip("Max antal kristaller Golemen kan bära.")]
    public int carryCapacity = 5;
    [Tooltip("Hur nära en kristall måste vara för att plockas upp.")]
    public float pickupRange = 1.0f;
    [Tooltip("Hur lång tid (sekunder) upplockningsanimationen + cooldown tar.")]
    public float pickupDuration = 1.5f;

    [Header("Inventory Bar")]
    [Tooltip("Dra in din prefab för inventarie-mätaren (blå slider) här.")]
    public GameObject inventoryBarPrefab;

    [Header("State (Internal)")]
    [Tooltip("Antal kristaller som bärs just nu.")]
    public int currentLoad = 0;
    private CrystalType carriedCrystalType = CrystalType.None;
    public enum HarvesterState { Idle, MovingToCrystal, Gathering, MovingToRefinery, PositioningForDropOff, WaitingForRefinery, Depositing }
    public HarvesterState currentState = HarvesterState.Idle;

    // Referenser
    private Unit unitInfo;
    private NavMeshAgent agent;
    private Animator animator;
    private HarvestableCrystal targetCrystal = null;
    private RefineryBuilding targetRefinery = null;
    private Slider inventoryBarSlider = null;
    private Image inventoryBarFillImage = null;

    // Timers
    private float checkRefineryTimer = 0f;
    private float checkInterval = 0.5f; // Glöm inte att denna deklarerades tidigare
    private float gatherTimer = 0f;

    // Cache
    private PlayerResourceManager resourceManager;

    // Animator Parameter IDs
    private static readonly int IsMiningParam = Animator.StringToHash("IsMining");
    private static readonly int SpeedParam = Animator.StringToHash("Speed");

    void Awake()
    {
        unitInfo = GetComponent<Unit>();
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
    }

    void Start()
    {
        resourceManager = FindAnyObjectByType<PlayerResourceManager>();
        if (resourceManager == null) { Debug.LogError($"Harvester '{gameObject.name}' could not find PlayerResourceManager!", this); }
        SetupInventoryBar();
        UpdateInventoryBar();
        SetMiningAnimation(false);
        currentState = HarvesterState.Idle;
    }

    void SetupInventoryBar()
    {
        if (unitInfo == null || inventoryBarPrefab == null) { if (inventoryBarPrefab == null) Debug.LogWarning("Inventory Bar Prefab not assigned.", this); return; }
        Canvas sharedCanvas = unitInfo.GetHealthBarCanvas();
        if (sharedCanvas != null)
        {
            GameObject inventoryBarInstance = Instantiate(inventoryBarPrefab, sharedCanvas.transform);
            inventoryBarSlider = inventoryBarInstance.GetComponent<Slider>();
            if (inventoryBarSlider != null)
            {
                Transform fillTransform = inventoryBarSlider.transform.Find("Fill Area/Fill");
                if (fillTransform != null) { inventoryBarFillImage = fillTransform.GetComponent<Image>(); }
                if (inventoryBarFillImage == null) { Debug.LogError("Could not find Fill Image on Inventory Bar!", inventoryBarSlider.gameObject); }
                // Positionera
                RectTransform healthBarRect = null; Slider[] slidersInParent = sharedCanvas.GetComponentsInChildren<Slider>();
                foreach (Slider slider in slidersInParent) { if (slider != inventoryBarSlider) { healthBarRect = slider.GetComponent<RectTransform>(); break; } }
                float healthBarHeight = (healthBarRect != null) ? healthBarRect.sizeDelta.y : 10f; float spacing = 2f;
                RectTransform invBarRect = inventoryBarSlider.GetComponent<RectTransform>(); invBarRect.anchoredPosition = new Vector2(0, healthBarHeight + spacing);
                inventoryBarSlider.gameObject.SetActive(true);
            }
            else { Debug.LogError("Inventory Bar Prefab lacks Slider!", inventoryBarInstance); }
        }
        else { Debug.LogError("Could not get HealthBarCanvas from Unit!", this); }
    }

    void Update()
    {
        if (unitInfo == null || unitInfo.currentHealth <= 0) return;
        switch (currentState)
        {
            case HarvesterState.Idle: FindWork(); break;
            case HarvesterState.MovingToCrystal: MoveToCrystalUpdate(); break;
            case HarvesterState.Gathering: GatheringUpdate(); break;
            case HarvesterState.MovingToRefinery: MoveToRefineryUpdate(); break;
            case HarvesterState.PositioningForDropOff: AttemptDeposit(); break;
            case HarvesterState.WaitingForRefinery: WaitingForRefineryUpdate(); break;
            case HarvesterState.Depositing: break;
        }
        UpdateAnimatorSpeed();
    }

    // --- State Logic ---

    void FindWork()
    {
        SetMiningAnimation(false);
        if (carriedCrystalType != CrystalType.None)
        {
            if (targetRefinery == null || !targetRefinery.gameObject.activeInHierarchy) targetRefinery = FindClosestRefinery();
            if (targetRefinery != null)
            {
                if (currentState != HarvesterState.MovingToRefinery && currentState != HarvesterState.WaitingForRefinery && currentState != HarvesterState.Depositing && currentState != HarvesterState.PositioningForDropOff)
                {
                    currentState = HarvesterState.MovingToRefinery; agent.stoppingDistance = 1.5f; agent.SetDestination(targetRefinery.dockingPoint.position);
                }
            }
            else { currentState = HarvesterState.Idle; }
            return;
        }
        targetCrystal = FindClosestAvailableCrystal();
        if (targetCrystal != null)
        {
            agent.stoppingDistance = pickupRange * 0.8f; agent.SetDestination(targetCrystal.transform.position); currentState = HarvesterState.MovingToCrystal;
        }
    }

    void MoveToCrystalUpdate()
    {
        if (targetCrystal == null || !targetCrystal.gameObject.activeInHierarchy)
        {
            currentState = HarvesterState.Idle; agent.ResetPath(); targetCrystal = null; SetMiningAnimation(false); return;
        }
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f)
        {
            currentState = HarvesterState.Gathering; gatherTimer = 0f; agent.ResetPath();
            if (targetCrystal != null) transform.LookAt(targetCrystal.transform.position);
            SetMiningAnimation(true); // Starta mining-animation
        }
    }

    void GatheringUpdate()
    {
        if (targetCrystal == null || !targetCrystal.gameObject.activeInHierarchy) { SetMiningAnimation(false); CheckIfFullOrFindNextCrystal(); return; }
        if (Vector3.Distance(transform.position, targetCrystal.transform.position) > pickupRange * 1.1f)
        {
            SetMiningAnimation(false); currentState = HarvesterState.MovingToCrystal; agent.stoppingDistance = pickupRange * 0.8f; agent.SetDestination(targetCrystal.transform.position); return;
        }
        transform.LookAt(targetCrystal.transform.position);

        gatherTimer += Time.deltaTime;

        if (gatherTimer >= pickupDuration)
        {
            HarvestableCrystal crystalInfo = targetCrystal.GetComponent<HarvestableCrystal>();
            if (crystalInfo != null)
            {
                if (currentLoad == 0) { carriedCrystalType = crystalInfo.type; }
                if (carriedCrystalType == crystalInfo.type)
                {
                    currentLoad++; UpdateInventoryBar();
                    Destroy(targetCrystal.gameObject); targetCrystal = null;
                }
                else { /* Ignorera fel typ, leta ny i CheckIfFull... */ }
            }
            else { if (targetCrystal != null) Destroy(targetCrystal.gameObject); targetCrystal = null; }

            SetMiningAnimation(false); // Sluta minea direkt efter pickup
            gatherTimer = 0f;          // Nollställ timer inför nästa ev. pickup
            CheckIfFullOrFindNextCrystal(); // Kolla vad som ska hända nu
        }
    }

    // *** Metod med extra Debug.Log ***
    void CheckIfFullOrFindNextCrystal()
    {
        gatherTimer = 0f; // Dubbelkolla nollställning
        // Mining-animationen bör redan vara avstängd från GatheringUpdate
        // SetMiningAnimation(false);

        // *** NYA LOGGAR ***
        Debug.Log($"Checking status after pickup: CurrentLoad={currentLoad}, CarryCapacity={carryCapacity}");

        if (currentLoad >= carryCapacity)
        {
            // Full last -> Refinery
            Debug.Log("Load is full or capacity reached. Finding refinery."); // *** NY LOGG ***
            targetRefinery = FindClosestRefinery();
            if (targetRefinery != null)
            {
                currentState = HarvesterState.MovingToRefinery;
                agent.stoppingDistance = 1.5f;
                agent.SetDestination(targetRefinery.dockingPoint.position);
                Debug.Log($"Moving to refinery: {targetRefinery.name}"); // *** NY LOGG ***
            }
            else
            {
                Debug.LogWarning("Load full, but NO refinery found! Going Idle.", this); // *** NY LOGG ***
                currentState = HarvesterState.Idle;
                // Stanna kvar, hoppas ett refinery dyker upp?
                agent.ResetPath();
            }
        }
        else
        {
            // Inte full -> Leta nästa kristall av SAMMA typ
            Debug.Log($"Not full. Looking for next crystal of type: {carriedCrystalType}"); // *** NY LOGG ***
            targetCrystal = FindClosestAvailableCrystalOfType(carriedCrystalType);
            if (targetCrystal != null)
            { // Hittade en till
                Debug.Log($"Found next crystal: {targetCrystal.name}. Moving."); // *** NY LOGG ***
                currentState = HarvesterState.MovingToCrystal;
                agent.stoppingDistance = pickupRange * 0.8f;
                agent.SetDestination(targetCrystal.transform.position);
            }
            else
            { // Inga fler av samma typ, åk och lämna
                Debug.Log($"Could not find another available crystal of type {carriedCrystalType}. Heading to refinery."); // *** NY LOGG ***
                targetRefinery = FindClosestRefinery();
                if (targetRefinery != null)
                {
                    currentState = HarvesterState.MovingToRefinery;
                    agent.stoppingDistance = 1.5f;
                    agent.SetDestination(targetRefinery.dockingPoint.position);
                    Debug.Log($"Moving to refinery: {targetRefinery.name}"); // *** NY LOGG ***
                }
                else
                {
                    Debug.LogWarning($"Cannot find refinery to deposit partial load ({currentLoad})! Going Idle.", this); // *** NY LOGG ***
                    currentState = HarvesterState.Idle;
                    agent.ResetPath();
                }
            }
        }
        // *** SLUT PÅ NYA LOGGAR ***
    }

    void UpdateInventoryBar()
    { /* ... som tidigare ... */
        if (inventoryBarSlider == null) return;
        float fillAmount = (carryCapacity > 0) ? (float)currentLoad / carryCapacity : 0f;
        inventoryBarSlider.value = Mathf.Clamp01(fillAmount);
        if (inventoryBarFillImage != null)
        {
            Color targetColor;
            switch (carriedCrystalType)
            {
                case CrystalType.Green: targetColor = Color.green; break;
                case CrystalType.Blue: targetColor = Color.blue; break;
                case CrystalType.Red: targetColor = Color.red; break;
                default: targetColor = Color.grey; break;
            }
            if (currentLoad == 0) { targetColor = Color.grey; }
            inventoryBarFillImage.color = targetColor;
        }
        if (inventoryBarSlider != null && !inventoryBarSlider.gameObject.activeSelf) { inventoryBarSlider.gameObject.SetActive(true); }
    }

    public void CompleteDeposit()
    { /* ... som tidigare ... */
        if (currentState != HarvesterState.Depositing)
        {
            currentLoad = 0; carriedCrystalType = CrystalType.None; UpdateInventoryBar();
            currentState = HarvesterState.Idle; agent.ResetPath(); SetMiningAnimation(false); return;
        }
        int valuePerCrystal = GetValueForCrystalType(carriedCrystalType); int totalValue = currentLoad * valuePerCrystal;
        if (totalValue > 0 && resourceManager != null) { resourceManager.AddResources(totalValue); }
        else if (resourceManager == null) { Debug.LogError("Harvester could not find PlayerResourceManager!"); }
        currentLoad = 0; carriedCrystalType = CrystalType.None; UpdateInventoryBar();
        currentState = HarvesterState.Idle; SetMiningAnimation(false);
    }

    // --- Hjälpmetoder för Animator ---
    void SetMiningAnimation(bool isMining) { animator?.SetBool(IsMiningParam, isMining); }
    void UpdateAnimatorSpeed()
    {
        if (animator == null || agent == null || !agent.isOnNavMesh) return;
        bool isMovingState = (currentState == HarvesterState.MovingToCrystal || currentState == HarvesterState.MovingToRefinery);
        float speed = isMovingState ? agent.velocity.magnitude : 0f;
        animator.SetFloat(SpeedParam, speed, 0.1f, Time.deltaTime);
    }

    // --- Metoder för att hitta mål ---
    HarvestableCrystal FindClosestAvailableCrystal()
    { /* ... som tidigare ... */
        HarvestableCrystal[] allCrystals = FindObjectsOfType<HarvestableCrystal>(); HarvestableCrystal closestCrystal = null; float minDistance = float.MaxValue;
        foreach (HarvestableCrystal crystal in allCrystals) { if (crystal != null && crystal.gameObject.activeInHierarchy) { float distance = Vector3.Distance(transform.position, crystal.transform.position); if (distance < minDistance) { minDistance = distance; closestCrystal = crystal; } } }
        return closestCrystal;
    }
    HarvestableCrystal FindClosestAvailableCrystalOfType(CrystalType type)
    { /* ... som tidigare ... */
        HarvestableCrystal[] allCrystals = FindObjectsOfType<HarvestableCrystal>(); HarvestableCrystal closestCrystal = null; float minDistance = float.MaxValue;
        foreach (HarvestableCrystal crystal in allCrystals) { if (crystal != null && crystal.gameObject.activeInHierarchy && crystal.type == type) { float distance = Vector3.Distance(transform.position, crystal.transform.position); if (distance < minDistance) { minDistance = distance; closestCrystal = crystal; } } }
        return closestCrystal;
    }
    RefineryBuilding FindClosestRefinery()
    { /* ... som tidigare ... */
        RefineryBuilding[] allRefineries = FindObjectsOfType<RefineryBuilding>(); RefineryBuilding closestRefinery = null; float minDistance = float.MaxValue;
        foreach (RefineryBuilding refinery in allRefineries) { if (refinery != null && refinery.gameObject.activeInHierarchy) { float distance = Vector3.Distance(transform.position, refinery.transform.position); if (distance < minDistance) { minDistance = distance; closestRefinery = refinery; } } }
        return closestRefinery;
    }
    int GetValueForCrystalType(CrystalType type)
    { /* ... som tidigare ... */
        switch (type) { case CrystalType.Green: return 100; case CrystalType.Blue: return 250; case CrystalType.Red: return 500; default: return 0; }
    }

    // --- Metoder för Refinery Interaction ---
    void MoveToRefineryUpdate()
    { /* ... som tidigare ... */
        if (targetRefinery == null || !targetRefinery.gameObject.activeInHierarchy) { currentState = HarvesterState.Idle; agent.ResetPath(); targetRefinery = null; SetMiningAnimation(false); return; }
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f) { AttemptDeposit(); }
    }
    void AttemptDeposit()
    { /* ... som tidigare ... */
        if (targetRefinery == null) { currentState = HarvesterState.Idle; agent.ResetPath(); SetMiningAnimation(false); return; }
        bool couldStart = targetRefinery.RequestUnload(this);
        if (couldStart) { currentState = HarvesterState.Depositing; agent.ResetPath(); agent.stoppingDistance = 0.5f; SetMiningAnimation(false); }
        else { currentState = HarvesterState.WaitingForRefinery; agent.stoppingDistance = 5f; if (agent.pathStatus != NavMeshPathStatus.PathInvalid && agent.destination != targetRefinery.dockingPoint.position) { agent.SetDestination(targetRefinery.dockingPoint.position); } checkRefineryTimer = checkInterval; SetMiningAnimation(false); }
    }
    void WaitingForRefineryUpdate()
    { /* ... som tidigare ... */
        checkRefineryTimer += Time.deltaTime; if (checkRefineryTimer >= checkInterval)
        {
            checkRefineryTimer = 0f; if (targetRefinery == null || !targetRefinery.gameObject.activeInHierarchy) { currentState = HarvesterState.Idle; agent.ResetPath(); SetMiningAnimation(false); return; }
            if (!targetRefinery.isCurrentlyUnloading) { bool couldStart = targetRefinery.RequestUnload(this); if (couldStart) { agent.stoppingDistance = 1.5f; agent.SetDestination(targetRefinery.dockingPoint.position); currentState = HarvesterState.Depositing; SetMiningAnimation(false); } }
        }
        if (!agent.hasPath && agent.velocity.sqrMagnitude < 0.1f && agent.isActiveAndEnabled && agent.isOnNavMesh) { if (targetRefinery != null) { agent.SetDestination(targetRefinery.dockingPoint.position); } else { currentState = HarvesterState.Idle; SetMiningAnimation(false); } }
    }

} // End of class