// Assets/RTSGAME/Scripts/Managers/InputManager.cs
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem; // Använder nya Input System
using UnityEngine.EventSystems; // För UI check
using System.Collections.Generic;
using System.Linq;

namespace RTSGAME
{
    public class InputManager : MonoBehaviour
    {
        public static InputManager Instance { get; private set; }

        [Header("References")]
        [SerializeField] private Camera mainCamera;
        // Ta bort om du har separat Camera Controller
        [SerializeField] private Transform cameraTransform;

        [Header("Camera Controls (Basic - Remove if using separate controller)")]
        [SerializeField] private float cameraMoveSpeed = 20f;
        [SerializeField] private float cameraRotateSpeed = 100f;
        [SerializeField] private float cameraZoomSpeed = 5f;
        [SerializeField] private float edgeScrollThreshold = 25f; // Pixlar från kanten
        [SerializeField] private Vector2 cameraHeightMinMax = new Vector2(10f, 80f);
        [SerializeField] private float cameraGroundOffset = 2f; // För att förhindra att kameran går under marken vid rotation

        [Header("Input Settings")]
        [SerializeField] private LayerMask groundLayerMask = 1 << 0; // Antag att marken är på Default layer

        private NetworkPlayer localPlayer; // Referens till den lokala spelarens script
        private SelectionManager selectionManager; // Cache reference

        // Input Modes / States
        private bool isAttackMoveActive = false;
        // public bool IsPlacingBuilding { get; set; } = false; // För byggplacering

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera != null && cameraTransform == null) cameraTransform = mainCamera.transform.parent ?? mainCamera.transform; // Antag att kameran sitter i en rigg

            selectionManager = SelectionManager.Instance; // Hämta referens till SelectionManager
            if (selectionManager == null) Debug.LogError("InputManager: Could not find SelectionManager Instance!");
        }

        public void AssignLocalPlayer(NetworkPlayer player)
        {
            localPlayer = player;
            selectionManager?.SetLocalPlayer(player); // Ge SelectionManager referensen också
            Debug.Log($"InputManager assigned to local player: {player?.playerName ?? "NULL"}");
        }

        public NetworkPlayer GetLocalPlayer() => localPlayer; // Kan behövas av t.ex. SelectionManager

        void Update()
        {
            if (localPlayer == null || !localPlayer.isLocalPlayer || mainCamera == null) return;

            // Avbryt lägen om Escape trycks
            if (Keyboard.current.escapeKey.wasPressedThisFrame) HandleEscapeKey();

            // Hantera input bara om inte ett textfält är aktivt (t.ex. chatt)
            // if (IsInputFieldActive()) return; // Kräver funktion IsInputFieldActive()

            // Kör INTE input-hantering om pekaren är över UI (gäller klick/box, inte kamera/hotkeys?)
            bool pointerOverUI = IsPointerOverUIObject();

            HandleCameraMovementInput(); // Flytta kamera
            HandleMouseInput(pointerOverUI);   // Hantera musklick/box
            HandleKeyboardInput(); // Hantera kortkommandon
        }

        // --- Kamera Kontroll (Enkel) ---
        void HandleCameraMovementInput()
        {
            // Ta bort eller anpassa om du har en separat Camera Controller
            if (cameraTransform == null) return;

            Vector3 moveInput = Vector3.zero;
            Vector2 mousePos = Mouse.current.position.ReadValue();

            // WASD
            if (Keyboard.current.wKey.isPressed) moveInput += cameraTransform.forward;
            if (Keyboard.current.sKey.isPressed) moveInput -= cameraTransform.forward;
            if (Keyboard.current.aKey.isPressed) moveInput -= cameraTransform.right;
            if (Keyboard.current.dKey.isPressed) moveInput += cameraTransform.right;

            // Muskant
            if (mousePos.x <= edgeScrollThreshold && mousePos.x >= 0) moveInput -= cameraTransform.right;
            if (mousePos.x >= Screen.width - edgeScrollThreshold && mousePos.x <= Screen.width) moveInput += cameraTransform.right;
            if (mousePos.y <= edgeScrollThreshold && mousePos.y >= 0) moveInput -= cameraTransform.forward;
            if (mousePos.y >= Screen.height - edgeScrollThreshold && mousePos.y <= Screen.height) moveInput += cameraTransform.forward;

            // Normalisera och applicera rörelse (endast X och Z)
            moveInput.y = 0;
            if (moveInput.sqrMagnitude > 0.1f)
            {
                cameraTransform.position += moveInput.normalized * cameraMoveSpeed * Time.deltaTime;
            }

            // Rotation (Q/E eller Mittenmusknapp?)
            float rotateInput = 0f;
            if (Keyboard.current.qKey.isPressed) rotateInput += 1f;
            if (Keyboard.current.eKey.isPressed) rotateInput -= 1f;
            // if (Mouse.current.middleButton.isPressed) { rotateInput += Mouse.current.delta.ReadValue().x * 0.1f; } // Mittenmus-rotation

            if (Mathf.Abs(rotateInput) > 0.1f)
            {
                cameraTransform.Rotate(Vector3.up, rotateInput * cameraRotateSpeed * Time.deltaTime, Space.World);
            }

            // Zoom (Scrollhjul)
            float scrollInput = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scrollInput) > 0.1f)
            {
                // Flytta kameran framåt/bakåt längs sin lokala Z-axel (eller ändra Y-höjd)
                // Enklast: ändra Y-höjd direkt
                Vector3 pos = cameraTransform.position;
                pos.y -= scrollInput * cameraZoomSpeed * 0.1f; // Skala ner scroll-värdet
                pos.y = Mathf.Clamp(pos.y, cameraHeightMinMax.x, cameraHeightMinMax.y);
                cameraTransform.position = pos;
            }

            // Förhindra att kameran går under marken vid låg vinkel (Raycast neråt)
            if (Physics.Raycast(cameraTransform.position, Vector3.down, out RaycastHit groundHit, cameraHeightMinMax.y * 2, groundLayerMask))
            {
                if (cameraTransform.position.y < groundHit.point.y + cameraGroundOffset)
                {
                    Vector3 clampedPos = cameraTransform.position;
                    clampedPos.y = groundHit.point.y + cameraGroundOffset;
                    cameraTransform.position = clampedPos;
                }
            }
        }


        // --- Mus Input ---
        void HandleMouseInput(bool pointerOverUI)
        {
            // Om vi är i ett speciellt läge, hantera det först
            if (isAttackMoveActive)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame && !pointerOverUI) HandleAttackMoveClick();
                // Högerklick avbryter attack-move?
                if (Mouse.current.rightButton.wasPressedThisFrame) CancelModes();
                return; // Gå inte vidare om i attack-move-läge
            }
            // if (IsPlacingBuilding) { ... HandlePlacementClick(); return; } // För byggplacering

            // Hantera klick och box selection (box hanteras nu i SelectionManager.Update)
            if (!selectionManager.IsDragging) // Om SelectionManager *inte* håller på att dra en ruta
            {
                // Vänsterklick (hanteras endast om INTE över UI och ingen box dras)
                if (Mouse.current.leftButton.wasPressedThisFrame && !pointerOverUI)
                {
                    HandleLeftClick();
                }
                // Högerklick (kan ofta ignorera UI?)
                if (Mouse.current.rightButton.wasPressedThisFrame)
                {
                    HandleRightClick();
                }
            }
        }

        void HandleLeftClick()
        {
            // Denna metod anropas nu bara om vi *inte* drog en box och *inte* klickade på UI

            // Raycast för att se vad vi klickade på
            Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, selectionManager.SelectableLayerMask)) // Använd SM's layer mask
            {
                // Anropa SelectionManager för att hantera valet
                bool additive = Keyboard.current.shiftKey.isPressed;
                selectionManager.HandleClickSelection(hit.collider.gameObject, additive);
            }
            else // Klickade på tom yta
            {
                bool additive = Keyboard.current.shiftKey.isPressed;
                if (!additive) selectionManager.ClearSelection();
            }
        }

        void HandleRightClick()
        {
            if (localPlayer == null || selectionManager == null) return;

            List<uint> selectedUnitNetIds = selectionManager.GetSelectedUnitNetIds();
            NetworkIdentity primarySelectedBuilding = selectionManager.GetPrimarySelectedBuildingIdentity();

            // Om inga enheter OCH ingen byggnad är vald -> gör inget på högerklick? (Eller avmarkera?)
            if (selectedUnitNetIds.Count == 0 && primarySelectedBuilding == null) return;

            Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, ~LayerMask.GetMask("Ignore Raycast", "UI"))) // Träffa allt utom Ignore/UI
            {
                // --- Fall 1: Valda enheter finns ---
                if (selectedUnitNetIds.Count > 0)
                {
                    HandleRightClick_UnitsSelected(selectedUnitNetIds, hit);
                }
                // --- Fall 2: Endast en produktionsbyggnad är vald ---
                else if (primarySelectedBuilding != null)
                {
                    // Kolla om det är en produktionsbyggnad (du behöver ett sätt att identifiera detta!)
                    // Antagande: GetComponent<ProductionStructure>() finns eller en bool på Building
                    // Försök hämta ProductionBuilding-komponenten från det valda byggnadsobjektet
                    ProductionBuilding prodBuilding = primarySelectedBuilding.GetComponent<ProductionBuilding>();
                    // Om komponenten finns (dvs. det ÄR en produktionsbyggnad)
                    if (prodBuilding != null)
                    {
                        // Anropa metoden för att hantera högerklick när en produktionsbyggnad är vald
                        HandleRightClick_ProductionBuildingSelected(primarySelectedBuilding.netId, hit);
                    }
                    // Annars (om det var en vanlig byggnad eller något annat), gör inget specifikt för produktionsbyggnader här
                    // Annars gör inget om en vanlig byggnad är vald?
                }
            }
        }

        void HandleRightClick_UnitsSelected(List<uint> selectedUnitNetIds, RaycastHit hit)
        {
            // Klickade på terräng -> Flytta
            if (((1 << hit.collider.gameObject.layer) & groundLayerMask) != 0) // Klickade på marken
            {
                localPlayer.CmdMoveUnits(selectedUnitNetIds, hit.point);
                // TODO: Spela upp ljud/visa effekt för move command
                return;
            }

            // Klickade på en enhet
            if (hit.collider.TryGetComponent<Unit>(out Unit targetUnit) && hit.collider.TryGetComponent<NetworkIdentity>(out var targetIdentity))
            {
                // Antagande: Metod IsEnemy finns och använder localPlayer.netId
                if (PlayerManager.Instance != null && PlayerManager.Instance.IsEnemy(localPlayer.netId, targetUnit.ownerNetId))
                {
                    localPlayer.CmdAttackTarget(selectedUnitNetIds, targetIdentity);
                    // TODO: Spela upp attack-ljud/effekt
                }
                else // Egen, allierad eller neutral enhet
                {
                    // TODO: Följ? Reparera om target är byggnad/mekanisk och selected är worker? Heal om selected är medic?
                    // För nu: Flytta bara till nära enheten
                    localPlayer.CmdMoveUnits(selectedUnitNetIds, hit.point); // Eller targetUnit.transform.position
                }
                return;
            }

            // Klickade på en byggnad
            if (hit.collider.TryGetComponent<Building>(out Building targetBuilding) && hit.collider.TryGetComponent<NetworkIdentity>(out var buildingIdentity))
            {
                // Antagande: Metod IsEnemy finns och använder localPlayer.netId
                int targetOwnerTeamId = GetTargetTeamId(targetBuilding.OwnerNetId); // <-- PLATSHÅLLARE! Se nedan.
                bool isEnemyBuilding = targetOwnerTeamId != localPlayer.teamID && targetOwnerTeamId != 0;
                // Förklaring: Målet är en fiende om dess team-ID (när vi väl kan få det) skiljer sig från vårt OCH inte är 0 (neutral).

                // Kolla om vi har valt arbetare
                bool workerSelected = IsWorkerSelected(); // Implementera denna hjälpmetod!

                if (workerSelected)
                {
                    List<uint> workerIds = GetSelectedWorkerNetIds(); // Implementera denna!
                    if (targetBuilding.NeedsConstruction && targetBuilding.OwnerNetId == localPlayer.netId) // Egen byggplats?
                    {
                        // Antagande: Cmd_StartBuilding finns i NetworkPlayer som anropar worker.Cmd_StartBuilding
                        // För enkelhet, skicka bara en worker? Eller alla? Designval.
                        // localPlayer.CmdOrderWorkersToBuild(workerIds, buildingIdentity.netId); // Exempel på nytt kommando
                        Debug.Log("ORDER BUILD (Implement Command)");
                        // Kanske bara flytta dit och låta Worker AI ta över?
                        // localPlayer.CmdMoveUnits(workerIds, hit.point);
                        return;
                    }
                    else if (isEnemyBuilding || targetBuilding.OwnerNetId == 0) // Fiende eller neutral byggnad?
                    {
                        // TODO: Kolla om byggnaden är "capturable"
                        // localPlayer.CmdOrderWorkersToCapture(workerIds, buildingIdentity.netId); // Exempel
                        Debug.Log("ORDER CAPTURE (Implement Command)");
                        return;
                    }
                    else if (targetBuilding.OwnerNetId == localPlayer.netId && targetBuilding.healthComponent.CurrentHealth < targetBuilding.healthComponent.MaxHealth) // Egen skadad byggnad?
                    {
                        // localPlayer.CmdOrderWorkersToRepair(workerIds, buildingIdentity.netId); // Exempel
                        Debug.Log("ORDER REPAIR (Implement Command)");
                        return;
                    }
                }
                // Om inte worker vald, eller om worker-conditions inte möttes:
                if (isEnemyBuilding) // Vanliga enheter attackerar fientlig byggnad
                {
                    localPlayer.CmdAttackTarget(selectedUnitNetIds, buildingIdentity);
                    // TODO: Spela ljud/effekt
                }
                else // Flytta till egen/neutral/allierad byggnad
                {
                    // TODO: Speciell interaktion? Gå in i bunker? Lämna resurser (för Harvesters)?
                    // För nu: Flytta bara till positionen
                    localPlayer.CmdMoveUnits(selectedUnitNetIds, hit.point);
                }
                return;
            }

            // Klickade på en resurs (HarvestableCrystal)?
            if (hit.collider.TryGetComponent<HarvestableCrystal>(out HarvestableCrystal targetCrystal) && hit.collider.TryGetComponent<NetworkIdentity>(out var crystalIdentity))
            {
                // Kolla om vi har valt Harvesters
                bool harvesterSelected = IsHarvesterSelected(); // Implementera!
                if (harvesterSelected)
                {
                    List<uint> harvesterIds = GetSelectedHarvesterNetIds(); // Implementera!
                                                                            // localPlayer.CmdOrderHarvestersToHarvest(harvesterIds, crystalIdentity.netId); // Exempel
                    Debug.Log("ORDER HARVEST (Implement Command)");
                }
                else
                {
                    // Vanliga enheter gör inget med kristaller? Flytta dit?
                    localPlayer.CmdMoveUnits(selectedUnitNetIds, hit.point);
                }
                return;
            }


            // Om vi träffade något annat (dekoration etc), behandla som markklick
            localPlayer.CmdMoveUnits(selectedUnitNetIds, hit.point);
        }

        void HandleRightClick_ProductionBuildingSelected(uint buildingNetId, RaycastHit hit)
        {
            // Sätt Rally Point där vi klickade
            // CmdSetRallyPoint behöver byggnadens NetID och positionen
            localPlayer.CmdSetRallyPoint(buildingNetId, hit.point);
            // TODO: Visa visuell feedback för rally point
        }

        void HandleAttackMoveClick()
        {
            if (localPlayer == null || selectionManager == null) return;

            // TODO: Implementera Attack-Move
            // 1. Hämta valda enheter
            List<uint> selectedUnitNetIds = selectionManager.GetSelectedUnitNetIds();
            if (selectedUnitNetIds.Count == 0) { CancelModes(); return; }

            // 2. Raycast för att få destinationen
            Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, ~LayerMask.GetMask("Ignore Raycast", "UI")))
            {
                // 3. Skicka ett nytt kommando för Attack-Move
                // localPlayer.CmdAttackMoveUnits(selectedUnitNetIds, hit.point); // Kräver nytt command i NetworkPlayer
                Debug.Log($"ATTACK MOVE ordered to {hit.point} (Implement Command)");
            }

            CancelModes(); // Återgå till normalt läge efter klick
                           // TODO: Uppdatera muspekare
        }


        // --- Kortkommandon ---
        void HandleKeyboardInput()
        {
            // Stopp ('S')
            if (Keyboard.current.sKey.wasPressedThisFrame)
            {
                IssueStopCommandToSelectedUnits();
            }

            // Attack-Move ('A')
            if (Keyboard.current.aKey.wasPressedThisFrame)
            {
                SetAttackMoveMode();
            }

            // Byggmeny ('B') - Exempel
            if (Keyboard.current.bKey.wasPressedThisFrame)
            {
                // Antagande: UIManager finns och har metoden
                UIManager.Instance?.ToggleBuildMenu();
            }

            // TODO: Fler kortkommandon (kontrollgrupper, abilities etc.)
        }

        // --- Hjälpmetoder ---

        private void SetAttackMoveMode()
        {
            // TODO: Implementera Attack-Move mode
            // 1. Kolla om militära enheter är valda
            if (selectionManager.GetSelectedUnitNetIds().Count > 0) // Förfinad check behövs kanske
            {
                isAttackMoveActive = true;
                Debug.Log("Attack-Move Mode Activated. Left-click to set target point.");
                // TODO: Ändra muspekare till attack-cursor
            }
            else
            {
                Debug.Log("No units selected to activate Attack-Move.");
            }
        }

        private void IssueStopCommandToSelectedUnits()
        {
            if (localPlayer == null || selectionManager == null) return;
            List<uint> selectedUnitNetIds = selectionManager.GetSelectedUnitNetIds();
            if (selectedUnitNetIds.Count > 0)
            {
                // localPlayer.CmdStopUnits(selectedUnitNetIds); // Kräver nytt command i NetworkPlayer
                Debug.Log("STOP Command issued (Implement Command)");
                // TODO: Spela ljud/effekt
            }
        }

        private void HandleEscapeKey()
        {
            // Avbryt aktiva lägen
            CancelModes();
            // Avmarkera allt? Designval.
            selectionManager?.ClearSelection();
        }

        private void CancelModes()
        {
            if (isAttackMoveActive)
            {
                isAttackMoveActive = false;
                Debug.Log("Attack-Move Mode Deactivated.");
                // TODO: Återställ muspekare
            }
            // if (IsPlacingBuilding) { CancelPlacementMode(); }
            // Fler lägen...
        }

        private bool IsPointerOverUIObject()
        {
            // Kräver att du har en EventSystem i scenen
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        // --- Hjälpmetoder som behöver implementeras mer specifikt ---

        private bool IsWorkerSelected()
        {
            if (selectionManager == null) return false;
            var selectedObjects = selectionManager.GetSelectedObjects();
            if (selectedObjects.Count == 0) return false;
            // Kolla om ALLA valda objekt är Workers (och minst ett finns)
            return selectedObjects.All(obj => obj != null && obj.GetComponent<ConstructionWorker>() != null);
        }

        private List<uint> GetSelectedWorkerNetIds()
        {
            if (selectionManager == null) return new List<uint>();
            return selectionManager.GetSelectedObjects()
                .Where(obj => obj != null && obj.TryGetComponent<ConstructionWorker>(out _))
                .Select(obj => obj.GetComponent<NetworkIdentity>().netId)
                .ToList();
        }

        private bool IsHarvesterSelected()
        {
            if (selectionManager == null) return false;
            var selectedObjects = selectionManager.GetSelectedObjects();
            if (selectedObjects.Count == 0) return false;
            return selectedObjects.All(obj => obj != null && obj.GetComponent<HarvesterUnit>() != null);
        }
        private List<uint> GetSelectedHarvesterNetIds()
        {
            if (selectionManager == null) return new List<uint>();
            return selectionManager.GetSelectedObjects()
                .Where(obj => obj != null && obj.TryGetComponent<HarvesterUnit>(out _))
                .Select(obj => obj.GetComponent<NetworkIdentity>().netId)
                .ToList();
        }

        private int GetTargetTeamId(uint targetOwnerNetId)
        {
            if (targetOwnerNetId == 0) return 0; // Neutral
            if (localPlayer != null && targetOwnerNetId == localPlayer.netId) return localPlayer.teamID; // Egen enhet/byggnad

            // --- TODO: IMPLEMENTERA RIKTIG LOOKUP HÄR! ---
            // Detta är den svåra delen på klienten. Kräver att team-info finns tillgänglig.
            // Möjliga sätt (senare):
            // 1. Hitta ägarens NetworkPlayer-objekt via NetworkClient.spawned och läs dess teamID.
            // 2. Läs från en synkad lista med spelare/team från PlayerManager.
            // 3. Om Unit/Building synkar sitt teamID direkt, läs det.

            // Tillfällig placeholder för att koden ska kompilera: Anta fiende om inte egen/neutral
            return -1; // Returnera ett "okänt" eller "fiende"-default team ID just nu
                       // Alternativt:
                       // return (targetOwnerNetId == localPlayer.netId) ? localPlayer.teamID : 99; // Anta team 99 för alla andra
        }


    } // End class InputManager
} // End namespace RTSGAME