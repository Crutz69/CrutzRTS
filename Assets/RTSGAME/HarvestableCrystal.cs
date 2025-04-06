using UnityEngine;

// Enum kan ligga här eller i en egen fil om fler scripts behöver den.
// Om du redan har den i ResourceNode.cs kan du ta bort den härifrån.
public enum CrystalType { None, Green, Blue, Red }

public class HarvestableCrystal : MonoBehaviour
{
    [Header("Crystal Properties")]
    [Tooltip("Vilken typ av kristall detta är (sätts i Inspektorn på prefaben).")]
    public CrystalType type = CrystalType.Green;

    [Tooltip("Hur mycket resurs denna kristall ger när den lämnas in (sätts i Inspektorn på prefaben).")]
    public int value = 100; // Exempelvärde för Grön

    // Valfritt: En referens tillbaka till fältet som äger den
    // Detta kan sättas av ResourceFieldController när den spawnar kristallen,
    // men behövs inte för grundfunktionaliteten just nu.
    // public ResourceFieldController ownerField = null;

    // Detta script behöver egentligen ingen egen logik i Update() eller Start() just nu.
    // Det fungerar mest som en databärare och en markör (tillsammans med sin Collider).
    // Fysik och interaktion hanteras av Harvester och ResourceFieldController.

    // Valfritt: Gizmo för att se kristallen lättare i editorn
    void OnDrawGizmos()
    {
        Gizmos.color = GetGizmoColor(type);
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            // Rita en lite mindre sfär inuti collidern för att visa typen
            Gizmos.DrawSphere(col.bounds.center, col.bounds.extents.magnitude * 0.3f);
        }
        else
        {
            Gizmos.DrawSphere(transform.position, 0.3f);
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
}