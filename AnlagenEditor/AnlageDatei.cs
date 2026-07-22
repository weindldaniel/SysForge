using Anlagensimulation;

namespace AnlageEditor;

/// <summary>
/// Vollstaendiger, serialisierbarer Zustand eines angelegten Systems: Stationen (inkl.
/// Canvas-Position und Systeminformationen), Verbindungen sowie Ziel/Level/Meta-Informationen.
/// Wird sowohl fuer Speichern/Laden als Datei als auch fuer den Undo-Stack verwendet.
/// </summary>
public sealed class AnlageDatei
{
    public string Formatversion { get; set; } = "1";
    public Zielkategorie? Ziel { get; set; }
    public int? Level { get; set; }
    public SystemMetaInformationen Meta { get; set; } = new();
    public List<StationDatei> Stationen { get; set; } = new();
    public List<VerbindungDatei> Verbindungen { get; set; } = new();
}

public sealed class StationDatei
{
    public string Name { get; set; } = "";
    public double Mu { get; set; }
    public double Sigma { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Breite { get; set; }
    public double Hoehe { get; set; }
    public bool IstQuelle { get; set; }
    public Dictionary<string, string> Systeminformationen { get; set; } = new();
}

public sealed class VerbindungDatei
{
    public string Von { get; set; } = "";
    public string Nach { get; set; } = "";
}
