// Filnamn: ManaBarController.cs
// Placeras på ett föräldraobjekt till Mana-barens UI-element i Canvasen.

using UnityEngine;
using UnityEngine.UI; // För Image och RectTransform

public class ManaBarController : MonoBehaviour
{
    [Header("UI Element References")]
    [Tooltip("Referens till Image-komponenten för den blå 'fyllda' stapeln.")]
    [SerializeField] private Image manaFillImage; // Kanske inte ändras om den alltid är full?

    [Tooltip("Referens till RectTransform för det röda strecket som visar upkeep.")]
    [SerializeField] private RectTransform upkeepMarkerRect; // Behöver flytta denna vertikalt

    [Tooltip("Referens till Image-komponenten för den grå overlayen (visas vid Mana-brist).")]
    [SerializeField] private Image lowPowerOverlayImage;

    [Tooltip("Valfritt: Referens till bakgrundsbilden för att få höjden.")]
    [SerializeField] private RectTransform backgroundBarRect; // Används för att beräkna markörens position

    // Interna värden (uppdateras av UIManager/NetworkPlayer hooks)
    private int currentGeneration = 0;
    private int currentUpkeep = 0;
    private bool hasSufficientPower = true;

    void Start()
    {
        // Göm overlay från start och sätt initiala värden om möjligt
        if (lowPowerOverlayImage != null)
        {
            lowPowerOverlayImage.enabled = false;
        }
        // Om bakgrunds-rect saknas, försök hämta från detta objekt
        if (backgroundBarRect == null)
        {
            backgroundBarRect = GetComponent<RectTransform>();
        }
        UpdateMarkerPosition(); // Sätt initial position
    }

    // --- Publika Metoder (Anropas av UIManager eller NetworkPlayer Hooks) ---

    /// <summary>
    /// Uppdaterar det kända värdet för mana-generation.
    /// </summary>
    public void UpdateGeneration(int generation)
    {
        currentGeneration = Mathf.Max(0, generation); // Säkerställ att det inte är negativt
        // Om den blå stapeln *inte* alltid är full, uppdatera dess fillAmount här.
        // Exempel: manaFillImage.fillAmount = Mathf.Clamp01((float)currentGeneration / MAX_POSSIBLE_GENERATION);
        UpdateMarkerPosition(); // Beräkna om markörens position
    }

    /// <summary>
    /// Uppdaterar det kända värdet för mana-upkeep.
    /// </summary>
    public void UpdateUpkeep(int upkeep)
    {
        currentUpkeep = Mathf.Max(0, upkeep); // Säkerställ att det inte är negativt
        UpdateMarkerPosition(); // Beräkna om markörens position
    }

    /// <summary>
    /// Uppdaterar power-status och visar/döljer overlay.
    /// </summary>
    public void UpdatePowerStatus(bool hasPower)
    {
        hasSufficientPower = hasPower;
        if (lowPowerOverlayImage != null)
        {
            lowPowerOverlayImage.enabled = !hasSufficientPower; // Visa overlay om INTE hasPower
        }
        UpdateMarkerPosition(); // Uppdatera ev. markörens färg? (Valfritt)
    }


    // --- Intern Logik ---

    /// <summary>
    /// Beräknar och sätter den vertikala positionen för upkeep-markören.
    /// </summary>
    private void UpdateMarkerPosition()
    {
        if (upkeepMarkerRect == null || backgroundBarRect == null) return;

        float barHeight = backgroundBarRect.rect.height; // Hämta höjden på bakgrunden/stapeln
        if (barHeight <= 0) return; // Undvik division med noll

        // Beräkna förhållandet mellan upkeep och generation
        // Om generation är 0 men upkeep > 0, visa max (1.0)
        // Annars, visa upkeep / generation, men max 1.0
        float fillRatio = 0f;
        if (currentGeneration > 0)
        {
            fillRatio = Mathf.Clamp01((float)currentUpkeep / currentGeneration);
        }
        else if (currentUpkeep > 0) // Ingen generation men det finns upkeep
        {
            fillRatio = 1f; // Visa max användning
        }
        // Om både gen och upkeep är 0, blir fillRatio 0.

        // Beräkna Y-positionen för markören
        // anchoredPosition.y = 0 är längst ner på RectTransform (om pivot är 0.5, 0)
        // anchoredPosition.y = barHeight är längst upp
        float targetY = fillRatio * barHeight;

        // Justera markörens position (vi ändrar bara Y-värdet)
        upkeepMarkerRect.anchoredPosition = new Vector2(upkeepMarkerRect.anchoredPosition.x, targetY);

        // Valfritt: Ändra färg på markören om man har för lite ström?
        // Image markerImage = upkeepMarkerRect.GetComponent<Image>();
        // if (markerImage != null) {
        //     markerImage.color = hasSufficientPower ? Color.red : Color.gray; // Exempel
        // }
    }
}