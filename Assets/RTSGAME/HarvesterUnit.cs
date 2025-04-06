// Fil: HarvesterUnit.cs
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
    private HarvestableCrystal targetCrystal = null; // Ändrad från public till private
    private RefineryBuilding targetRefinery = null; // Ändrad från public till private
    private Slider inventoryBarSlider = null;
    private Image inventoryBarFillImage = null;

    // Timers
    private float checkRefineryTimer = 0f;
    private float checkInterval = 0.5f;
    private float gatherTimer = 0f;

    // Cache
    private PlayerResourceManager resourceManager;

    // Animator Parameter IDs
    private static readonly int IsMiningParam = Animator.StringToHash("IsMining");
    private static readonly int SpeedParam = Animator.StringToHash("Forward");

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
        GoToIdleState(false); // Starta i Idle state korrekt
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

                // Positionera relativt till Health Bar (om den finns)
                RectTransform healthBarRect = null;
                Slider[] slidersInParent = sharedCanvas.GetComponentsInChildren<Slider>();
                foreach (Slider slider in slidersInParent)
                {
                    if (slider != inventoryBarSlider && slider.GetComponentInParent<Unit>() == unitInfo)
                    { // Se till att det är vår health bar
                        healthBarRect = slider.GetComponent<RectTransform>();
                        break;
                    }
                }
                // Om health bar hittas, positionera under den, annars i mitten.
                float yOffset = 0f;
                if (healthBarRect != null)
                {
                    float healthBarHeight = healthBarRect.sizeDelta.y * healthBarRect.localScale.y; // Ta hänsyn till scale
                    float spacing = 2f * healthBarRect.localScale.y; // Skala spacing också
                    yOffset = -(healthBarHeight + spacing); // Negativt för att placera under
                }

                RectTransform invBarRect = inventoryBarSlider.GetComponent<RectTransform>();
                invBarRect.anchoredPosition = new Vector2(0, yOffset); // Använd beräknad offset
                inventoryBarSlider.gameObject.SetActive(true);
            }
            else { Debug.LogError("Inventory Bar Prefab lacks Slider!", inventoryBarInstance); }
        }
        else { Debug.LogError("Could not get HealthBarCanvas from Unit!", this); }
    }

    void Update()
    {
        if (unitInfo == null || unitInfo.currentHealth <= 0) return; // Pausa om död

        switch (currentState)
        {
            case HarvesterState.Idle: FindWork(); break;
            case HarvesterState.MovingToCrystal: MoveToCrystalUpdate(); break;
            case HarvesterState.Gathering: GatheringUpdate(); break;
            case HarvesterState.MovingToRefinery: MoveToRefineryUpdate(); break;
            case HarvesterState.PositioningForDropOff: AttemptDeposit(); break; // Antog att detta leder till AttemptDeposit
            case HarvesterState.WaitingForRefinery: WaitingForRefineryUpdate(); break;
            case HarvesterState.Depositing: break; // Ingen logik här, styrs av Refinery
        }
        UpdateAnimatorSpeed();
    }

    // --- NY Hjälpmetod för att byta till Idle och hantera reservation ---
    void GoToIdleState(bool releaseCurrentTarget)
    {
        // Släpp reservationen om vi hade en kristall som mål och ska släppa den
        if (releaseCurrentTarget && targetCrystal != null)
        {
            targetCrystal.Release(this); // Anropa Release på kristallen
            Debug.Log($"{gameObject.name} released target {targetCrystal.name} due to state change to Idle.");
        }
        targetCrystal = null; // Rensa alltid referensen

        // Resten av Idle-logiken
        currentState = HarvesterState.Idle;
        // Stanna agenten om den inte redan är stoppad
        if (agent.isOnNavMesh && !agent.isStopped)
        {
            agent.ResetPath(); // Stoppar och rensar vägen
                               // agent.isStopped = true; // Alternativt sätt att stoppa
        }
        SetMiningAnimation(false);

        // targetRefinery behöver normalt inte rensas här, FindWork hanterar det.
    }


    // --- State Logic (med reservation) ---

    void FindWork()
    {
        SetMiningAnimation(false);

        // 1. Om vi bär på något, hitta refinery
        if (carriedCrystalType != CrystalType.None)
        {
            if (targetRefinery == null || !targetRefinery.gameObject.activeInHierarchy) targetRefinery = FindClosestRefinery();
            if (targetRefinery != null)
            {
                // Se till att vi inte redan är på väg dit eller väntar
                if (currentState != HarvesterState.MovingToRefinery && currentState != HarvesterState.WaitingForRefinery && currentState != HarvesterState.Depositing && currentState != HarvesterState.PositioningForDropOff)
                {
                    currentState = HarvesterState.MovingToRefinery;
                    agent.stoppingDistance = 1.5f; // Anpassa efter refinery dockningspunkt
                    agent.SetDestination(targetRefinery.dockingPoint.position);
                }
            }
            else
            {
                // Ingen refinery, gå Idle men släpp ingen kristall (vi har ingen som mål)
                Debug.LogWarning($"{gameObject.name} carries resources but no refinery found. Going Idle.", this);
                GoToIdleState(false); // Använd hjälpmetoden
            }
            return; // Viktigt att avsluta här
        }

        // 2. Om vi är tomma, försök hitta och reservera en kristall
        targetCrystal = FindClosestAvailableCrystal(); // Använder nu den modifierade metoden

        if (targetCrystal != null)
        {
            // Reservationen lyckades (hanteras inuti FindClosest...)
            agent.stoppingDistance = pickupRange * 0.8f;
            agent.SetDestination(targetCrystal.transform.position);
            currentState = HarvesterState.MovingToCrystal;
            // Debug-log finns nu i FindClosest...
        }
        else
        {
            // Hittade ingen *ledig* kristall just nu. Stanna i Idle.
            GoToIdleState(false); // Gå till Idle, ingen kristall att släppa.
                                  // Debug-log finns nu i FindClosest...
        }
    }

    void MoveToCrystalUpdate()
    {
        // Kolla om målet försvunnit ELLER om någon annan snott reservationen!
        if (targetCrystal == null || !targetCrystal.gameObject.activeInHierarchy || targetCrystal.targetedBy != this)
        {
            Debug.Log($"{gameObject.name} lost target crystal {(targetCrystal?.name ?? "NULL")} or its reservation while moving. Going Idle.");
            // Gå till Idle. VIKTIGT: Släpp INTE reservationen här (false) eftersom den antingen är borta
            // eller ägs av någon annan nu. Vi rensar bara vår egen referens.
            GoToIdleState(false); // Använd hjälpmetoden
            return;
        }

        // Kolla om vi är framme
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f)
        {
            // Check if we actually stopped moving close to the target
            if (agent.velocity.sqrMagnitude < 0.1f)
            {
                currentState = HarvesterState.Gathering;
                gatherTimer = 0f;
                agent.ResetPath(); // Stop movement completely
                transform.LookAt(targetCrystal.transform.position); // Face the crystal
                SetMiningAnimation(true); // Starta mining-animation
            }
        }
    }

    void GatheringUpdate()
    {
        // Kolla om målet försvunnit ELLER om någon annan snott reservationen!
        if (targetCrystal == null || !targetCrystal.gameObject.activeInHierarchy || targetCrystal.targetedBy != this)
        {
            Debug.Log($"{gameObject.name} lost target crystal {(targetCrystal?.name ?? "NULL")} or reservation during gathering. Finding next.");
            SetMiningAnimation(false);
            // Gå direkt till att kolla om vi är fulla eller ska leta ny. Ingen release behövs.
            CheckIfFullOrFindNextCrystal();
            return;
        }

        // Stå still och titta på kristallen
        agent.ResetPath(); // Ensure we are stopped
        transform.LookAt(targetCrystal.transform.position);

        // Kolla om vi drev iväg (t.ex. knuffad)
        if (Vector3.Distance(transform.position, targetCrystal.transform.position) > pickupRange * 1.1f)
        {
            Debug.Log($"{gameObject.name} drifted too far from {targetCrystal.name} while gathering. Re-pathing.");
            SetMiningAnimation(false);
            currentState = HarvesterState.MovingToCrystal;
            agent.stoppingDistance = pickupRange * 0.8f;
            agent.SetDestination(targetCrystal.transform.position);
            return;
        }

        // Öka insamlingstimer
        gatherTimer += Time.deltaTime;

        // Kolla om insamlingen är klar
        if (gatherTimer >= pickupDuration)
        {
            HarvestableCrystal crystalInfo = targetCrystal.GetComponent<HarvestableCrystal>(); // Borde finnas

            if (crystalInfo != null)
            {
                if (currentLoad == 0)
                { // Första kristallen sätter typen
                    carriedCrystalType = crystalInfo.type;
                    Debug.Log($"{gameObject.name} started collecting type {carriedCrystalType}");
                }

                if (carriedCrystalType == crystalInfo.type)
                { // Samla bara om det är rätt typ
                    currentLoad++;
                    UpdateInventoryBar();
                    Debug.Log($"{gameObject.name} harvested {crystalInfo.name}. Load: {currentLoad}/{carryCapacity}");

                    // *** VIKTIGT: Förstör kristallen. Ingen Release() behövs här,
                    // reservationen försvinner med objektet. ***
                    Destroy(targetCrystal.gameObject);
                    targetCrystal = null; // Rensa vår referens
                }
                else
                {
                    // Vi nådde fram till en kristall av fel typ (borde inte hända med FindOfType men som säkerhet)
                    Debug.LogWarning($"{gameObject.name} reached crystal {crystalInfo.name} of wrong type ({crystalInfo.type}), expected {carriedCrystalType}. Releasing and searching again.");
                    // Släpp reservationen på den felaktiga kristallen så någon annan kan ta den
                    crystalInfo.Release(this);
                    targetCrystal = null; // Rensa referens
                }
            }
            else
            {
                // Borde inte hända om första null-checken passerades, men för säkerhets skull
                Debug.LogError($"TargetCrystal {targetCrystal?.name ?? "NULL"} lost its HarvestableCrystal component during gathering!", this);
                if (targetCrystal != null) Destroy(targetCrystal.gameObject); // Försök städa upp
                targetCrystal = null;
            }

            // Återställ och gå vidare oavsett om vi lyckades eller ej
            SetMiningAnimation(false);
            gatherTimer = 0f;
            CheckIfFullOrFindNextCrystal(); // Kolla vad som ska hända nu
        }
    }


    void CheckIfFullOrFindNextCrystal()
    {
        gatherTimer = 0f; // Säkerställ nollställning
                          // SetMiningAnimation(false); // Ska redan vara gjord i GatheringUpdate

        // Om vi inte bär något (t.ex. om vi försökte plocka fel typ)
        if (currentLoad == 0)
        {
            Debug.Log($"{gameObject.name} has empty load after gathering attempt. Going Idle to find new work.");
            GoToIdleState(false); // Gå Idle, ingen kristall att släppa
            return;
        }

        // Om vi är fulla, åk till refinery
        if (currentLoad >= carryCapacity)
        {
            Debug.Log($"{gameObject.name} is full ({currentLoad}/{carryCapacity}). Finding refinery.");
            targetRefinery = FindClosestRefinery();
            if (targetRefinery != null)
            {
                currentState = HarvesterState.MovingToRefinery;
                agent.stoppingDistance = 1.5f;
                agent.SetDestination(targetRefinery.dockingPoint.position);
                Debug.Log($"{gameObject.name} moving to refinery: {targetRefinery.name}");
            }
            else
            {
                Debug.LogWarning($"{gameObject.name} Load full, but NO refinery found! Going Idle.", this);
                // Gå Idle, men vi måste släppa det vi håller på med (leta kristaller).
                // Ingen kristall är target just nu, så release=false.
                GoToIdleState(false);
            }
        }
        else
        {
            // Inte full -> Leta nästa *tillgängliga* kristall av SAMMA typ
            Debug.Log($"{gameObject.name} Not full. Looking for next available crystal of type: {carriedCrystalType}");
            targetCrystal = FindClosestAvailableCrystalOfType(carriedCrystalType); // Använder modifierad metod

            if (targetCrystal != null)
            { // Hittade och reserverade en till
              // Debug-log finns i FindClosest...
                currentState = HarvesterState.MovingToCrystal;
                agent.stoppingDistance = pickupRange * 0.8f;
                agent.SetDestination(targetCrystal.transform.position);
            }
            else
            { // Inga fler tillgängliga av samma typ, åk och lämna det vi har
                Debug.Log($"{gameObject.name} Could not find another available crystal of type {carriedCrystalType}. Heading to refinery with partial load.");
                targetRefinery = FindClosestRefinery();
                if (targetRefinery != null)
                {
                    currentState = HarvesterState.MovingToRefinery;
                    agent.stoppingDistance = 1.5f;
                    agent.SetDestination(targetRefinery.dockingPoint.position);
                    Debug.Log($"{gameObject.name} moving to refinery: {targetRefinery.name}");
                }
                else
                {
                    Debug.LogWarning($"{gameObject.name} Cannot find refinery to deposit partial load ({currentLoad})! Going Idle.", this);
                    // Gå Idle. Ingen kristall är target, så release=false.
                    GoToIdleState(false);
                }
            }
        }
    }

    void UpdateInventoryBar()
    {
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
                default: targetColor = Color.grey; break; // Grå vid None eller 0 last
            }
            if (currentLoad == 0) { targetColor = Color.grey; } // Explicit grå om tom
            inventoryBarFillImage.color = targetColor;
        }
        // Ensure the bar is active if it exists
        if (!inventoryBarSlider.gameObject.activeSelf) { inventoryBarSlider.gameObject.SetActive(true); }
    }

    public void CompleteDeposit()
    {
        // Denna metod anropas av Refinery när urlastningen är klar
        // Vi behöver inte vara i Depositing state längre, bara ta emot signalen.
        // if (currentState != HarvesterState.Depositing) {
        //     Debug.LogWarning($"{gameObject.name} received CompleteDeposit but wasn't in Depositing state?", this);
        //     // Kan hända om refinery signalerar sent, bara återställ ändå.
        // }

        int valuePerCrystal = GetValueForCrystalType(carriedCrystalType);
        int totalValue = currentLoad * valuePerCrystal;
        Debug.Log($"{gameObject.name} depositing {currentLoad} crystals of type {carriedCrystalType} for a total value of {totalValue}");

        if (totalValue > 0 && resourceManager != null)
        {
            resourceManager.AddResources(totalValue);
        }
        else if (resourceManager == null)
        {
            Debug.LogError("Harvester could not find PlayerResourceManager during deposit!", this);
        }

        // Återställ och gå Idle för att hitta nytt jobb
        currentLoad = 0;
        carriedCrystalType = CrystalType.None;
        UpdateInventoryBar(); // Nollställ färg och värde
        GoToIdleState(false); // Gå till Idle, ingen kristall att släppa
    }

    // --- Hjälpmetoder för Animator ---
    void SetMiningAnimation(bool isMining)
    {
        // Undvik fel om animatorn saknas
        if (animator == null) return;
        // Undvik onödiga SetBool om värdet inte ändras
        if (animator.GetBool(IsMiningParam) != isMining)
        {
            animator.SetBool(IsMiningParam, isMining);
        }
    }

    void UpdateAnimatorSpeed()
    {
        if (animator == null || agent == null || !agent.isOnNavMesh) return;
        // Använd agentens önskade hastighet istället för faktisk för mjukare övergångar
        float speed = agent.desiredVelocity.magnitude;
        // Låt animatorn veta hastigheten (för gå/spring-animation)
        animator.SetFloat(SpeedParam, speed, 0.1f, Time.deltaTime); // Smooth damp
    }

    // --- Metoder för att hitta mål (Med reservation) ---

    // Hitta närmaste *lediga* kristall oavsett typ
    HarvestableCrystal FindClosestAvailableCrystal()
    {
        HarvestableCrystal[] allCrystals = FindObjectsOfType<HarvestableCrystal>();
        HarvestableCrystal potentialTarget = null;
        float minDistance = float.MaxValue;

        foreach (HarvestableCrystal crystal in allCrystals)
        {
            // *** KOLLA OM LEDIG ***
            if (crystal != null && crystal.gameObject.activeInHierarchy && !crystal.isTargeted) // !crystal.isTargeted
            {
                float distance = Vector3.Distance(transform.position, crystal.transform.position);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    potentialTarget = crystal;
                }
            }
        }
        // *** REServera om vi hittade en ***
        if (potentialTarget != null)
        {
            if (potentialTarget.Reserve(this)) // Försök reservera
            {
                Debug.Log($"{gameObject.name} successfully reserved {potentialTarget.name}");
                return potentialTarget; // Lyckades!
            }
            else
            {
                // Någon annan hann före. Logga och returnera null. FindWork får försöka igen.
                Debug.LogWarning($"{gameObject.name} tried to reserve {potentialTarget.name} but failed (race condition?). Will retry finding another.");
                return null; // Låt FindWork hantera att ingen hittades denna gången
            }
        }
        Debug.Log($"{gameObject.name} found no available, unreserved crystal of any type.");
        return null; // Ingen ledig kristall hittades
    }

    // Hitta närmaste *lediga* kristall av specifik typ
    HarvestableCrystal FindClosestAvailableCrystalOfType(CrystalType type)
    {
        HarvestableCrystal[] allCrystals = FindObjectsOfType<HarvestableCrystal>();
        HarvestableCrystal potentialTarget = null;
        float minDistance = float.MaxValue;

        foreach (HarvestableCrystal crystal in allCrystals)
        {
            // *** KOLLA TYP OCH OM LEDIG ***
            if (crystal != null && crystal.gameObject.activeInHierarchy && crystal.type == type && !crystal.isTargeted) // !crystal.isTargeted
            {
                float distance = Vector3.Distance(transform.position, crystal.transform.position);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    potentialTarget = crystal;
                }
            }
        }
        // *** REServera om vi hittade en ***
        if (potentialTarget != null)
        {
            if (potentialTarget.Reserve(this)) // Försök reservera
            {
                Debug.Log($"{gameObject.name} successfully reserved {potentialTarget.name} of type {type}");
                return potentialTarget; // Lyckades!
            }
            else
            {
                Debug.LogWarning($"{gameObject.name} tried to reserve {potentialTarget.name} of type {type} but failed (race condition?). Will retry finding another.");
                return null; // Låt CheckIfFull... hantera att ingen hittades
            }
        }
        // Debug.Log($"{gameObject.name} found no available, unreserved crystal of type {type}."); // Lite väl spammy kanske
        return null; // Ingen ledig kristall av rätt typ hittades
    }


    // Hitta närmaste refinery (ingen ändring här)
    RefineryBuilding FindClosestRefinery()
    {
        RefineryBuilding[] allRefineries = FindObjectsOfType<RefineryBuilding>();
        RefineryBuilding closestRefinery = null;
        float minDistance = float.MaxValue;
        foreach (RefineryBuilding refinery in allRefineries)
        {
            if (refinery != null && refinery.gameObject.activeInHierarchy && refinery.dockingPoint != null)
            { // Kolla dockingPoint
                float distance = Vector3.Distance(transform.position, refinery.transform.position);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestRefinery = refinery;
                }
            }
        }
        if (closestRefinery == null) Debug.LogWarning($"{gameObject.name} could not find any active RefineryBuilding!");
        return closestRefinery;
    }

    // Få värde baserat på typ (ingen ändring här)
    int GetValueForCrystalType(CrystalType type)
    {
        // Du bör nog ha dessa värden definierade centralt eller på Crystal prefaben
        // Exempelvärden:
        switch (type)
        {
            case CrystalType.Green: return 100; // Eller hämta från crystalInfo.value om den finns
            case CrystalType.Blue: return 250;
            case CrystalType.Red: return 500;
            default: return 0;
        }
    }


    // --- Metoder för Refinery Interaction (ingen ändring här) ---
    void MoveToRefineryUpdate()
    {
        if (targetRefinery == null || !targetRefinery.gameObject.activeInHierarchy || targetRefinery.dockingPoint == null)
        {
            Debug.LogWarning($"{gameObject.name} target refinery became invalid while moving. Going Idle.", this);
            GoToIdleState(false); // Ingen kristall att släppa
            return;
        }
        // Kolla om vi är framme vid dockningspunkten
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f)
        {
            if (agent.velocity.sqrMagnitude < 0.1f)
            {
                // Stanna helt och försök lämna av
                agent.ResetPath();
                AttemptDeposit();
            }
        }
    }

    void AttemptDeposit()
    {
        if (targetRefinery == null)
        {
            Debug.LogError($"{gameObject.name} tried AttemptDeposit with null refinery!", this);
            GoToIdleState(false);
            return;
        }

        bool couldStart = targetRefinery.RequestUnload(this); // Skicka med oss själva

        if (couldStart)
        {
            // Refinery accepterade, vi går in i Depositing state (passivt, väntar på CompleteDeposit)
            currentState = HarvesterState.Depositing;
            agent.ResetPath(); // Stå still vid dockningspunkten
                               // Rotera mot refinery? (Valfritt)
            transform.LookAt(targetRefinery.transform.position);
            Debug.Log($"{gameObject.name} started deposit at {targetRefinery.name}");
            SetMiningAnimation(false); // Se till att mining är av
        }
        else
        {
            // Refinery var upptagen, gå in i Waiting state
            currentState = HarvesterState.WaitingForRefinery;
            // Backa lite? Eller stå kvar. Sätt längre stopping distance så vi inte blockerar.
            // agent.stoppingDistance = 5f; // Justera vid behov
            // Se till att vi är på väg till rätt punkt om vi inte redan är där
            // if (!agent.hasPath || agent.destination != targetRefinery.dockingPoint.position) {
            //    agent.SetDestination(targetRefinery.dockingPoint.position);
            // }
            agent.ResetPath(); // Bara stå still i närheten
            checkRefineryTimer = checkInterval; // Vänta lite innan första kollen
            Debug.Log($"{gameObject.name} waiting for refinery {targetRefinery.name} to become free.");
            SetMiningAnimation(false);
        }
    }

    void WaitingForRefineryUpdate()
    {
        // Kolla om refinery försvann
        if (targetRefinery == null || !targetRefinery.gameObject.activeInHierarchy)
        {
            Debug.LogWarning($"{gameObject.name} target refinery became invalid while waiting. Going Idle.", this);
            GoToIdleState(false);
            return;
        }

        // Stå stilla (eller patrullera lite?)
        if (!agent.hasPath && agent.velocity.sqrMagnitude < 0.1f)
        {
            // Kanske backa lite från dockningspunkten om vi är för nära?
        }

        // Kolla med jämna mellanrum om refinery är ledigt
        checkRefineryTimer += Time.deltaTime;
        if (checkRefineryTimer >= checkInterval)
        {
            checkRefineryTimer = 0f;
            // Fråga igen om den INTE är upptagen just nu
            if (!targetRefinery.isCurrentlyUnloading)
            {
                bool couldStart = targetRefinery.RequestUnload(this);
                if (couldStart)
                {
                    // Lyckades! Gå till dockningspunkten och Depositing state
                    currentState = HarvesterState.Depositing; // Gå till passiv state
                    agent.stoppingDistance = 1.5f; // Närma oss igen
                    agent.SetDestination(targetRefinery.dockingPoint.position); // Gå fram till dockan
                    Debug.Log($"{gameObject.name} finished waiting and started deposit at {targetRefinery.name}");
                    SetMiningAnimation(false);
                }
                // Om det inte lyckades nu heller, fortsätt vänta...
            }
        }
    }

} // End of class