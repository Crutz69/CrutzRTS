// Filnamn: Enums.cs
// Placeras lämpligen i din huvud-scriptmapp, t.ex. Assets/RTSGAME/Scripts

// Samlar alla viktiga enum-definitioner för spelet på ett ställe.
namespace RTSGAME
{
    /// <summary>
    /// Definierar vilken grundläggande typ ett byggbart objekt är.
    /// Används i BuildableData för att styra logik (placera vs köa vs forska).
    /// </summary>
    public enum BuildableItemType
    {
        Unit,       // En enhet som ska köas och produceras
        Building,   // En byggnad som ska placeras ut och konstrueras
        Upgrade     // En uppgradering som ska forskas
    }

    /// <summary>
    /// Definierar de olika kategorierna som byggnader eller enheter tillhör.
    /// Används för att filtrera i UI (Build Category Panel) och spellogik.
    /// </summary>
    public enum BuildingType // Eller CategoryType?
    {
        None,       // Default / Ingen vald
        Building,      // Din "Hem/Bas"-kategori (Townhall etc.)
        Defence,     // Din "Försvar"-kategori
        Cavalry,    // Din "Kavalleri"-kategori
        Infantry,   // Din "Infanteri"-kategori
        Archer,     // Din "Bågskyttar"-kategori
        Flying,     // Din "Flygande"-kategori
        HeavyUnits       // Din "Stora enheter/Golems"-kategori
    }

    public enum BuildingState
    {
        Ghost,              // Visas bara som blueprint för placering (klient-sida)
        Placing,            // Har precis placerats, väntar på att konstruktion ska börja? (Eller direkt till Constructing?)
        Constructing,       // Under konstruktion av en arbetare
        Operational,        // Färdigbyggd och fungerar normalt
        Disabled_NoPower,   // Färdigbyggd men avstängd p.g.a. Mana-brist (Upkeep > Generation)
        BeingCaptured,      // Håller på att tas över av fienden
        Destroyed           // Förstörd
    }

    public enum BuildPauseState
    {
        None,       
        Manual,        
        Resource, 
    }

    /// <summary>
    /// Definierar olika typer av rustning för enheter och byggnader.
    /// Används i kombatsberäkningar.
    /// </summary>
    public enum ArmorType
    {
        Unarmored,  // Lättklädda enheter, magiker?
        Light,      // Lätt infanteri, vissa flygare?
        Medium,     // Standard pansar, riddare?
        Heavy,      // Tungt pansar, tanks/golems?
        Fortified,  // Byggnader
        Stone,      // Specifikt för t.ex. Harvester Golem?
        Ethereal,   // Spöken etc?
        Hero
    }

    /// <summary>
    /// Definierar olika typer av skada.
    /// Används i kombatsberäkningar för att interagera med ArmorType.
    /// </summary>
    public enum DamageType
    {
        Normal,     // Standard fysisk skada
        Piercing,   // Bra mot lätt pansar
        Siege,      // Bra mot byggnader/fortifierat
        Magic,      // Ignorerar ofta vanligt pansar
        Fire,       // Kan ha DoT-effekt?
        Chaos,      // Bra mot allt/speciella enheter?
        Hero        // Speciell typ för hjältar?
    }

    public enum PlayerStatus
    {
        Playing,    // Spelar aktivt
        Defeated,   // Har förlorat (alla byggnader/enheter borta?)
        Spectating  // Tittar på spelet (kan implementeras senare)
                    // Lägg till fler vid behov (t.ex. Disconnected, Loading?)
    }

    public enum CrystalType
    {
        None,  // Represents no crystal type (e.g., empty harvester)
        Green,
        Blue,
        Red
        // Lägg till fler typer här om det behövs
    }
    // --- Lägg till fler av dina egna Enums här vid behov ---

} // Slut på namespace RTSGAME