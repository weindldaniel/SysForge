using System.Runtime.CompilerServices;
using System.Text.Json;
using Anlagensimulation;
using Taktzeitexport;

// ---- Serielle Anlage, wie in Anlagensimulation/Program.cs (Kapitel 4.3) ----
var anlage = FiktiveAnlage.Erstellen();

// ---- Simulationsparameter ----
const int ANZAHL_LAEUFE = 5;
const int TEILE_PRO_LAUF = 50;
const int SEED = 42;

// Das Arbeitsverzeichnis des Prozesses unterscheidet sich je nach Startart
// (dotnet run: Projektordner; Start ueber Visual Studio: Build-Ausgabeordner)
// - ein relativer Pfad landet damit mal hier, mal dort. Der Ausgabepfad wird
// daher unabhaengig davon relativ zu dieser Quelldatei aufgeloest.
string ausgabePfad = Path.Combine(QuellVerzeichnis(), "taktzeiten.json");

var sim = new Simulation(anlage);
var rng = new Random(SEED);

// ---- Läufe simulieren ----
var ergebnisse = new List<LaufExport>();
var alleTaktzeiten = new List<double>();

for (int lauf = 1; lauf <= ANZAHL_LAEUFE; lauf++)
{
    double[] abgaenge = sim.SimuliereLauf(TEILE_PRO_LAUF, rng).Abgaenge;
    double[] taktzeiten = Simulation.Taktzeiten(abgaenge);

    ergebnisse.Add(new LaufExport(lauf, taktzeiten.Length, taktzeiten));
    alleTaktzeiten.AddRange(taktzeiten);

    (double mittelwert, double std) = Statistik.Kennzahlen(taktzeiten);
    Console.WriteLine($"Lauf {lauf,4}: n={taktzeiten.Length,4}  Mittelwert={mittelwert:0.0000}  Std={std:0.0000}");
}

// ---- Gesamtergebnis ----
(double mittelwertGesamt, double stdGesamt) = Statistik.Kennzahlen(alleTaktzeiten);
Console.WriteLine(new string('-', 56));
Console.WriteLine($"Läufe gesamt         : {ANZAHL_LAEUFE}");
Console.WriteLine($"Teile pro Lauf       : {TEILE_PRO_LAUF}");
Console.WriteLine($"Taktzeiten gesamt (n): {alleTaktzeiten.Count}");
Console.WriteLine($"Mittelwert gesamt    : {mittelwertGesamt:0.0000}");
Console.WriteLine($"Std.-Abweichung      : {stdGesamt:0.0000}");

// ---- JSON-Export ----
var export = new TaktzeitExportDatei(
    ErstelltAm: DateTime.UtcNow.ToString("o"),
    Anlage: string.Join(" -> ", FiktiveAnlage.Reihenfolge),
    AnzahlLaeufe: ANZAHL_LAEUFE,
    TeileProLauf: TEILE_PRO_LAUF,
    Seed: SEED,
    Ergebnisse: ergebnisse);

var jsonOptionen = new JsonSerializerOptions { WriteIndented = true };
string json = JsonSerializer.Serialize(export, jsonOptionen);
File.WriteAllText(ausgabePfad, json);

Console.WriteLine();
Console.WriteLine($"JSON exportiert: {ausgabePfad}");

static string QuellVerzeichnis([CallerFilePath] string hier = "") => Path.GetDirectoryName(hier)!;
