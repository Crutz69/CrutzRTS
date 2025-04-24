// Filnamn: BuildingIconButtonHandler.cs
// Placeras på roten av din BuildingIconButton_Prefab.

using UnityEngine;
using UnityEngine.EventSystems; // Viktigt för interfacet IPointerClickHandler etc.
using UnityEngine.UI;         // För Image etc.
using Mirror;                 // För NetworkIdentity (om SelectionManager behöver det)

// Se till att detta ligger i samma namespace som dina andra scripts
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
        /// Sätts av UIController när knappen skapas.
        /// </summary>
        public Building AssociatedBuilding { get; set; }

        private UIController uiController; // Cachelagrad referens till UIController
        private bool isPointerOver = false; // För att hantera färg när man släpper upp musen

        // --- Unity Methods ---

        void Awake()
        {
            // Försök hitta referenser om de inte är satta i Inspektorn
            if (iconImage == null) iconImage = GetComponent<Image>(); // Om ikonen är på samma objekt
            // Om Icon är ett barn: iconImage = transform.Find("Icon")?.GetComponent<Image>();
            if (highlightBorder == null) highlightBorder = transform.Find("HighlightBorder")?.gameObject; // Hitta GameObject

            // Hitta UIController (Singleton är bäst, FindObjectOfType är en fallback)
            // Försök hitta via UIManager Singleton först
            if (UIManager.Instance != null)
            {
                uiController = UIManager.Instance.GetComponent<UIController>(); // Antag att UIController sitter på samma objekt som UIManager Singleton
                if (uiController == null)
                {
                    uiController = UIManager.Instance.GetComponentInChildren<UIController>(); // Eller som barn?
                }
            }
            // Fallback om UIManager inte hittades eller inte har UIController
            if (uiController == null) uiController = FindObjectOfType<UIController>();
            if (uiController == null) Debug.LogError("BuildingIconButtonHandler could not find UIController!", this);

            // Sätt initialt visuellt state
            if (iconImage != null) normalColor = iconImage.color; // Spara ursprungsfärgen
            SetHighlightActive(false); // Se till att highlight är av från start
        }

        // --- IPointerClickHandler Implementation ---

        /// <summary>
        /// Kallas när pekaren (musen) klickar på detta UI-element.
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (AssociatedBuilding == null || uiController == null)
            {
                Debug.LogError("Button clicked but AssociatedBuilding or UIController is null!", this);
                return;
            }

            // Kolla vilket klick det var
            if (eventData.button == PointerEventData.InputButton.Left) // Vänsterklick
            {
                if (eventData.clickCount == 1) // Enkelklick
                {
                    // Meddela UIController att denna specifika byggnadsinstans har valts i UI:t
                    uiController.SelectBuildingInstance(AssociatedBuilding, this);
                    // Debug.Log($"Single Click on: {AssociatedBuilding.BuildingName}");
                }
                else if (eventData.clickCount >= 2) // Dubbelklick
                {
                    Debug.Log($"Double Click on: {AssociatedBuilding.BuildingName}");

                    // 1. Flytta Kameran (Använder nu ditt korrekta scriptnamn)
                    if (RTSCameraController.Instance != null) // *** Korrekt namn här ***
                    {
                        // Anropa din TeleportTo-metod
                        Vector3 targetPos = AssociatedBuilding.transform.position;
                        RTSCameraController.Instance.TeleportTo(targetPos); // *** Korrekt namn här ***
                    }
                    else { Debug.LogWarning("RTSCameraController.Instance not found for double-click teleport!"); }

                    // 2. Välj Byggnaden i spelet
                    // Försök hitta SelectionManager via Singleton
                    if (SelectionManager.Instance != null)
                    {
                        NetworkIdentity buildingIdentity = AssociatedBuilding.GetComponent<NetworkIdentity>();
                        if (buildingIdentity != null)
                        {
                            SelectionManager.Instance.SelectObject(buildingIdentity); // Eller liknande metod
                        }
                        else { Debug.LogWarning($"Building {AssociatedBuilding.name} missing NetworkIdentity for selection!"); }
                    }
                    else { Debug.LogWarning("SelectionManager.Instance not found for double-click selection!"); }

                    // 3. (Valfritt) Stäng byggmenyn efter teleport?
                    // uiController.CloseBuildMenus();
                }
            }
            // Här kan du lägga till logik för högerklick om det behövs på dessa ikoner
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
            if (iconImage != null) iconImage.color = normalColor; // Återställ till normalfärg
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) // Reagera bara på vänsterklick här
            {
                if (iconImage != null) iconImage.color = pressedColor;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                // Gå tillbaka till hover-färg om musen fortfarande är över, annars normal
                if (iconImage != null) iconImage.color = isPointerOver ? hoverColor : normalColor;
            }
        }

        // --- Metod för att styra Highlight (anropas av UIController) ---

        /// <summary>
        /// Aktiverar eller inaktiverar highlight-grafiken för denna ikon.
        /// Anropas av UIController när SelectBuildingInstance körs.
        /// </summary>
        /// <param name="isActive">Ska highlight visas?</param>
        public void SetHighlightActive(bool isActive)
        {
            if (highlightBorder != null)
            {
                highlightBorder.SetActive(isActive);
            }
            // Om du inte har en separat border, kan du ändra t.ex. iconImage.color här istället
            // if (!isActive && iconImage != null) iconImage.color = normalColor; // Se till att återställa färgen
        }
    } // Slut på klassen BuildingIconButtonHandler
} // Slut på namespace RTSGAME