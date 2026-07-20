using Anlagensimulation;

// ---- Fiktive Anlage aus Kapitel 4.3 (Bewertung der Ergebnisunsicherheit) ----
var anlage = FiktiveAnlage.Erstellen();

// ---- Versuchsparameter ----
int R = 100;    // Wiederholungen
int K = 200;    // Teile je Lauf
int warmup = 50;     // verworfene Teile (Einschwingphase)
double niveau = 0.99;   // Konfidenzniveau
int seed = 42;

var sim = new Simulation(anlage);
var rng = new Random(seed);

// ---- R Wiederholungen: je Lauf die stationaere mittlere Taktzeit ----
var laufMittel = new double[R];
for (int r = 0; r < R; r++)
{
    double[] abgaenge = sim.SimuliereLauf(K, rng).Abgaenge;
    double[] takt = Simulation.Taktzeiten(abgaenge);

    double summe = 0.0;
    for (int i = warmup; i < takt.Length; i++)
        summe += takt[i];
    laufMittel[r] = summe / (takt.Length - warmup);
}

// ---- Ausgabe ----
Console.WriteLine("Mittlere Taktzeit je Lauf:");
for (int r = 0; r < R; r++)
    Console.WriteLine($"  Lauf {r + 1,4} : {laufMittel[r]:0.0000}");
Console.WriteLine(new string('-', 48));

KonfidenzIntervall ki = Statistik.Konfidenzintervall(laufMittel, niveau);
Console.WriteLine($"Wiederholungen R     : {R}");
Console.WriteLine($"Konfidenzniveau      : {niveau:P0}");
Console.WriteLine($"Mittelwert Taktzeit  : {ki.Mittelwert:0.0000}");
Console.WriteLine($"t-Quantil (df={R - 1})   : {ki.TQuantil:0.0000}");
Console.WriteLine($"Konfidenzintervall   : [ {ki.Untere:0.0000} ; {ki.Obere:0.0000} ]");
Console.WriteLine($"Halbbreite           : {ki.Halbbreite:0.0000}");
