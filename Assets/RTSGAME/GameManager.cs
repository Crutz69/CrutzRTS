// Fil: GameManager.cs
using UnityEngine;
using Mirror;

namespace RTSGAME
{
    // Flytta GameMode till Enums.cs om du inte redan gjort det
    // public enum GameMode { Teams, FFA }

    public class GameManager : NetworkBehaviour
    {
        public static GameManager Instance { get; private set; }

        [SyncVar]
        public GameMode CurrentGameMode = GameMode.FFA; // Default? Eller sätt från lobby

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (Instance != null)
            {
                Debug.LogError("Duplicate GameManager!", gameObject);
                Destroy(gameObject);
                return;
            }
            Instance = this;
            // TODO: Sätt CurrentGameMode baserat på match settings
            Debug.Log($"[Server] GameManager Started. Mode: {CurrentGameMode}");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}