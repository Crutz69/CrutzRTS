using UnityEngine;
using System.Collections; // Behövs för Coroutine

public class RefineryBuilding : MonoBehaviour
{
    [Tooltip("Hur lång tid i sekunder tar avlastningsanimationen?")]
    public float unloadDuration = 3.0f;
    [Tooltip("Är enheten för närvarande upptagen med att lasta av?")]
    public bool isCurrentlyUnloading = false;
    [Tooltip("Valfri: Specifik punkt där harvestern ska docka (t.ex. ett child GameObject).")]
    public Transform dockingPoint; // Golemen ska sikta på denna

    // Referens till Animator för kranen (läggs till senare)
    // private Animator craneAnimator;

    void Awake()
    {
        // craneAnimator = GetComponentInChildren<Animator>(); // Hitta animatorn senare
        if (dockingPoint == null)
        {
            Debug.LogWarning("RefineryBuilding har ingen dockingPoint satt, harvester kommer sikta på byggnadens centrum.", this);
            dockingPoint = this.transform; // Använd byggnadens position som fallback
        }
    }

    // Metod som Harvester anropar för att försöka starta avlastning
    public bool RequestUnload(HarvesterUnit harvester)
    {
        if (isCurrentlyUnloading)
        {
            return false; // Upptaget
        }
        else
        {
            isCurrentlyUnloading = true;
            Debug.Log($"Refinery '{gameObject.name}': Starting unload for '{harvester.gameObject.name}'.");
            // Starta kran-animationen här...
            // craneAnimator?.SetTrigger("StartUnloading");

            // Starta en timer/Coroutine för att simulera processen
            StartCoroutine(UnloadProcess(harvester));
            return true; // Processen startad
        }
    }

    // Simulerar avlastningstiden och meddelar harvester när klar
    private IEnumerator UnloadProcess(HarvesterUnit harvester)
    {
        // Vänta tills animationen/processen är klar
        yield return new WaitForSeconds(unloadDuration);

        // Kolla om harvestern fortfarande existerar (den kanske dog/fick ny order?)
        if (harvester != null && harvester.currentState == HarvesterUnit.HarvesterState.Depositing) // Kolla state!
        {
            harvester.CompleteDeposit(); // Meddela harvestern att den är klar
        }
        else
        {
            Debug.LogWarning($"Refinery '{gameObject.name}': Harvester '{harvester?.gameObject.name ?? "null"}' was not ready or in Depositing state when unload finished.", this);
            // Återställ ändå, om harvestern försvann
        }

        Debug.Log($"Refinery '{gameObject.name}': Unload complete.");
        isCurrentlyUnloading = false; // Bli ledig igen
    }

    // Valfritt: Visa Gizmo för dockningspunkt
    void OnDrawGizmosSelected()
    {
        if (dockingPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(dockingPoint.position, 0.5f);
            Gizmos.DrawLine(transform.position, dockingPoint.position);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(dockingPoint.position + Vector3.up * 0.5f, "Docking Point");
#endif
        }
        Gizmos.color = isCurrentlyUnloading ? Color.red : Color.green;
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size * 1.1f); // Rita en ruta runt
        }
    }
}