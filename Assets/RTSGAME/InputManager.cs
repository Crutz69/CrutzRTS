// Assets/RTSGAME/Scripts/Managers/InputManager.cs
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem; // Om du använder nya Input System

namespace RTSGAME
{
    public class InputManager : MonoBehaviour
    {
        public static InputManager Instance { get; private set; }

        private NetworkPlayer localPlayer; // Referens till den lokala spelarens script
        private Camera mainCamera;

        // TODO: Lägg till variabler för att hålla koll på "modes" (t.ex. placera byggnad)

        void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            mainCamera = Camera.main; // Hitta huvudkameran
        }

        // Denna metod anropas av NetworkPlayer.OnStartLocalPlayer
        public void AssignLocalPlayer(NetworkPlayer player)
        {
            localPlayer = player;
            Debug.Log("InputManager assigned to local player.");
        }

        void Update()
        {
            if (localPlayer == null || !localPlayer.isLocalPlayer) return; // Agera bara för den lokala spelaren

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
            // if (Input.GetMouseButtonDown(0)) // Gammalt Input System
            {
                HandleLeftClick();
            }

            // Högerklick
            if (Mouse.current.rightButton.wasPressedThisFrame)
            // if (Input.GetMouseButtonDown(1))
            {
                HandleRightClick();
            }

            // Hantera box selection (om vänsterknapp hålls nere)
            // TODO: Implementera box selection logic här eller i SelectionManager
        }

        void HandleLeftClick()
        {
            // TODO: Är vi i "placera byggnad"-läge?
            // if (IsInPlacementMode()) { ProcessPlacement(); return; }

            // TODO: Är vi över ett UI-element? Ignorera klick i så fall.
            // if (IsPointerOverUIObject()) { return; }

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
                // Klickade på tom yta - avmarkera?
                bool additive = Keyboard.current.shiftKey.isPressed;
                if (!additive) SelectionManager.Instance?.ClearSelection();
            }
        }

        void HandleRightClick()
        {
            // TODO: Är vi över ett UI-element? Ignorera.
            // if (IsPointerOverUIObject()) { return; }

            Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                // Försök identifiera vad som klickades på
                if (hit.collider.TryGetComponent<Unit>(out Unit targetUnit))
                {
                    // Klickade på en enhet - är den fiende? Attackera! Annars kanske följ/reparera?
                    // TODO: Kolla relation via PlayerManager/TeamManager
                    // bool isEnemy = CheckIfEnemy(targetUnit);
                    // if (isEnemy) localPlayer.ProcessAttackRequest(targetUnit.netIdentity);
                    // else { /* Följ/Reparera? */ }

                    // För nu: Skicka Attack-förfrågan (servern validerar)
                    if (targetUnit.TryGetComponent<NetworkIdentity>(out var targetIdentity))
                    {
                        localPlayer.ProcessAttackRequest(targetIdentity);
                    }

                }
                else if (hit.collider.TryGetComponent<Building>(out Building targetBuilding))
                {
                    // Klickade på byggnad - Sätt Rally Point? Capture? Reparera?
                    if (targetBuilding.TryGetComponent<NetworkIdentity>(out var targetIdentity))
                    {
                        // Om egna byggare valda och målet är skadat/fiende/neutral?
                        // if(IsWorkerSelected() && targetBuilding.ownerNetId != localPlayer.netId) {
                        //     // Antag att vi har en vald arbetare
                        //     NetworkIdentity worker = SelectionManager.Instance.GetFirstSelectedWorkerIdentity();
                        //     localPlayer.ProcessCaptureRequest(worker, targetIdentity);
                        // } else { // Annars, sätt Rally Point om det är en produktionsbyggnad vald
                        localPlayer.ProcessSetRallyPointRequest(hit.point);
                        // }
                    }
                }
                else
                {
                    // Klickade på terräng - Flytta valda enheter
                    localPlayer.ProcessMoveRequest(hit.point);
                }
            }
        }

        void HandleKeyboardInput()
        {
            // TODO: Implementera kortkommandon (t.ex. 'B' för byggmeny, 'A' för attack-move, siffror för kontrollgrupper)
            // Exempel:
            // if (Keyboard.current.bKey.wasPressedThisFrame) { UIManager.Instance.ToggleBuildMenu(); }
        }

        // TODO: Hjälpmetoder som IsPointerOverUIObject(), GetBuildPositionFromMouse() etc.
    }
}