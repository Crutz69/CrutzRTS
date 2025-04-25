// Filnamn: CategoryButtonHelper.cs
using UnityEngine;
using RTSGAME; // För att komma åt BuildingType enum

// Lägg detta script på varje GameObject som är en kategoriknapp
// under BuildCategoryPanel i din UI-hierarki.
public class CategoryButtonHelper : MonoBehaviour
{
    [Tooltip("Vilken byggnadskategori representerar denna knapp? Sätt i Inspektorn!")]
    public BuildingType categoryToSet = BuildingType.None;

    // Ingen Start() eller Update() behövs här,
    // UIManager kommer att hitta detta script och läsa av 'categoryToSet'.
}