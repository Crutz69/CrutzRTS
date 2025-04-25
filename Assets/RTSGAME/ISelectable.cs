// Filnamn: ISelectable.cs
// Placeras förslagsvis i Assets/RTSGAME/Scripts/Interfaces/ eller liknande

// *** Lägg till using för relevanta typer om det behövs i framtiden ***
// using UnityEngine;
// using Mirror;

namespace RTSGAME // *** Ligger nu i namnrymden ***
{
    /// <summary>
    /// Interface för objekt som kan väljas av spelaren.
    /// Implementeras av t.ex. Unit och Building.
    /// </summary>
    public interface ISelectable
    {
        /// <summary>
        /// Returnerar NetID för ägaren av detta objekt.
        /// Viktigt för att SelectionManager ska kunna filtrera val.
        /// </summary>
        /// <returns>Ägarens Network ID (uint).</returns>
        uint GetOwnerNetId();

        /// <summary>
        /// Metod som anropas när objektet väljs (t.ex. av SelectionManager).
        /// Ansvarar för att visa visuell feedback (selection circle etc.).
        /// Körs på klienten.
        /// </summary>
        void Select();

        /// <summary>
        /// Metod som anropas när objektet avmarkeras.
        /// Ansvarar för att dölja visuell feedback.
        /// Körs på klienten.
        /// </summary>
        void Deselect();
    }
}