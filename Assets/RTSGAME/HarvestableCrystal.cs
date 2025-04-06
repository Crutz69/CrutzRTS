// Fil: HarvestableCrystal.cs
using UnityEngine;

// Enum kan ligga här eller i en egen fil
// Se till att du bara har EN definition av denna enum i ditt projekt
public enum CrystalType { None, Green, Blue, Red }

public class HarvestableCrystal : MonoBehaviour
{
    [Header("Crystal Properties")]
    [Tooltip("Vilken typ av kristall detta är.")]
    public CrystalType type = CrystalType.Green;

    [Tooltip("Hur mycket resurs denna kristall ger.")]
    public int value = 100; // Sätts per prefab eller dynamiskt

    // --- Variabler för Reservation (Alternativ 1) ---
    [Header("State (Internal)")]
    [Tooltip("Är denna kristall just nu måltavla för en Harvester?")]
    [ReadOnly] // Gör den skrivskyddad i inspektorn för tydlighet
    public bool isTargeted = false;

    [Tooltip("Vilken Harvester siktar på denna kristall?")]
    [ReadOnly]
    public HarvesterUnit targetedBy = null;
    // --- Slut på variabler för Reservation ---


    // Valfritt: Referens till fältet som äger den
    // public ResourceFieldController ownerField = null;

    // --- Metoder för Reservation (Alternativ 1) ---

    /// <summary>
    /// Försöker reservera denna kristall för en specifik Harvester.
    /// </summary>
    /// <param name="harvester">Harvestern som försöker reservera.</param>
    /// <returns>True om reservationen lyckades, false om den redan var reserverad.</returns>
    public bool Reserve(HarvesterUnit harvester)
    {
        if (!isTargeted && harvester != null)
        {
            isTargeted = true;
            targetedBy = harvester;
            // Debug.Log($"Crystal {gameObject.name} RESERVED by {harvester.name}");
            return true; // Reservation lyckades
        }
        // Debug.LogWarning($"Crystal {gameObject.name} FAILED TO RESERVE for {harvester?.name}. Already targeted by: {targetedBy?.name}");
        return false; // Redan reserverad eller ogiltig harvester
    }

    /// <summary>
    /// Släpper reservationen för denna kristall, men bara om den angivna Harvestern är den som har reserverat den.
    /// </summary>
    /// <param name="harvester">Harvestern som försöker släppa reservationen.</param>
    public void Release(HarvesterUnit harvester)
    {
        // Bara den som reserverade får släppa (viktigt!)
        if (targetedBy == harvester)
        {
            isTargeted = false;
            targetedBy = null;
            // Debug.Log($"Crystal {gameObject.name} RELEASED by {harvester.name}");
        }
        // else { Debug.LogWarning($"Crystal {gameObject.name} release attempt by {harvester?.name} IGNORED. Current target: {targetedBy?.name}"); }
    }

    // --- Slut på metoder för Reservation ---


    // Gizmo-kod (behålls som den är)
    void OnDrawGizmos()
    {
        Gizmos.color = GetGizmoColor(type);
        // Visa om den är targetad? (Valfritt extra)
        if (isTargeted)
        {
            Gizmos.color = Color.yellow; // Gör den gul om den är target
        }

        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.DrawSphere(col.bounds.center, col.bounds.extents.magnitude * 0.3f);
            if (isTargeted) Gizmos.DrawWireSphere(col.bounds.center, col.bounds.extents.magnitude * 0.35f); // Extra ring
        }
        else
        {
            Gizmos.DrawSphere(transform.position, 0.3f);
            if (isTargeted) Gizmos.DrawWireSphere(transform.position, 0.35f); // Extra ring
        }
    }

    Color GetGizmoColor(CrystalType crystalType)
    {
        switch (crystalType)
        {
            case CrystalType.Green: return Color.green;
            case CrystalType.Blue: return Color.blue;
            case CrystalType.Red: return Color.red;
            default: return Color.white;
        }
    }

    // Helper för ReadOnly attributet (lägg till detta om du inte redan har det i ett annat script)
    // Om du redan har detta någonstans, kan du ta bort det härifrån.
    public class ReadOnlyAttribute : PropertyAttribute { }

#if UNITY_EDITOR
    [UnityEditor.CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
    public class ReadOnlyDrawer : UnityEditor.PropertyDrawer
    {
        public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label)
        {
            GUI.enabled = false; // Gör fältet grått
            UnityEditor.EditorGUI.PropertyField(position, property, label, true);
            GUI.enabled = true; // Återställ
        }
        public override float GetPropertyHeight(UnityEditor.SerializedProperty property, GUIContent label)
        {
            return UnityEditor.EditorGUI.GetPropertyHeight(property, label, true);
        }
    }
#endif
}