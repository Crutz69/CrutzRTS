// Assets/RTSGAME/Scripts/World/HarvestableCrystal.cs
using UnityEngine;
using Mirror;

namespace RTSGAME
{
    // Flytta enum till en egen fil eller gemensam plats?
    // public enum CrystalType { None, Green, Blue, Red }

    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(Collider))]
    public class HarvestableCrystal : NetworkBehaviour
    {
        [Header("Crystal Properties")]
        [Tooltip("Vilken typ av kristall detta är.")]
        [SyncVar(hook = nameof(OnTypeChanged))]
        public CrystalType crystalType = CrystalType.Green;

        [Tooltip("Hur mycket resurs denna kristall ger.")]
        [SyncVar]
        public int valuePerUnit = 100;

        [SyncVar(hook = nameof(OnTargetedChanged))]
        private uint targetedByNetId = 0; // Håll denna privat

        // Lägg till denna publika property för att LÄSA värdet utifrån:
        public uint TargetedByNetId => targetedByNetId;
        // Detta är en förkortad syntax för: public uint TargetedByNetId { get { return targetedByNetId; } }

        // Property för att enkelt kolla om den är upptagen finns redan:
        public bool IsTargeted => targetedByNetId != 0;

        // --- Mirror Callbacks ---

        public override void OnStartServer()
        {
            base.OnStartServer();
            targetedByNetId = 0; // Säkerställ att den är ledig vid start
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Klienten reagerar på initiala SyncVar-värden via hooks
            OnTypeChanged(crystalType, crystalType);
            OnTargetedChanged(0, targetedByNetId);
        }

        // --- SyncVar Hooks (Client-side) ---

        void OnTypeChanged(CrystalType oldType, CrystalType newType)
        {
            UpdateVisuals();
        }

        void OnTargetedChanged(uint oldTargetNetId, uint newTargetNetId)
        {
            UpdateVisuals();
            // Debug.Log($"Crystal {netId} targeted status changed to: {(newTargetNetId != 0)} by {newTargetNetId}");
        }

        // Uppdaterar utseende baserat på typ och om den är reserverad
        void UpdateVisuals()
        {
            Renderer rend = GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                Color baseColor = GetGizmoColor(crystalType);
                // Gör den lite mörkare/gråare om targetad för visuell feedback
                rend.material.color = IsTargeted ? Color.Lerp(baseColor, Color.grey, 0.5f) : baseColor;
            }
        }

        // --- Commands (Called by Harvester Client, Run on Server) ---

        // En Harvester anropar detta via sitt NetworkPlayer för att försöka reservera
        [Command(requiresAuthority = false)] // Vem som helst kan försöka reservera (servern validerar)
        public void Cmd_RequestReserve(NetworkIdentity requestingHarvesterIdentity)
        {
            if (requestingHarvesterIdentity == null) return;
            Server_TryReserve(requestingHarvesterIdentity.netId);
            // Servern returnerar inget direkt, Harvestern får kolla SyncVar eller få RPC?
            // Eller så får Harvester-kommandot som anropade detta vänta på Server_TryReserve.
            // Låt oss hålla det enkelt: Harvester anropar, servern sätter SyncVar.
        }

        // En Harvester anropar detta via sitt NetworkPlayer för att släppa
        [Command(requiresAuthority = false)] // Vem som helst kan försöka släppa (servern validerar)
        public void Cmd_RequestRelease(NetworkIdentity requestingHarvesterIdentity)
        {
            if (requestingHarvesterIdentity == null) return;
            Server_TryRelease(requestingHarvesterIdentity.netId);
        }


        // --- Server-Side Reservation Logic ---

        [Server]
        public bool Server_IsAvailable()
        {
            return targetedByNetId == 0;
        }

        // Försöker reservera. Returnerar true om det lyckades.
        [Server]
        public bool Server_TryReserve(uint harvesterNetId)
        {
            if (targetedByNetId == 0 && harvesterNetId != 0)
            {
                targetedByNetId = harvesterNetId; // Reservation lyckades! SyncVar uppdaterar klienter.
                // Debug.Log($"Crystal {netId} RESERVED by Harvester {harvesterNetId}");
                return true;
            }
            // Om redan reserverad av SAMMA harvester, är det ok? Ja.
            if (targetedByNetId == harvesterNetId) return true;

            // Annars, redan upptagen av någon annan.
            // Debug.LogWarning($"Crystal {netId} FAILED TO RESERVE for {harvesterNetId}. Already targeted by: {targetedByNetId}");
            return false;
        }

        // Försöker släppa. Returnerar true om det lyckades.
        [Server]
        public bool Server_TryRelease(uint harvesterNetId)
        {
            // Bara den som reserverade får släppa
            if (targetedByNetId == harvesterNetId && harvesterNetId != 0)
            {
                targetedByNetId = 0; // Gör tillgänglig igen. SyncVar uppdaterar klienter.
                // Debug.Log($"Crystal {netId} RELEASED by Harvester {harvesterNetId}");
                return true;
            }
            return false;
        }

        // Anropas av Harvester server-side när den samlat klart
        [Server]
        public void Server_HarvestComplete()
        {
            Debug.Log($"Crystal {netId} harvested, destroying.");
            // Förstör objektet på nätverket
            NetworkServer.Destroy(gameObject);
        }


        // --- Gizmos & Helpers ---
        void OnDrawGizmos()
        {
            Gizmos.color = GetGizmoColor(crystalType);
            if (IsTargeted) { Gizmos.color = Color.yellow; }
            Collider col = GetComponent<Collider>();
            if (col != null) { Gizmos.DrawSphere(col.bounds.center, col.bounds.extents.magnitude * 0.3f); if (IsTargeted) Gizmos.DrawWireSphere(col.bounds.center, col.bounds.extents.magnitude * 0.35f); }
            else { Gizmos.DrawSphere(transform.position, 0.3f); if (IsTargeted) Gizmos.DrawWireSphere(transform.position, 0.35f); }
        }
        Color GetGizmoColor(CrystalType ct) { /* ... som tidigare ... */ return Color.white; }
        public class ReadOnlyAttribute : PropertyAttribute { }
#if UNITY_EDITOR
        [UnityEditor.CustomPropertyDrawer(typeof(ReadOnlyAttribute))] public class ReadOnlyDrawer : UnityEditor.PropertyDrawer { public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label) { GUI.enabled = false; UnityEditor.EditorGUI.PropertyField(position, property, label, true); GUI.enabled = true; } public override float GetPropertyHeight(UnityEditor.SerializedProperty property, GUIContent label) { return UnityEditor.EditorGUI.GetPropertyHeight(property, label, true); } }
#endif
    }
}