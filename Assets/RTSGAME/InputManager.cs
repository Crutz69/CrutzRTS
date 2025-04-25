// Assets/RTSGAME/Scripts/Managers/InputManager.cs
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem; // Om du använder nya Input System
using System.Collections.Generic; // För List<>
using System.Linq; // Kan behövas för SelectionManager-logik

namespace RTSGAME
{
    public class InputManager : MonoBehaviour
    {
        public static InputManager Instance { get; private set; }

        private NetworkPlayer localPlayer; // Referens till den lokala spelarens script
        private Camera mainCamera;

        // TODO: Lägg till variabler för att hålla koll på "modes" (t.ex. placera byggnad)
        // public bool IsPlacingBuilding { get; set; } = false; // Exempel

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject); // Singleton bör ofta överleva scenbyten
            }
            else
            {
                Destroy(gameObject);
                return; // Avbryt om en instans redan finns
            }

            mainCamera = Camera.main; // Hitta huvudkameran
            if (mainCamera == null)
            {
                Debug.LogError("InputManager: Could not find main camera!");
            }
        }

        // Denna metod anropas av NetworkPlayer.OnStartLocalPlayer
        public void AssignLocalPlayer(NetworkPlayer player)
        {
            localPlayer = player;
            Debug.Log($"InputManager assigned to local player: {player?.playerName ?? "NULL"}");
        }

        void Update()
        {
            // Agera bara för den lokala spelaren som har blivit korrekt tilldelad
            if (localPlayer == null || !localPlayer.isLocalPlayer || mainCamera == null) return;

            HandleCameraMovement(); // Hantera kamerakontroll
            HandleMouseInput();   // Hantera musklick för val/handlingar
            HandleKeyboardInput(); // Hantera kortkommandon
        }

        void HandleCameraMovement()
        {
            // TODO: Implementera logik för att panorera/zooma/rotera kameran
            // baserat på input (mus vid kanten, WASD, Q/E, scrollhjul etc.)
        }

        void HandleMouseInput()
        {
            // Vänsterklick
            if (Mouse.current.leftButton.wasPressedThisFrame) // Nytt Input System exempel
            {
                HandleLeftClick();
            }

            // Högerklick
            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                HandleRightClick();
            }

            // Hantera box selection (om vänsterknapp hålls nere)
            // TODO: Implementera box selection logic här eller i SelectionManager
        }

        void HandleLeftClick()
        {
            // TODO: Är vi över ett UI-element? Ignorera klick i så fall.
            // (Använd EventSystem.current.IsPointerOverGameObject())
            // if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) { return; }

            // TODO: Är vi i "placera byggnad"-läge?
            // if (IsPlacingBuilding) { ProcessPlacement(); return; }

            // Annars, försök selektera
            Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                // Anropa SelectionManager för att hantera valet
                bool additive = Keyboard.current.shiftKey.isPressed; // Shift-klick för att lägga till?
                SelectionManager.Instance?.HandleClickSelection(hit.collider.gameObject, additive);
            }
            else
            {
                // Klickade på tom yta - avmarkera? (Bara om inte shift hålls nere)
                bool additive = Keyboard.current.shiftKey.isPressed;
                if (!additive) SelectionManager.Instance?.ClearSelection();
            }
        }

        void HandleRightClick()
        {
            // TODO: Är vi över ett UI-element? Ignorera.
            // if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) { return; }

            // Se till att vi har en lokal spelare innan vi försöker skicka kommandon
            if (localPlayer == null) return;

            Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                // Försök identifiera vad som klickades på
                if (hit.collider.TryGetComponent<Unit>(out Unit targetUnit))
                {
                    // Klickade på en enhet. Försök attackera med valda enheter.
                    if (targetUnit.TryGetComponent<NetworkIdentity>(out var targetIdentity))
                    {
                        // *** FIX: Anropa CmdAttackTarget istället för ProcessAttackRequest ***

                        // 1. Hämta NetIDs för de för närvarande valda enheterna från SelectionManager
                        //    OBS: Du behöver implementera GetSelectedUnitNetIds() i SelectionManager!
                        List<uint> selectedUnitNetIds = SelectionManager.Instance?.GetSelectedUnitNetIds();

                        // 2. Kontrollera att det faktiskt finns valda enheter som kan attackera
                        if (selectedUnitNetIds != null && selectedUnitNetIds.Count > 0)
                        {
                            // 3. Skicka det korrekta kommandot till NetworkPlayer
                            localPlayer.CmdAttackTarget(selectedUnitNetIds, targetIdentity);
                            Debug.Log($"Sending CmdAttackTarget from {selectedUnitNetIds.Count} units to target {targetIdentity.netId}");
                        }
                        else
                        {
                            Debug.Log("Right-clicked target unit, but no units selected to issue attack command.");
                            // Kanske spela ett ljud eller ge visuell feedback?
                            // Alternativt: Om inga är valda, kanske flytta enskild vald enhet om bara en är vald? Mer komplex logik.
                        }
                    }
                }
                else if (hit.collider.TryGetComponent<Building>(out Building targetBuilding))
                {
                    // Klickade på byggnad
                    if (targetBuilding.TryGetComponent<NetworkIdentity>(out var targetIdentity))
                    {
                        // TODO: Beroende på vad som är valt (enheter, arbetare?) och vad byggnaden är (egen, fiende?),
                        // ska detta anropa olika kommandon:
                        // - CmdSetRallyPoint(targetIdentity, hit.point) om en produktionsbyggnad är vald.
                        // - CmdStartCapture(workerNetId, targetIdentity) om en arbetare är vald och byggnaden är neutral/fiende.
                        // - CmdRepairTarget(workerNetId, targetIdentity) om en arbetare är vald och byggnaden är skadad och egen/allierad.
                        // - CmdAttackTarget(...) om militära enheter är valda och byggnaden är fiende.

                        // ** TILLFÄLLIG PLATT KOD - ERSÄTT MED KORREKT LOGIK **
                        // Detta är bara en placeholder och kommer ge fel:
                        // localPlayer.ProcessSetRallyPointRequest(hit.point); // <-- MÅSTE ÄNDRAS till CmdSetRallyPoint eller liknande!
                        Debug.LogWarning("Right-click on building needs proper logic to determine action (Rally, Capture, Attack, Repair).");

                        // Exempel på hur man skulle kunna sätta rally point om en byggnad är vald:
                        // NetworkIdentity selectedBuilding = SelectionManager.Instance?.GetPrimarySelectedBuildingIdentity();
                        // if (selectedBuilding != null) {
                        //     localPlayer.CmdSetRallyPoint(selectedBuilding, hit.point);
                        // } else {
                        //     // Kanske attackera byggnaden om militära enheter är valda?
                        //     List<uint> selectedUnitNetIds = SelectionManager.Instance?.GetSelectedUnitNetIds();
                        //     if (selectedUnitNetIds != null && selectedUnitNetIds.Count > 0 && PlayerManager.Instance.IsEnemy(targetBuilding.ownerNetId, localPlayer.netId) ) // Behöver IsEnemy check
                        //     {
                        //          localPlayer.CmdAttackTarget(selectedUnitNetIds, targetIdentity);
                        //     }
                        // }
                    }
                }
                else // Klickade på terräng
                {
                    // Flytta valda enheter till positionen

                    // ** FIX BEHÖVS HÄR OCKSÅ **
                    // Hämta valda enheter
                    List<uint> selectedUnitNetIds = SelectionManager.Instance?.GetSelectedUnitNetIds();

                    if (selectedUnitNetIds != null && selectedUnitNetIds.Count > 0)
                    {
                        // Anropa rätt Command
                        localPlayer.CmdMoveUnits(selectedUnitNetIds, hit.point);
                        Debug.Log($"Sending CmdMoveUnits for {selectedUnitNetIds.Count} units to {hit.point}");
                    }
                    else
                    {
                        Debug.Log("Right-clicked ground, but no units selected to move.");
                    }
                    // Ersätt detta:
                    // localPlayer.ProcessMoveRequest(hit.point); // <-- MÅSTE ÄNDRAS till CmdMoveUnits!
                }
            }
            else // Klickade utanför spelplanen?
            {
                Debug.Log("Right-click raycast did not hit anything.");
                // Kanske avbryta något läge, t.ex. attack-move?
            }
        }

        void HandleKeyboardInput()
        {
            // TODO: Implementera kortkommandon (t.ex. 'B' för byggmeny, 'A' för attack-move, siffror för kontrollgrupper)
            // Exempel:
            // if (Keyboard.current.bKey.wasPressedThisFrame) { UIManager.Instance?.ToggleBuildMenu(); }
            // if (Keyboard.current.aKey.wasPressedThisFrame) { SetAttackMoveMode(); }
            // if (Keyboard.current.sKey.wasPressedThisFrame) { IssueStopCommandToSelectedUnits(); }
            // if (Keyboard.current.escapeKey.wasPressedThisFrame) { HandleEscapeKey(); } // Avbryt lägen, avmarkera etc.
        }

        // TODO: Hjälpmetoder som IsPointerOverUIObject(), SetAttackMoveMode(), IssueStopCommandToSelectedUnits(), HandleEscapeKey() etc.

    } // End class InputManager
} // End namespace RTSGAME