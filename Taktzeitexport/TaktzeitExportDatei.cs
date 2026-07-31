namespace Taktzeitexport;

/// <summary>Taktzeiten eines einzelnen Simulationslaufs fuer den JSON-Export.</summary>
public sealed record LaufExport(int Lauf, int N, double[] Taktzeiten);

/// <summary>Ergebnis mehrerer Simulationslaeufe fuer den JSON-Export.</summary>
public sealed record TaktzeitExportDatei(
    string ErstelltAm,
    string Anlage,
    int AnzahlLaeufe,
    int TeileProLauf,
    int Seed,
    List<LaufExport> Ergebnisse);
