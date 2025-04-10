// Assets/RTSGAME/Scripts/World/SpawnPointMarker.cs
using UnityEngine;

namespace RTSGAME // <--- Viktigt med samma namespace!
{
    public class SpawnPointMarker : MonoBehaviour
    {
        [Tooltip("Optional: Assign a player/team number this spawn is intended for (e.g., for fixed spawns). 0 = Any.")]
        public int assignedPlayerOrTeamIndex = 0;

        // Rita ut en Gizmo i Scene-vyn för att lätt se den
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(transform.position, 1f);
            Gizmos.DrawWireSphere(transform.position, 1.1f);
#if UNITY_EDITOR
            string label = assignedPlayerOrTeamIndex > 0 ? $"P/T: {assignedPlayerOrTeamIndex}" : "Spawn";
            UnityEditor.Handles.Label(transform.position + Vector3.up * 1.5f, label);
#endif
        }

        // Valfritt: Gör så att ev. visuella modeller försvinner vid start
        void Start()
        {
            Renderer rend = GetComponentInChildren<Renderer>();
            if (rend != null) { rend.enabled = false; }
        }
    }
}