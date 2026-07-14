using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Anlagensimulation;

namespace AnlageEditor;

public partial class MainWindow : Window
{
    private readonly Anlage _anlage = new();
    private readonly Dictionary<string, Point> _positionen = new();       // Name -> Boxposition
    private readonly Dictionary<string, Rectangle> _boxen = new();        // Name -> Rechteck (fuer Auswahl)
    private readonly Dictionary<string, TextBlock> _beschriftungen = new(); // Name -> Namensbeschriftung
    private readonly Dictionary<string, TextBlock> _eigenschaften = new(); // Name -> Untertitel (µ/σ)
    private readonly List<Verbindung> _verbindungen = new();
    private readonly HashSet<string> _quellenNamen = new();               // Namen der aktuellen Quellen
    private int _stationsIndex = 0;
    private string? _ausgewaehlt;    // aktuell selektierte Station (Modus Auswahl)
    private string? _verbindenVon;   // erste angeklickte Station (Modus Verbinden)
    private Modus _modus = Modus.Auswahl;

    // ---- Ziehen (Verschieben einer Station) ----
    private string? _ziehName;
    private Point _ziehStartMaus;
    private Point _ziehStartBox;
    private bool _hatGezogen;

    // ---- Materialfluss-Animation ----
    private readonly List<Ellipse> _materialTokens = new();
    private const double MillisekundenProStation = 700;
    private const int MaxFaeden = 200;   // Schutz vor Explosion bei stark verzweigten/zyklischen Graphen

    private enum Modus { Auswahl, Verbinden, Quelle }

    private sealed record Verbindung(string Von, string Nach, Line Linie, Polygon Spitze);

    private const double BoxBreite = 90;
    private const double BoxHoehe = 56;
    private const double StandardMu = 5.0;
    private const double StandardSigma = 1.0;

    // ---- Farbpalette (hellrot / hellgrau / weiss) ----
    private static readonly Brush FarbeBoxFuellung = Brushes.White;
    private static readonly Brush FarbeBoxRand = new SolidColorBrush(Color.FromRgb(0xD8, 0xD2, 0xD2));
    private static readonly Brush FarbeAkzent = new SolidColorBrush(Color.FromRgb(0xE4, 0x73, 0x6B));
    private static readonly Brush FarbeQuelleFuellung = new SolidColorBrush(Color.FromRgb(0xFB, 0xEA, 0xE8));
    private static readonly Brush FarbeTextPrimaer = new SolidColorBrush(Color.FromRgb(0x3A, 0x35, 0x35));
    private static readonly Brush FarbeTextSekundaer = new SolidColorBrush(Color.FromRgb(0x94, 0x8C, 0x8C));
    private static readonly Brush FarbePfeil = new SolidColorBrush(Color.FromRgb(0xAE, 0xA6, 0xA6));

    public MainWindow()
    {
        InitializeComponent();
        RbModusAuswahl.IsChecked = true;   // loest Modus_Checked aus (Namen sind jetzt initialisiert)
    }

    // ---- Klickmodus ----
    private void Modus_Checked(object sender, RoutedEventArgs e)
    {
        if (_verbindenVon is not null)
        {
            AktualisiereBoxRand(_verbindenVon);
            _verbindenVon = null;
        }

        _modus = sender switch
        {
            _ when ReferenceEquals(sender, RbModusVerbinden) => Modus.Verbinden,
            _ when ReferenceEquals(sender, RbModusQuelle) => Modus.Quelle,
            _ => Modus.Auswahl
        };
        Melde(NachrichtFuerModus(_modus));
    }

    private static string NachrichtFuerModus(Modus modus) => modus switch
    {
        Modus.Verbinden => "Verbinden: Startstation anklicken, danach Zielstation.",
        Modus.Quelle => "Quelle: Station anklicken, um sie ein-/auszuschalten.",
        _ => "Auswählen: Station anklicken für Eigenschaften, ziehen zum Verschieben."
    };

    private void StationKlick(string name)
    {
        switch (_modus)
        {
            case Modus.Verbinden: KlickVerbinden(name); break;
            case Modus.Quelle: KlickQuelle(name); break;
            default: WaehleStation(name); break;
        }
    }

    private void KlickVerbinden(string name)
    {
        if (_verbindenVon is null)
        {
            _verbindenVon = name;
            _boxen[name].Stroke = FarbeAkzent;
            _boxen[name].StrokeThickness = 2.5;
            Melde($"Verbinden: {name} → ? (Zielstation anklicken)");
            return;
        }

        if (_verbindenVon == name)
        {
            AktualisiereBoxRand(name);
            _verbindenVon = null;
            Melde("Verbindung abgebrochen.");
            return;
        }

        string von = _verbindenVon;
        _anlage.Verbinde(von, name);
        ZeichnePfeil(von, name);
        AktualisiereBoxRand(von);
        _verbindenVon = null;
        Melde($"Verbindung {von} → {name} gesetzt.");
    }

    private void KlickQuelle(string name)
    {
        if (_quellenNamen.Contains(name))
        {
            _quellenNamen.Remove(name);
            _boxen[name].Fill = FarbeBoxFuellung;
        }
        else
        {
            _quellenNamen.Add(name);
            _boxen[name].Fill = FarbeQuelleFuellung;
        }

        _anlage.SetzeQuellen(_quellenNamen.ToArray());
        Melde(_quellenNamen.Count == 0
            ? "Keine Quelle gesetzt."
            : $"Quelle(n): {string.Join(", ", _quellenNamen)}");
    }

    private void AktualisiereBoxRand(string name)
    {
        Rectangle r = _boxen[name];
        bool istAusgewaehlt = name == _ausgewaehlt;
        r.Stroke = istAusgewaehlt ? FarbeAkzent : FarbeBoxRand;
        r.StrokeThickness = istAusgewaehlt ? 2.5 : 1.5;
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
            alt.Stroke = FarbeBoxRand;
            alt.StrokeThickness = 1.5;
        }

        _ausgewaehlt = name;
        Rectangle rechteck = _boxen[name];
        rechteck.Stroke = FarbeAkzent;
        rechteck.StrokeThickness = 2.5;

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

    private void StationLoeschen_Click(object sender, RoutedEventArgs e)
    {
        if (_ausgewaehlt is null) { Melde("Keine Station ausgewählt."); return; }
        string name = _ausgewaehlt;

        foreach (Verbindung v in _verbindungen.Where(v => v.Von == name || v.Nach == name).ToList())
        {
            ModellCanvas.Children.Remove(v.Linie);
            ModellCanvas.Children.Remove(v.Spitze);
            _verbindungen.Remove(v);
        }

        ModellCanvas.Children.Remove(_boxen[name]);
        ModellCanvas.Children.Remove(_beschriftungen[name]);
        ModellCanvas.Children.Remove(_eigenschaften[name]);

        _boxen.Remove(name);
        _beschriftungen.Remove(name);
        _eigenschaften.Remove(name);
        _positionen.Remove(name);
        _quellenNamen.Remove(name);
        _anlage.EntferneStation(name);

        _ausgewaehlt = null;
        PropPanel.IsEnabled = false;
        TxtPropName.Text = TxtPropMu.Text = TxtPropSigma.Text = string.Empty;

        Melde($"Station {name} gelöscht.");
    }

    // ---- Verschieben einer Station ----
    private void StationMouseDown(string name, Rectangle rect, MouseButtonEventArgs e)
    {
        _ziehName = name;
        _hatGezogen = false;
        _ziehStartMaus = e.GetPosition(ModellCanvas);
        _ziehStartBox = _positionen[name];
        rect.CaptureMouse();
        e.Handled = true;
    }

    private void StationMouseMove(string name, MouseEventArgs e)
    {
        if (_ziehName != name || e.LeftButton != MouseButtonState.Pressed) return;

        Vector delta = e.GetPosition(ModellCanvas) - _ziehStartMaus;
        if (!_hatGezogen && delta.Length < 4) return;
        _hatGezogen = true;

        double x = Math.Max(0, _ziehStartBox.X + delta.X);
        double y = Math.Max(0, _ziehStartBox.Y + delta.Y);
        VersetzeBox(name, x, y);
    }

    private void StationMouseUp(string name, Rectangle rect, MouseButtonEventArgs e)
    {
        if (_ziehName != name) return;
        rect.ReleaseMouseCapture();
        _ziehName = null;
        e.Handled = true;

        if (_hatGezogen) Melde($"Station {name} verschoben.");
        else StationKlick(name);
    }

    private void VersetzeBox(string name, double x, double y)
    {
        _positionen[name] = new Point(x, y);

        Canvas.SetLeft(_boxen[name], x);
        Canvas.SetTop(_boxen[name], y);
        Canvas.SetLeft(_beschriftungen[name], x);
        Canvas.SetTop(_beschriftungen[name], y + 6);
        Canvas.SetLeft(_eigenschaften[name], x);
        Canvas.SetTop(_eigenschaften[name], y + BoxHoehe - 20);

        foreach (Verbindung v in _verbindungen)
            if (v.Von == name || v.Nach == name)
                AktualisierePfeil(v);
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

    // ---- Materialfluss-Visualisierung ----
    private void MaterialflussAbspielen_Click(object sender, RoutedEventArgs e)
    {
        if (_anlage.Quellen.Count == 0) { Melde("Materialfluss: bitte zuerst eine Quelle setzen."); return; }

        List<List<string>> faeden = ErmittleAlleFaeden();

        foreach (Ellipse alterToken in _materialTokens) ModellCanvas.Children.Remove(alterToken);
        _materialTokens.Clear();

        int animiert = 0;
        foreach (List<string> faden in faeden)
        {
            if (faden.Count < 2) continue;   // Quelle ohne Nachfolger: nichts zu bewegen

            var token = new Ellipse
            {
                Width = 18,
                Height = 18,
                Fill = FarbeAkzent,
                Stroke = Brushes.White,
                StrokeThickness = 2,
                IsHitTestVisible = false
            };
            Panel.SetZIndex(token, 100);
            ModellCanvas.Children.Add(token);
            _materialTokens.Add(token);

            var animX = new DoubleAnimationUsingKeyFrames();
            var animY = new DoubleAnimationUsingKeyFrames();
            for (int i = 0; i < faden.Count; i++)
            {
                Point p = _positionen[faden[i]];
                double mitteX = p.X + BoxBreite / 2 - token.Width / 2;
                double mitteY = p.Y + BoxHoehe / 2 - token.Height / 2;
                KeyTime zeit = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * MillisekundenProStation));
                animX.KeyFrames.Add(new LinearDoubleKeyFrame(mitteX, zeit));
                animY.KeyFrames.Add(new LinearDoubleKeyFrame(mitteY, zeit));
            }

            token.BeginAnimation(Canvas.LeftProperty, animX);
            token.BeginAnimation(Canvas.TopProperty, animY);
            animiert++;
        }

        Melde(animiert == 0
            ? "Materialfluss: keine Quelle hat einen Nachfolger."
            : $"Materialfluss: {animiert} parallele(r) Pfad(e) animiert.");
    }

    /// <summary>
    /// Ermittelt ab jeder Quelle alle Pfade bis zur jeweiligen Senke; an Stationen mit
    /// mehreren Nachfolgern spaltet sich ein Pfad in mehrere auf (Verzweigung). Ein Pfad
    /// endet auch, sobald er eine Station ein zweites Mal erreichen wuerde (Zyklusschutz).
    /// </summary>
    private List<List<string>> ErmittleAlleFaeden()
    {
        var faeden = new List<List<string>>();
        foreach (Knoten quelle in _anlage.Quellen)
        {
            if (faeden.Count >= MaxFaeden) break;
            SammleFaeden(quelle, new List<string>(), faeden);
        }
        return faeden;
    }

    private static void SammleFaeden(Knoten aktuell, List<string> pfadBisher, List<List<string>> faeden)
    {
        if (faeden.Count >= MaxFaeden) return;

        if (pfadBisher.Contains(aktuell.Name))
        {
            faeden.Add(pfadBisher);
            return;
        }

        var pfad = new List<string>(pfadBisher) { aktuell.Name };

        if (aktuell.Nachfolger.Count == 0)
        {
            faeden.Add(pfad);
            return;
        }

        foreach (Knoten nachfolger in aktuell.Nachfolger)
            SammleFaeden(nachfolger, pfad, faeden);
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
            Stroke = FarbeBoxRand,
            StrokeThickness = 1.5,
            Fill = FarbeBoxFuellung,
            Cursor = Cursors.Hand,
            Tag = name
        };
        rect.MouseLeftButtonDown += (_, e) => StationMouseDown(name, rect, e);
        rect.MouseMove += (_, e) => StationMouseMove(name, e);
        rect.MouseLeftButtonUp += (_, e) => StationMouseUp(name, rect, e);
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        ModellCanvas.Children.Add(rect);
        _boxen[name] = rect;

        var label = new TextBlock
        {
            Text = name,
            FontWeight = FontWeights.SemiBold,
            Foreground = FarbeTextPrimaer,
            Width = BoxBreite,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y + 6);
        ModellCanvas.Children.Add(label);
        _beschriftungen[name] = label;

        Knoten k = _anlage.Knoten.First(n => n.Name == name);
        var untertitel = new TextBlock
        {
            Text = FormatiereEigenschaften(k),
            FontSize = 10,
            Foreground = FarbeTextSekundaer,
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

    private void ZeichnePfeil(string von, string nach)
    {
        var (x1, y1, x2, y2, spitzenPunkte) = PfeilGeometrie(_positionen[von], _positionen[nach]);
        var linie = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = FarbePfeil, StrokeThickness = 1.5 };
        var spitze = new Polygon { Points = spitzenPunkte, Fill = FarbePfeil };
        ModellCanvas.Children.Add(linie);
        ModellCanvas.Children.Add(spitze);
        _verbindungen.Add(new Verbindung(von, nach, linie, spitze));
    }

    private void AktualisierePfeil(Verbindung v)
    {
        var (x1, y1, x2, y2, spitzenPunkte) = PfeilGeometrie(_positionen[v.Von], _positionen[v.Nach]);
        v.Linie.X1 = x1; v.Linie.Y1 = y1; v.Linie.X2 = x2; v.Linie.Y2 = y2;
        v.Spitze.Points = spitzenPunkte;
    }

    private static (double x1, double y1, double x2, double y2, PointCollection spitze) PfeilGeometrie(Point von, Point nach)
    {
        double x1 = von.X + BoxBreite;
        double y1 = von.Y + BoxHoehe / 2;
        double x2 = nach.X;
        double y2 = nach.Y + BoxHoehe / 2;

        // Einfache Pfeilspitze am Ziel.
        double winkel = Math.Atan2(y2 - y1, x2 - x1);
        const double laenge = 10;
        var p1 = new Point(x2 - laenge * Math.Cos(winkel - Math.PI / 6),
                           y2 - laenge * Math.Sin(winkel - Math.PI / 6));
        var p2 = new Point(x2 - laenge * Math.Cos(winkel + Math.PI / 6),
                           y2 - laenge * Math.Sin(winkel + Math.PI / 6));

        return (x1, y1, x2, y2, new PointCollection { new Point(x2, y2), p1, p2 });
    }

    // ---- Hilfen ----
    private static bool TryParseZahl(string s, out double wert) =>
        double.TryParse(s.Replace(',', '.'), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out wert);

    private void Melde(string text) => TxtErgebnis.Text = text;
}
