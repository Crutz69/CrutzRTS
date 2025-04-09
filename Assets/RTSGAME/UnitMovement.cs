// Assets/RTSGAME/Scripts/Units/UnitMovement.cs
using Mirror;
using UnityEngine;
using UnityEngine.AI; // För NavMeshAgent

namespace RTSGAME
{
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Unit))] // Kräver huvud-Unit scriptet
    public class UnitMovement : NetworkBehaviour
    {
        [Header("Components")]
        [SerializeField] private NavMeshAgent agent;
        [SerializeField] private Unit unit; // Referens tillbaka till huvudscriptet

        [Header("Settings")]
        [SerializeField] private float defaultStoppingDistance = 0.5f;

        // --- Unity & Mirror Callbacks ---

        private void Awake()
        {
            // Hitta komponenter
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            if (unit == null) unit = GetComponent<Unit>();

            // Viktigt: Konfigurera NavMeshAgent
            // Stäng av automatisk uppdatering av position/rotation om NetworkTransform sköter det.
            // Detta kan variera beroende på NetworkTransform-komponentens inställningar!
            // agent.updatePosition = false; // Ofta bäst att låta NetworkTransform hantera detta
            // agent.updateRotation = true; // Rotation kan ofta hanteras av agenten eller NetworkTransform
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            // Servern äger agentens logik
            agent.enabled = true;
            agent.stoppingDistance = defaultStoppingDistance;
            // Se till att agentens position matchar transform direkt vid start
            if (!agent.Warp(transform.position))
            {
                Debug.LogWarning($"Failed to warp NavMeshAgent for {unit?.UnitDisplayName ?? "Unit"} ({netId}) on server start.");
            }
        }

        public override void OnStartAuthority()
        {
            // Om du använder client authority för rörelse (mindre vanligt i RTS)
            // skulle du aktivera agenten här istället. För server authority
            // behöver klienten oftast inte ha en aktiv agent.
            // agent.enabled = hasAuthority;
        }

        public override void OnStopAuthority()
        {
            // agent.enabled = false;
        }


        // --- Server-Side Movement Control ---

        /// <summary>
        /// Sets the destination for the NavMeshAgent. Called only on the server.
        /// </summary>
        [Server]
        public void Server_SetDestination(Vector3 destination)
        {
            if (!agent.enabled) agent.enabled = true; // Aktivera om den var avstängd
            agent.stoppingDistance = defaultStoppingDistance; // Sätt standard stoppavstånd
            agent.isStopped = false; // Börja röra på dig
            if (agent.SetDestination(destination))
            {
                // Debug.Log($"Unit {unit?.UnitDisplayName ?? ""} ({netId}) moving to {destination}");
            }
            else
            {
                Debug.LogWarning($"Unit {unit?.UnitDisplayName ?? ""} ({netId}) failed to set destination {destination}");
                // Gå till Idle direkt om destinationen är ogiltig?
                // unit?.Server_GoToIdleState(); // Kräver metod på Unit
            }
        }

        /// <summary>
        /// Sets the destination for following a target (adjusts stopping distance). Called only on the server.
        /// </summary>
        [Server]
        public void Server_SetFollowTarget(Transform target, float stoppingDist)
        {
            if (!agent.enabled) agent.enabled = true;
            agent.stoppingDistance = stoppingDist; // Anpassa stoppavstånd för attack/interaktion
            agent.isStopped = false;
            if (!agent.SetDestination(target.position))
            { // Gå mot målets nuvarande position
                Debug.LogWarning($"Unit {unit?.UnitDisplayName ?? ""} ({netId}) failed to set follow target {target.name}");
                // unit?.Server_GoToIdleState();
            }
            // OBS: För att följa ett rörligt mål behöver destinationen uppdateras regelbundet!
            // Detta görs ofta i UnitCombat eller en AI-komponent.
        }


        /// <summary>
        /// Stops the NavMeshAgent's current path. Called only on the server.
        /// </summary>
        [Server]
        public void Server_StopMovement()
        {
            if (agent.enabled && agent.hasPath) // Kolla om agenten är aktiv och har en path
            {
                agent.isStopped = true; // Stoppa agenten
                agent.ResetPath(); // Rensa nuvarande path
                                   // Debug.Log($"Unit {unit?.UnitDisplayName ?? ""} ({netId}) stopped movement.");
            }
            // Se till att NetworkTransform slutar försöka synka om den använder velocity
            var nt = GetComponent<NetworkTransformBase>();
            if (nt != null && nt.syncMode == SyncMode.Observers)
            { // Exempel för Mirror NT
              // Kan behöva sätta velocity till noll manuellt om NT inte gör det?
            }
        }


        // --- Server-Side Update (Check for Arrival) ---

        [ServerCallback] // Körs bara på servern
        void Update()
        {
            // Om vi inte rör oss (isStopped) eller inte har en path, behöver vi inte kolla.
            if (!agent.enabled || agent.isStopped || !agent.hasPath) return;

            // Kolla om vi har nått destinationen
            // pathPending: Kollar om agenten fortfarande beräknar en path
            // remainingDistance: Avstånd kvar längs den nuvarande pathen
            // stoppingDistance: Hur nära målet agenten ska stanna
            // velocity: Kollar om agenten faktiskt har stannat (ibland stannar remainingDistance precis ovanför stoppingDistance)
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
            {
                // Lägg till en liten hastighetskoll för att säkerställa att den verkligen stannat
                if (agent.velocity.sqrMagnitude < 0.1f * 0.1f) // Jämför kvadrat för prestanda
                {
                    // Vi har nått målet!
                    Server_StopMovement(); // Stanna agenten korrekt

                    // Meddela huvud-Unit scriptet att vi har anlänt
                    unit?.OnMovementArrival(); // Anropa metoden på Unit-scriptet
                }
            }
            // Uppdatera rotation? NetworkTransform kanske sköter detta.
            // Om inte, kan du rotera här:
            // if (agent.velocity.sqrMagnitude > 0.1f) {
            //     Quaternion lookRotation = Quaternion.LookRotation(agent.velocity.normalized);
            //     transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * agent.angularSpeed);
            // }
        }
    }
}