// Assets/RTSGAME/Scripts/SharedData/DamageType.cs
namespace RTSGAME
{
    public enum DamageType
    {
        Normal,     // Standard fysisk skada
        Piercing,   // Bra mot lätt pansar
        Siege,      // Bra mot byggnader/fortifierat
        Magic,      // Ignorerar ofta vanligt pansar
        Fire,       // Kan ha DoT-effekt?
        Chaos,      // Bra mot allt/speciella enheter?
        Hero        // Speciell typ för hjältar?
        // Lägg till de typer du behöver
    }
}