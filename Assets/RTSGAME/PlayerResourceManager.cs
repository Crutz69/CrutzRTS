using UnityEngine;
using UnityEngine.UI; // Om du vill koppla till UI-Text

public class PlayerResourceManager : MonoBehaviour
{
    public int currentFantasyResource = 200; // Startvärde
    public Text resourceDisplayText; // Dra din UI Text hit i Inspektorn (valfritt)

    // Singleton instance (enkel variant)
    public static PlayerResourceManager Instance { get; private set; }

    void Awake()
    {
        // Enkel Singleton setup
        if (Instance == null)
        {
            Instance = this;
            // DontDestroyOnLoad(gameObject); // Behövs bara om du byter scen
        }
        else
        {
            Destroy(gameObject);
        }
        UpdateResourceDisplay(); // Uppdatera vid start
    }


    public void AddResources(int amount)
    {
        if (amount > 0)
        {
            currentFantasyResource += amount;
            Debug.Log($"Added {amount} resources. Total: {currentFantasyResource}");
            UpdateResourceDisplay();
        }
    }

    public bool SpendResources(int amount)
    {
        if (amount > 0 && currentFantasyResource >= amount)
        {
            currentFantasyResource -= amount;
            Debug.Log($"Spent {amount} resources. Total: {currentFantasyResource}");
            UpdateResourceDisplay();
            return true; // Köpet lyckades
        }
        Debug.Log($"Failed to spend {amount} resources. Current: {currentFantasyResource}");
        return false; // Inte tillräckligt med resurser
    }

    void UpdateResourceDisplay()
    {
        if (resourceDisplayText != null)
        {
            resourceDisplayText.text = "Crystals: " + currentFantasyResource; // Anpassa texten
        }
    }
}