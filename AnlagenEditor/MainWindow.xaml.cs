using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Anlagensimulation;

namespace AnlageEditor;

public partial class MainWindow : Window
{
    private readonly Anlage _anlage = new();
    private readonly Dictionary<string, Point> _positionen = new();       // Name -> Boxposition
    private readonly Dictionary<string, Rectangle> _boxen = new();        // Name -> Rechteck (fuer Auswahl)
    private readonly Dictionary<string, TextBlock> _eigenschaften = new(); // Name -> Untertitel (µ/σ)
    private int _stationsIndex = 0;
    private string? _ausgewaehlt;   // aktuell selektierte Station

    private const double BoxBreite = 90;
    private const double BoxHoehe = 56;
    private const double StandardMu = 5.0;
    private const double StandardSigma = 1.0;

    public MainWindow()
    {
        InitializeComponent();
    }

    // ---- Werkzeug: Station per Drag-and-Drop aus der Toolbox ----
    private void ToolStation_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragDrop.DoDragDrop((DependencyObject)sender, "Station", DragDropEffects.Copy);
    }

    private void ModellCanvas_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.StringFormat)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void ModellCanvas_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.StringFormat)) return;
        if ((string)e.Data.GetData(DataFormats.StringFormat) != "Station") return;

        Point p = e.GetPosition(ModellCanvas);
        double x = Math.Max(0, p.X - BoxBreite / 2);
        double y = Math.Max(0, p.Y - BoxHoehe / 2);

        string name;
        do
        {
            _stationsIndex++;
            name = $"St{_stationsIndex}";
        } while (_positionen.ContainsKey(name));

        _anlage.FuegeStationHinzu(name, StandardMu, StandardSigma);
        _positionen[name] = new Point(x, y);

        ZeichneBox(name, x, y);
        WaehleStation(name);
        Melde($"Station {name} hinzugefügt. Eigenschaften links bearbeiten.");
    }

    // ---- Eigenschaften-Panel ----
    private void WaehleStation(string name)
    {
        if (_ausgewaehlt is not null && _boxen.TryGetValue(_ausgewaehlt, out Rectangle? alt))
        {
            alt.Stroke = Brushes.Black;
            alt.StrokeThickness = 1.5;
        }

        _ausgewaehlt = name;
        Rectangle rechteck = _boxen[name];
        rechteck.Stroke = Brushes.RoyalBlue;
        rechteck.StrokeThickness = 3;

        Knoten k = _anlage.Knoten.First(n => n.Name == name);
        TxtPropName.Text = k.Name;
        TxtPropMu.Text = k.Mu.ToString(CultureInfo.InvariantCulture);
        TxtPropSigma.Text = k.Sigma.ToString(CultureInfo.InvariantCulture);
        PropPanel.IsEnabled = true;
    }

    private void PropUebernehmen_Click(object sender, RoutedEventArgs e)
    {
        if (_ausgewaehlt is null) { Melde("Keine Station ausgewählt."); return; }
        if (!TryParseZahl(TxtPropMu.Text, out double mu)) { Melde("µ ist keine Zahl."); return; }
        if (!TryParseZahl(TxtPropSigma.Text, out double sigma)) { Melde("σ ist keine Zahl."); return; }

        Knoten k = _anlage.Knoten.First(n => n.Name == _ausgewaehlt);
        k.Mu = mu;
        k.Sigma = sigma;

        _eigenschaften[_ausgewaehlt].Text = FormatiereEigenschaften(k);
        Melde($"Station {_ausgewaehlt}: µ={mu}, σ={sigma} übernommen.");
    }

    private void Verbinden_Click(object sender, RoutedEventArgs e)
    {
        string von = TxtVon.Text.Trim();
        string nach = TxtNach.Text.Trim();
        if (!_positionen.ContainsKey(von) || !_positionen.ContainsKey(nach))
        {
            Melde("von/nach: Station unbekannt."); return;
        }
        _anlage.Verbinde(von, nach);
        ZeichnePfeil(_positionen[von], _positionen[nach]);
        Melde($"Verbindung {von} \u2192 {nach} gesetzt.");
        TxtVon.Clear(); TxtNach.Clear();
    }

    private void Quellen_Click(object sender, RoutedEventArgs e)
    {
        string[] namen = TxtQuellen.Text.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (namen.Length == 0) { Melde("Keine Quelle angegeben."); return; }
        foreach (string n in namen)
            if (!_positionen.ContainsKey(n)) { Melde($"Station {n} unbekannt."); return; }

        _anlage.SetzeQuellen(namen);
        foreach (string n in namen) MarkiereQuelle(_positionen[n]);
        Melde($"Quelle(n): {string.Join(", ", namen)}");
    }

    private void Simulieren_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtR.Text, out int R) ||
            !int.TryParse(TxtK.Text, out int K) ||
            !int.TryParse(TxtWarmup.Text, out int warmup) ||
            !TryParseZahl(TxtNiveau.Text, out double niveau))
        {
            Melde("Simulationsparameter ungültig."); return;
        }
        if (warmup >= K - 1) { Melde("Warm-up muss kleiner als K sein."); return; }
        if (_anlage.Quellen.Count == 0) { Melde("Bitte zuerst eine Quelle setzen."); return; }

        var sim = new Simulation(_anlage);
        var rng = new Random(42);
        var laufMittel = new double[R];

        for (int r = 0; r < R; r++)
        {
            double[] abgaenge = sim.SimuliereLauf(K, rng);
            double[] takt = Simulation.Taktzeiten(abgaenge);

            double summe = 0.0;
            for (int i = warmup; i < takt.Length; i++) summe += takt[i];
            laufMittel[r] = summe / (takt.Length - warmup);
        }

        KonfidenzIntervall ki = Statistik.Konfidenzintervall(laufMittel, niveau);
        Melde($"Mittlere Taktzeit: {ki.Mittelwert:0.0000}\n" +
              $"KI ({niveau:P0}): [{ki.Untere:0.0000}; {ki.Obere:0.0000}]\n" +
              $"Halbbreite: {ki.Halbbreite:0.0000}");
    }

    // ---- Zeichnen ----
    private void ZeichneBox(string name, double x, double y)
    {
        var rect = new Rectangle
        {
            Width = BoxBreite,
            Height = BoxHoehe,
            RadiusX = 6,
            RadiusY = 6,
            Stroke = Brushes.Black,
            StrokeThickness = 1.5,
            Fill = Brushes.WhiteSmoke,
            Cursor = Cursors.Hand,
            Tag = name
        };
        rect.MouseLeftButtonDown += (_, e) => { WaehleStation(name); e.Handled = true; };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        ModellCanvas.Children.Add(rect);
        _boxen[name] = rect;

        var label = new TextBlock
        {
            Text = name,
            FontWeight = FontWeights.Bold,
            Width = BoxBreite,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y + 6);
        ModellCanvas.Children.Add(label);

        Knoten k = _anlage.Knoten.First(n => n.Name == name);
        var untertitel = new TextBlock
        {
            Text = FormatiereEigenschaften(k),
            FontSize = 10,
            Foreground = Brushes.DimGray,
            Width = BoxBreite,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(untertitel, x);
        Canvas.SetTop(untertitel, y + BoxHoehe - 20);
        ModellCanvas.Children.Add(untertitel);
        _eigenschaften[name] = untertitel;
    }

    private static string FormatiereEigenschaften(Knoten k) =>
        $"µ={k.Mu:0.##}  σ={k.Sigma:0.##}";

    private void MarkiereQuelle(Point p)
    {
        var rahmen = new Rectangle
        {
            Width = BoxBreite,
            Height = BoxHoehe,
            RadiusX = 6,
            RadiusY = 6,
            Stroke = Brushes.SeaGreen,
            StrokeThickness = 3,
            Fill = Brushes.Transparent
        };
        Canvas.SetLeft(rahmen, p.X);
        Canvas.SetTop(rahmen, p.Y);
        ModellCanvas.Children.Add(rahmen);
    }

    private void ZeichnePfeil(Point von, Point nach)
    {
        double x1 = von.X + BoxBreite;
        double y1 = von.Y + BoxHoehe / 2;
        double x2 = nach.X;
        double y2 = nach.Y + BoxHoehe / 2;

        var linie = new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = Brushes.Black,
            StrokeThickness = 1.5
        };
        ModellCanvas.Children.Add(linie);

        // Einfache Pfeilspitze am Ziel.
        double winkel = Math.Atan2(y2 - y1, x2 - x1);
        const double laenge = 10;
        var p1 = new Point(x2 - laenge * Math.Cos(winkel - Math.PI / 6),
                           y2 - laenge * Math.Sin(winkel - Math.PI / 6));
        var p2 = new Point(x2 - laenge * Math.Cos(winkel + Math.PI / 6),
                           y2 - laenge * Math.Sin(winkel + Math.PI / 6));
        var spitze = new Polygon
        {
            Points = new PointCollection { new Point(x2, y2), p1, p2 },
            Fill = Brushes.Black
        };
        ModellCanvas.Children.Add(spitze);
    }

    // ---- Hilfen ----
    private static bool TryParseZahl(string s, out double wert) =>
        double.TryParse(s.Replace(',', '.'), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out wert);

    private void Melde(string text) => TxtErgebnis.Text = text;
}
