using UnityEngine;
using Mirror;

namespace RTSGAME
{
    // Lägg till detta på dina enheter som kan attackera
    [RequireComponent(typeof(Unit))] // Kräver Unit-komponenten
    public class UnitCombat : NetworkBehaviour
    {
        // *** LÄGG TILL KOD HÄR SENARE ***
        // (T.ex. attackRange, attackDamage, attackCooldown,
        //  metoden Server_SetAttackTarget etc.)

        // Lägg till åtminstone detta för att felet ska försvinna:
        [Server]
        public void Server_SetAttackTarget(NetworkIdentity newTarget)
        {
            Debug.LogWarning($"[Server] Unit {netId} received attack order for {newTarget?.netId ?? 0}, but UnitCombat logic is not fully implemented yet!");
            // TODO: Implementera logik för att flytta inom räckvidd och attackera.
        }

        [Server]
        public void Server_ClearTarget()
        {
            Debug.LogWarning($"[Server] Unit {netId} clearing attack target - UnitCombat logic not fully implemented.");
            // TODO: Implementera logik för att stoppa attack.
        }
    }
}