// Filnamn: BuildingIconButtonHandler.cs
// Uppdaterad för att använda UIManager istället för UIController

using UnityEngine;
using UnityEngine.EventSystems; // Viktigt för interfacet IPointerClickHandler etc.
using UnityEngine.UI;         // För Image etc.
using Mirror;               // För NetworkIdentity (om SelectionManager behöver det)
using RTSGAME;              // *** Se till att denna finns om UIManager är i detta namespace ***

namespace RTSGAME
{
    [RequireComponent(typeof(Image))] // Bra att kräva en Image som grund
    public class BuildingIconButtonHandler : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [Header("UI References")]
        [Tooltip("Bilden som visar själva byggnadsikonen. Färgen ändras vid hover/press.")]
        [SerializeField] private Image iconImage;
        [Tooltip("GameObject med en Image som visas när denna ikon är vald i UI:t.")]
        [SerializeField] private GameObject highlightBorder; // Använd GameObject för att enkelt visa/dölja

        [Header("Visual Feedback Colors")]
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color hoverColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        [SerializeField] private Color pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);

        // --- Properties & State ---
        /// <summary>
        /// Den specifika byggnadsinstansen som denna ikon representerar.
        /// Sätts av UIManager när knappen skapas. // *** ÄNDRING: Kommentar ***
        /// </summary>
        public Building AssociatedBuilding { get; set; }

        // *** ÄNDRING: Variabeltyp och namn ***
        private UIManager uiManager; // Cachelagrad referens till UIManager
        private bool isPointerOver = false; // För att hantera färg när man släpper upp musen

        // --- Unity Methods ---

        void Awake()
        {
            // Försök hitta referenser om de inte är satta i Inspektorn
            if (iconImage == null) iconImage = GetComponent<Image>();
            if (highlightBorder == null) highlightBorder = transform.Find("HighlightBorder")?.gameObject;

            // *** ÄNDRING: Hitta UIManager via Singleton ***
            uiManager = UIManager.Instance; // Försök få tag på Singleton direkt

            // Fallback om Singleton inte var redo/hittades (bör inte hända om UIManager är korrekt satt upp)
            if (uiManager == null) uiManager = FindAnyObjectByType<UIManager>();

            // *** ÄNDRING: Felmeddelande ***
            if (uiManager == null) Debug.LogError("BuildingIconButtonHandler could not find UIManager!", this);

            // Sätt initialt visuellt state
            if (iconImage != null) normalColor = iconImage.color; // Spara ursprungsfärgen (eller sätt initialt till normalColor)
            else if (iconImage != null) iconImage.color = normalColor; // Sätt färgen om den inte redan har normalColor

            SetHighlightActive(false); // Se till att highlight är av från start
        }

        // --- IPointerClickHandler Implementation ---

        public void OnPointerClick(PointerEventData eventData)
        {
            // *** ÄNDRING: Kollar uiManager ***
            if (AssociatedBuilding == null || uiManager == null)
            {
                // *** ÄNDRING: Felmeddelande ***
                Debug.LogError("Button clicked but AssociatedBuilding or UIManager is null!", this);
                return;
            }

            if (eventData.button == PointerEventData.InputButton.Left) // Vänsterklick
            {
                if (eventData.clickCount == 1) // Enkelklick
                {
                    // *** ÄNDRING: Anropar uiManager ***
                    // Meddela UIManager att denna specifika byggnadsinstans har valts i UI:t
                    uiManager.SelectBuildingInstance(AssociatedBuilding, this);
                }
                else if (eventData.clickCount >= 2) // Dubbelklick
                {
                    Debug.Log($"Double Click on: {AssociatedBuilding.BuildingName}");

                    // Flytta Kameran
                    if (RTSCameraController.Instance != null)
                    {
                        Vector3 targetPos = AssociatedBuilding.transform.position;
                        RTSCameraController.Instance.TeleportTo(targetPos);
                    }
                    else { Debug.LogWarning("RTSCameraController.Instance not found for double-click teleport!"); }

                    // Välj Byggnaden i spelet
                    if (SelectionManager.Instance != null)
                    {
                        // NetworkIdentity behövs inte för att skicka GameObject till SelectSingleObject
                        SelectionManager.Instance.SelectSingleObject(AssociatedBuilding.gameObject);
                    }
                    else { Debug.LogWarning("SelectionManager.Instance not found for double-click selection!"); }

                    // (Valfritt) Stäng byggmenyn efter teleport?
                    // *** ÄNDRING: Kommentar ***
                    // uiManager.CloseBuildMenus();
                }
            }
            // else if (eventData.button == PointerEventData.InputButton.Right) { ... }
        }

        // --- IPointer Enter/Exit/Down/Up (För visuell feedback) ---

        public void OnPointerEnter(PointerEventData eventData)
        {
            isPointerOver = true;
            if (iconImage != null) iconImage.color = hoverColor;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isPointerOver = false;
            if (iconImage != null) iconImage.color = normalColor;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                if (iconImage != null) iconImage.color = pressedColor;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                if (iconImage != null) iconImage.color = isPointerOver ? hoverColor : normalColor;
            }
        }

        // --- Metod för att styra Highlight (anropas av UIManager) --- // *** ÄNDRING: Kommentar ***

        /// <summary>
        /// Aktiverar eller inaktiverar highlight-grafiken för denna ikon.
        /// Anropas av UIManager när SelectBuildingInstance körs. // *** ÄNDRING: Kommentar ***
        /// </summary>
        /// <param name="isActive">Ska highlight visas?</param>
        public void SetHighlightActive(bool isActive)
        {
            if (highlightBorder != null)
            {
                highlightBorder.SetActive(isActive);
            }
        }
    } // Slut på klassen BuildingIconButtonHandler
} // Slut på namespace RTSGAME