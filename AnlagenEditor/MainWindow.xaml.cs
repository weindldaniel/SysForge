using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Anlagensimulation;
using Microsoft.Win32;

namespace AnlageEditor;

public partial class MainWindow : Window
{
    private readonly Anlage _anlage = new();
    private readonly Dictionary<string, Point> _positionen = new();       // Name -> Boxposition
    private readonly Dictionary<string, Size> _groessen = new();          // Name -> Boxgroesse (mit Maus aenderbar)
    private readonly Dictionary<string, Rectangle> _boxen = new();        // Name -> Rechteck (fuer Auswahl)
    private readonly Dictionary<string, Polygon> _griffe = new();         // Name -> Groessengriff (Ecke unten rechts)
    private readonly Dictionary<string, TextBlock> _beschriftungen = new(); // Name -> Namensbeschriftung
    private readonly Dictionary<string, TextBlock> _eigenschaften = new(); // Name -> Untertitel (µ/σ)
    private readonly List<Verbindung> _verbindungen = new();
    private readonly HashSet<string> _quellenNamen = new();               // Namen der aktuellen Quellen
    private List<IReadOnlyDictionary<string, double[]>> _laeufe = new();  // Stationszeiten je Lauf (Details-Ansicht)
    private const int MaxDetailEintraege = 50;   // Obergrenze fuer die Detail-Auflistung je Station und Lauf
    private readonly Dictionary<string, TextBox> _feldEditoren = new();   // Feldname -> Editor der aktuell gezeigten Systeminformationen
    private readonly Stack<AnlageDatei> _undoStack = new();
    private const int MaxUndoSchritte = 50;
    private const string DateiFilter = "SysForge-Anlage (*.sysforge)|*.sysforge|Alle Dateien (*.*)|*.*";
    private int _stationsIndex = 0;
    private string? _ausgewaehlt;    // aktuell selektierte Station (Modus Auswahl)
    private string? _verbindenVon;   // erste angeklickte Station (Modus Verbinden)
    private Modus _modus = Modus.Auswahl;

    // ---- Ziehen (Verschieben einer Station) ----
    private string? _ziehName;
    private Point _ziehStartMaus;
    private Point _ziehStartBox;
    private bool _hatGezogen;

    // ---- Groesse aendern (Umriss einer Station skalieren) ----
    private string? _groesseName;
    private Point _groesseStartMaus;
    private Size _groesseStartGroesse;
    private bool _hatGroesseGeaendert;

    // ---- Materialfluss-Animation (zeitbasiert, Issue #8) ----
    private readonly List<Ellipse> _materialTokens = new();
    private DispatcherTimer? _materialflussTimer;
    private bool _materialflussLaeuft;
    private const double MillisekundenProZeiteinheit = 300; // Skalierung: Erwartungswert (µ) -> Verweildauer
    private const double MinVerweildauerMs = 400;
    private const double MaxVerweildauerMs = 4000;
    private const double UebergangsMillisekunden = 400;     // Fahrzeit zwischen zwei Stationen
    private const double SpawnIntervallMs = 900;             // Abstand neuer Materialwellen ab den Quellen
    private const int MaxAktiveToken = 300;                  // Schutz vor Explosion bei sehr kurzen Verweildauern
    private const int MaxFaeden = 200;   // Schutz vor Explosion bei stark verzweigten/zyklischen Graphen

    private enum Modus { Auswahl, Verbinden, Quelle }

    private sealed record Verbindung(string Von, string Nach, Line Linie, Polygon Spitze);

    private const double BoxBreite = 90;   // Standardbreite einer neuen Station
    private const double BoxHoehe = 56;    // Standardhoehe einer neuen Station
    private const double MinBoxBreite = 60;
    private const double MinBoxHoehe = 40;
    private const double GriffGroesse = 14; // Kantenlaenge des Groessengriffs unten rechts
    private const double StandardMu = 5.0;
    private const double StandardSigma = 1.0;

    // ---- Farbpalette (Farbgebung Anwendung: RAL 3002 / RAL 9006 / RAL 7024) ----
    private static readonly Brush FarbeBoxFuellung = Brushes.White;
    private static readonly Brush FarbeBoxRand = new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4));
    private static readonly Brush FarbeAkzent = new SolidColorBrush(Color.FromRgb(0xB2, 0x0B, 0x10));
    private static readonly Brush FarbeQuelleFuellung = new SolidColorBrush(Color.FromRgb(0xF7, 0xE3, 0xE3));
    private static readonly Brush FarbeTextPrimaer = new SolidColorBrush(Color.FromRgb(0x45, 0x49, 0x4E));
    private static readonly Brush FarbeTextSekundaer = new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x78));
    private static readonly Brush FarbePfeil = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));

    public MainWindow()
    {
        InitializeComponent();
        RbModusAuswahl.IsChecked = true;   // loest Modus_Checked aus (Namen sind jetzt initialisiert)
    }

    // ---- Rueckgaengig (Strg+Z) ----
    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Rueckgaengig();
            e.Handled = true;
        }
    }

    private void Rueckgaengig_Click(object sender, RoutedEventArgs e) => Rueckgaengig();

    /// <summary>Sichert den aktuellen Zustand, bevor eine mutierende Aktion ausgeführt wird.</summary>
    private void SichereFuerUndo()
    {
        _undoStack.Push(ErstelleAnlageDatei());
        if (_undoStack.Count <= MaxUndoSchritte) return;

        var uebrig = _undoStack.Reverse().Skip(1).Reverse().ToList();   // aeltesten Eintrag verwerfen
        _undoStack.Clear();
        foreach (AnlageDatei s in uebrig) _undoStack.Push(s);
    }

    /// <summary>
    /// Erfasst den gesamten bearbeitbaren Zustand (Stationen inkl. Position/Systeminformationen,
    /// Verbindungen, Quellen, Ziel/Level/Meta) — Grundlage sowohl fuer Undo als auch fuer Speichern.
    /// </summary>
    private AnlageDatei ErstelleAnlageDatei()
    {
        var datei = new AnlageDatei
        {
            Ziel = _anlage.Ziel,
            Level = _anlage.Level,
            Meta = KopiereMeta(_anlage.Meta)
        };

        foreach (Knoten k in _anlage.Knoten)
        {
            datei.Stationen.Add(new StationDatei
            {
                Name = k.Name,
                Mu = k.Mu,
                Sigma = k.Sigma,
                X = _positionen[k.Name].X,
                Y = _positionen[k.Name].Y,
                Breite = _groessen[k.Name].Width,
                Hoehe = _groessen[k.Name].Height,
                IstQuelle = _quellenNamen.Contains(k.Name),
                Systeminformationen = new Dictionary<string, string>(k.Systeminformationen)
            });
        }

        foreach (Verbindung v in _verbindungen)
            datei.Verbindungen.Add(new VerbindungDatei { Von = v.Von, Nach = v.Nach });

        return datei;
    }

    private static SystemMetaInformationen KopiereMeta(SystemMetaInformationen m) => new()
    {
        Systembezeichnung = m.Systembezeichnung,
        Systemgrenzen = m.Systemgrenzen,
        Eingangsgroessen = m.Eingangsgroessen,
        Ausgangsgroessen = m.Ausgangsgroessen,
        AblaufstrukturUebergeordnet = m.AblaufstrukturUebergeordnet,
        Bauteile = m.Bauteile,
        Systemklassifikation = m.Systemklassifikation,
        AnnahmenVereinfachungen = m.AnnahmenVereinfachungen,
        Produktionsplan = m.Produktionsplan,
        MtbfMttr = m.MtbfMttr
    };

    private void Rueckgaengig()
    {
        if (_undoStack.Count == 0) { Melde("Nichts zum Rückgängigmachen."); return; }

        AnlageDatei datei = _undoStack.Pop();
        AllesLoeschen();
        WendeAnlageDateiAn(datei);

        Melde("Letzte Änderung rückgängig gemacht.");
    }

    /// <summary>
    /// Baut Canvas und Anlage aus einer <see cref="AnlageDatei"/> neu auf (Undo und Laden aus
    /// Datei). Erwartet ein bereits geleertes Canvas (siehe <see cref="AllesLoeschen"/>).
    /// </summary>
    private void WendeAnlageDateiAn(AnlageDatei datei)
    {
        foreach (StationDatei s in datei.Stationen)
        {
            _anlage.FuegeStationHinzu(s.Name, s.Mu, s.Sigma);
            Knoten k = _anlage.Knoten.First(n => n.Name == s.Name);
            foreach ((string feld, string wert) in s.Systeminformationen) k.Systeminformationen[feld] = wert;

            _positionen[s.Name] = new Point(s.X, s.Y);
            _groessen[s.Name] = new Size(
                s.Breite > 0 ? s.Breite : BoxBreite,
                s.Hoehe > 0 ? s.Hoehe : BoxHoehe);
            ZeichneBox(s.Name, s.X, s.Y);
            if (s.IstQuelle) _quellenNamen.Add(s.Name);
        }

        foreach (VerbindungDatei v in datei.Verbindungen)
        {
            _anlage.Verbinde(v.Von, v.Nach);
            ZeichnePfeil(v.Von, v.Nach);
        }

        _anlage.SetzeQuellen(_quellenNamen.ToArray());
        foreach (string q in _quellenNamen)
            if (_boxen.TryGetValue(q, out Rectangle? box)) box.Fill = FarbeQuelleFuellung;

        _anlage.Ziel = datei.Ziel;
        _anlage.Level = datei.Level;
        _anlage.Meta = datei.Meta;
        AktualisiereSystemInfo();
    }

    // ---- Speichern / Laden ----
    private static readonly JsonSerializerOptions JsonOptionen = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private void Speichern_Click(object sender, RoutedEventArgs e)
    {
        if (_anlage.Knoten.Count == 0) { Melde("Kein System zum Speichern vorhanden."); return; }

        string vorschlag = string.IsNullOrWhiteSpace(_anlage.Meta.Systembezeichnung)
            ? "Anlage"
            : string.Join("_", _anlage.Meta.Systembezeichnung.Split(System.IO.Path.GetInvalidFileNameChars()));

        var dialog = new SaveFileDialog { Filter = DateiFilter, DefaultExt = ".sysforge", FileName = vorschlag };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            AnlageDatei datei = ErstelleAnlageDatei();
            string json = JsonSerializer.Serialize(datei, JsonOptionen);
            File.WriteAllText(dialog.FileName, json);
            Melde($"System gespeichert: {System.IO.Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            Melde($"Speichern fehlgeschlagen: {ex.Message}");
        }
    }

    private void Laden_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = DateiFilter };
        if (dialog.ShowDialog(this) != true) return;

        AnlageDatei? datei;
        try
        {
            string json = File.ReadAllText(dialog.FileName);
            datei = JsonSerializer.Deserialize<AnlageDatei>(json, JsonOptionen);
        }
        catch (Exception ex)
        {
            Melde($"Laden fehlgeschlagen: {ex.Message}");
            return;
        }

        if (datei is null) { Melde("Datei enthält kein gültiges System."); return; }

        SichereFuerUndo();
        AllesLoeschen();
        WendeAnlageDateiAn(datei);
        Melde($"System geladen: {System.IO.Path.GetFileName(dialog.FileName)}");
    }

    // ---- Neues System (Zielbeschreibung -> Aufgabendefinition -> Meta-Systeminformationen) ----
    private void NeuesSystemAnlegen_Click(object sender, RoutedEventArgs e)
    {
        // Issue #5: Systemdaten zuerst (Meta), Ziel/Level erst spaeter ueber "Systeminformation erhalten".
        var wizard = new NeuesSystemWizard(WizardModus.NurMeta) { Owner = this };
        if (wizard.ShowDialog() != true) return;

        SichereFuerUndo();
        AllesLoeschen();
        _anlage.Meta = wizard.Meta;

        AktualisiereSystemInfo();
        Melde($"System \"{_anlage.Meta.Systembezeichnung}\" angelegt. Jetzt Stationen hinzufügen, dann bei Bedarf „Systeminformation erhalten“.");
    }

    /// <summary>
    /// Fragt Ziel und Level fuer das bestehende System ab (Issue #5: "Systeminformation erhalten").
    /// Prueft anschliessend, ob bei den bereits angelegten Stationen noch levelabhaengige
    /// Systeminformationen fehlen, und weist den Benutzer ggf. darauf hin.
    /// </summary>
    private void SystemInformationErhalten_Click(object sender, RoutedEventArgs e)
    {
        var wizard = new NeuesSystemWizard(WizardModus.NurZielUndLevel, _anlage.Ziel, _anlage.Level) { Owner = this };
        if (wizard.ShowDialog() != true) return;

        SichereFuerUndo();
        _anlage.Ziel = wizard.GewaehltesZiel;
        _anlage.Level = wizard.GewaehltesLevel;
        AktualisiereSystemInfo();

        if (_ausgewaehlt is not null)
            BefuelleSystemelementInfo(_anlage.Knoten.First(n => n.Name == _ausgewaehlt));

        int level = _anlage.Level ?? 3;
        IReadOnlyList<string> benoetigt = SystemelementFelder.FelderFuerLevel(level);
        var fehlend = new List<string>();
        foreach (Knoten k in _anlage.Knoten)
        {
            var fehlendeFelder = benoetigt
                .Where(feld => !k.Systeminformationen.TryGetValue(feld, out string? wert) || string.IsNullOrWhiteSpace(wert))
                .ToList();
            if (fehlendeFelder.Count > 0)
                fehlend.Add($"{k.Name}: {string.Join(", ", fehlendeFelder)}");
        }

        if (fehlend.Count > 0)
        {
            MessageBox.Show(this,
                "Für folgende Stationen fehlen noch Systeminformationen (Level " + level + "):\n\n" + string.Join("\n", fehlend),
                "Systeminformationen unvollständig");
            Melde($"Ziel/Level übernommen. {fehlend.Count} Station(en) benötigen noch Systeminformationen.");
        }
        else
        {
            Melde("Ziel/Level übernommen. Alle Systeminformationen vollständig.");
        }
    }

    /// <summary>
    /// Baut den Header über dem Canvas neu auf: Systembezeichnung, Ziel/Level und die
    /// Meta-Systeminformationen (Kapitel 4.1.5) als beschriftete Feld-Kacheln.
    /// </summary>
    private void AktualisiereSystemInfo()
    {
        PanelHeaderFelder.Children.Clear();

        SystemMetaInformationen meta = _anlage.Meta;
        bool systemVorhanden = _anlage.Ziel is not null || _anlage.Level is not null
            || !string.IsNullOrWhiteSpace(meta.Systembezeichnung) || _anlage.Knoten.Count > 0;

        if (!systemVorhanden)
        {
            TxtHeaderTitel.Text = "Kein System angelegt";
            TxtHeaderZielLevel.Text = "";
            TxtHeaderHinweis.Visibility = Visibility.Visible;
            return;
        }

        TxtHeaderHinweis.Visibility = Visibility.Collapsed;

        TxtHeaderTitel.Text = string.IsNullOrWhiteSpace(meta.Systembezeichnung)
            ? "(ohne Bezeichnung)"
            : meta.Systembezeichnung;

        if (_anlage.Ziel is null || _anlage.Level is null)
        {
            TxtHeaderZielLevel.Text = "Ziel/Level noch nicht gewählt";
        }
        else
        {
            string ziel = _anlage.Ziel switch
            {
                Zielkategorie.Produktionsplanung => "Produktionsplanung",
                Zielkategorie.ProofOfConcept => "Proof of Concept",
                Zielkategorie.Rendering => "Rendering",
                _ => "?"
            };
            TxtHeaderZielLevel.Text = $"{ziel} · Level {_anlage.Level}";
        }

        void Feld(string label, string wert)
        {
            if (string.IsNullOrWhiteSpace(wert)) return;

            var kachel = new StackPanel { Width = 320, Margin = new Thickness(0, 0, 16, 10) };
            kachel.Children.Add(new TextBlock
            {
                Text = label, FontSize = 11, Foreground = FarbeTextSekundaer, Margin = new Thickness(0, 0, 0, 2)
            });
            kachel.Children.Add(new TextBlock
            {
                Text = wert, FontSize = 12, Foreground = FarbeTextPrimaer, TextWrapping = TextWrapping.Wrap
            });
            PanelHeaderFelder.Children.Add(kachel);
        }

        Feld("Systemgrenzen", meta.Systemgrenzen);
        Feld("Eingangsgrößen", meta.Eingangsgroessen);
        Feld("Ausgangsgrößen", meta.Ausgangsgroessen);
        Feld("Ablaufstruktur (übergeordnet)", meta.AblaufstrukturUebergeordnet);
        Feld("Bauteile", meta.Bauteile);
        Feld("Systemklassifikation", meta.Systemklassifikation);
        Feld("Annahmen / Vereinfachungen", meta.AnnahmenVereinfachungen);
        if (_anlage.Level is 1 or 2)
        {
            Feld("Produktionsplan", meta.Produktionsplan);
            Feld("MTBF & MTTR Zeiten", meta.MtbfMttr);
        }
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
        SichereFuerUndo();
        _anlage.Verbinde(von, name);
        ZeichnePfeil(von, name);
        AktualisiereBoxRand(von);
        _verbindenVon = null;
        Melde($"Verbindung {von} → {name} gesetzt.");
    }

    private void KlickQuelle(string name)
    {
        SichereFuerUndo();
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

        SichereFuerUndo();
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
        BefuelleSystemelementInfo(k);
        PropPanel.IsEnabled = true;
    }

    /// <summary>
    /// Baut die Systeminformationen-Editoren fuer die gewaehlte Station passend zum Level des
    /// aktuellen Systems auf (siehe SystemelementFelder). Ohne aktives System wird Level 3
    /// (Taktzeit-Level) als Standard angenommen, damit die Felder auch ohne Wizard nutzbar sind.
    /// </summary>
    private void BefuelleSystemelementInfo(Knoten k)
    {
        PanelSystemelementInfo.Children.Clear();
        _feldEditoren.Clear();

        int level = _anlage.Level ?? 3;
        IReadOnlyList<string> felder = SystemelementFelder.FelderFuerLevel(level);

        if (felder.Count == 0)
        {
            PanelSystemelementInfo.Children.Add(new TextBlock
            {
                Text = "Für Level 1–2 werden keine Systemelement-Daten je Station erfasst.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = FarbeTextSekundaer,
                Margin = new Thickness(0, 0, 0, 10)
            });
            return;
        }

        foreach (string feld in felder)
        {
            PanelSystemelementInfo.Children.Add(new TextBlock
            {
                Text = feld,
                FontSize = 11,
                Foreground = FarbeTextSekundaer,
                Margin = new Thickness(0, 0, 0, 3)
            });

            var editor = new TextBox
            {
                Text = k.Systeminformationen.GetValueOrDefault(feld, ""),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                MinHeight = 28
            };
            PanelSystemelementInfo.Children.Add(editor);
            _feldEditoren[feld] = editor;
        }
    }

    private void PropUebernehmen_Click(object sender, RoutedEventArgs e)
    {
        if (_ausgewaehlt is null) { Melde("Keine Station ausgewählt."); return; }
        if (!TryParseZahl(TxtPropMu.Text, out double mu)) { Melde("µ ist keine Zahl."); return; }
        if (!TryParseZahl(TxtPropSigma.Text, out double sigma)) { Melde("σ ist keine Zahl."); return; }

        string neuerName = TxtPropName.Text.Trim();
        if (string.IsNullOrWhiteSpace(neuerName)) { Melde("Name darf nicht leer sein."); return; }

        SichereFuerUndo();

        string alterName = _ausgewaehlt;
        if (neuerName != alterName)
        {
            if (!UmbenenneStationUI(alterName, neuerName))
            {
                _undoStack.Pop();   // Schnappschuss verwerfen, da nichts geaendert wurde
                Melde($"Name \"{neuerName}\" ist bereits vergeben.");
                return;
            }
            _ausgewaehlt = neuerName;
        }

        Knoten k = _anlage.Knoten.First(n => n.Name == _ausgewaehlt);
        k.Mu = mu;
        k.Sigma = sigma;

        foreach ((string feld, TextBox editor) in _feldEditoren)
            k.Systeminformationen[feld] = editor.Text.Trim();

        _eigenschaften[_ausgewaehlt].Text = FormatiereEigenschaften(k);
        Melde($"Station {_ausgewaehlt}: µ={mu}, σ={sigma} und Systeminformationen übernommen.");
    }

    /// <summary>
    /// Benennt eine Station in Anlage, Canvas und allen namensindizierten Sammlungen um.
    /// Liefert false, wenn der neue Name bereits vergeben ist; dann bleibt alles unveraendert.
    /// </summary>
    private bool UmbenenneStationUI(string alterName, string neuerName)
    {
        if (_boxen.ContainsKey(neuerName)) return false;
        if (!_anlage.UmbenenneStation(alterName, neuerName)) return false;

        Rectangle box = _boxen[alterName];
        box.Tag = neuerName;
        _boxen.Remove(alterName);
        _boxen[neuerName] = box;

        TextBlock label = _beschriftungen[alterName];
        label.Text = neuerName;
        _beschriftungen.Remove(alterName);
        _beschriftungen[neuerName] = label;

        _eigenschaften[neuerName] = _eigenschaften[alterName];
        _eigenschaften.Remove(alterName);

        Polygon griff = _griffe[alterName];
        griff.Tag = neuerName;
        _griffe.Remove(alterName);
        _griffe[neuerName] = griff;

        _groessen[neuerName] = _groessen[alterName];
        _groessen.Remove(alterName);

        _positionen[neuerName] = _positionen[alterName];
        _positionen.Remove(alterName);

        if (_quellenNamen.Remove(alterName)) _quellenNamen.Add(neuerName);
        if (_verbindenVon == alterName) _verbindenVon = neuerName;

        for (int i = 0; i < _verbindungen.Count; i++)
        {
            Verbindung v = _verbindungen[i];
            if (v.Von != alterName && v.Nach != alterName) continue;
            _verbindungen[i] = v with
            {
                Von = v.Von == alterName ? neuerName : v.Von,
                Nach = v.Nach == alterName ? neuerName : v.Nach
            };
        }

        return true;
    }

    private void StationLoeschen_Click(object sender, RoutedEventArgs e)
    {
        if (_ausgewaehlt is null) { Melde("Keine Station ausgewählt."); return; }
        string name = _ausgewaehlt;
        SichereFuerUndo();
        EntferneStationAusModell(name);

        _ausgewaehlt = null;
        PropPanel.IsEnabled = false;
        TxtPropName.Text = TxtPropMu.Text = TxtPropSigma.Text = string.Empty;
        PanelSystemelementInfo.Children.Clear();
        _feldEditoren.Clear();

        Melde($"Station {name} gelöscht.");
    }

    /// <summary>Entfernt eine Station samt ihrer Verbindungen aus Canvas, UI-Zustand und Anlage.</summary>
    private void EntferneStationAusModell(string name)
    {
        foreach (Verbindung v in _verbindungen.Where(v => v.Von == name || v.Nach == name).ToList())
        {
            ModellCanvas.Children.Remove(v.Linie);
            ModellCanvas.Children.Remove(v.Spitze);
            _verbindungen.Remove(v);
        }

        ModellCanvas.Children.Remove(_boxen[name]);
        ModellCanvas.Children.Remove(_beschriftungen[name]);
        ModellCanvas.Children.Remove(_eigenschaften[name]);
        ModellCanvas.Children.Remove(_griffe[name]);

        _boxen.Remove(name);
        _beschriftungen.Remove(name);
        _eigenschaften.Remove(name);
        _griffe.Remove(name);
        _positionen.Remove(name);
        _groessen.Remove(name);
        _quellenNamen.Remove(name);
        _anlage.EntferneStation(name);
    }

    /// <summary>Leert Canvas und Anlage vollständig, z. B. vor dem Laden einer Beispielanlage.</summary>
    private void AllesLoeschen()
    {
        foreach (string name in _boxen.Keys.ToList())
            EntferneStationAusModell(name);

        MaterialflussStoppen(melden: false);

        _ausgewaehlt = null;
        _verbindenVon = null;
        PropPanel.IsEnabled = false;
        TxtPropName.Text = TxtPropMu.Text = TxtPropSigma.Text = string.Empty;
        PanelSystemelementInfo.Children.Clear();
        _feldEditoren.Clear();
    }

    /// <summary>
    /// Baut die fiktive Anlage aus Kapitel 4.3 (siehe Anlagensimulation.FiktiveAnlage,
    /// dieselbe Definition wie in Anlagensimulation/Program.cs) auf dem Canvas auf und
    /// stößt anschließend direkt eine Simulation mit den aktuellen Versuchsparametern an.
    /// </summary>
    private void FiktiveAnlageLaden_Click(object sender, RoutedEventArgs e)
    {
        SichereFuerUndo();
        AllesLoeschen();

        Anlage vorlage = FiktiveAnlage.Erstellen();
        string[] namen = FiktiveAnlage.Reihenfolge;
        const double startX = 40;
        const double abstandX = BoxBreite + 60;
        const double y = 220;

        _anlage.Ziel = vorlage.Ziel;
        _anlage.Level = vorlage.Level;
        _anlage.Meta = vorlage.Meta;

        for (int i = 0; i < namen.Length; i++)
        {
            Knoten k = vorlage.Knoten.First(n => n.Name == namen[i]);
            double x = startX + i * abstandX;

            _anlage.FuegeStationHinzu(k.Name, k.Mu, k.Sigma);
            Knoten neu = _anlage.Knoten.First(n => n.Name == k.Name);
            foreach ((string feld, string wert) in k.Systeminformationen)
                neu.Systeminformationen[feld] = wert;

            _positionen[k.Name] = new Point(x, y);
            ZeichneBox(k.Name, x, y);
        }

        for (int i = 0; i < namen.Length - 1; i++)
        {
            _anlage.Verbinde(namen[i], namen[i + 1]);
            ZeichnePfeil(namen[i], namen[i + 1]);
        }

        _quellenNamen.Add("Quelle");
        _boxen["Quelle"].Fill = FarbeQuelleFuellung;
        _anlage.SetzeQuellen(_quellenNamen.ToArray());

        AktualisiereSystemInfo();
        Simulieren_Click(sender, e);
    }

    // ---- Verschieben einer Station ----
    private void StationMouseDown(string name, Rectangle rect, MouseButtonEventArgs e)
    {
        _ziehName = name;
        _hatGezogen = false;
        _ziehStartMaus = e.GetPosition(ModellCanvas);
        _ziehStartBox = _positionen[name];
        SichereFuerUndo();   // wird in StationMouseUp verworfen, falls gar nicht gezogen wurde
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
        else
        {
            if (_undoStack.Count > 0) _undoStack.Pop();   // kein tatsaechliches Ziehen -> Schnappschuss verwerfen
            StationKlick(name);
        }
    }

    private void VersetzeBox(string name, double x, double y)
    {
        _positionen[name] = new Point(x, y);
        AktualisiereBoxLayout(name);

        foreach (Verbindung v in _verbindungen)
            if (v.Von == name || v.Nach == name)
                AktualisierePfeil(v);
    }

    // ---- Groesse aendern (Umriss ueber den Griff unten rechts skalieren) ----
    private void GriffMouseDown(string name, Polygon griff, MouseButtonEventArgs e)
    {
        _groesseName = name;
        _hatGroesseGeaendert = false;
        _groesseStartMaus = e.GetPosition(ModellCanvas);
        _groesseStartGroesse = _groessen[name];
        SichereFuerUndo();   // wird in GriffMouseUp verworfen, falls gar nicht skaliert wurde
        griff.CaptureMouse();
        e.Handled = true;
    }

    private void GriffMouseMove(string name, MouseEventArgs e)
    {
        if (_groesseName != name || e.LeftButton != MouseButtonState.Pressed) return;

        Vector delta = e.GetPosition(ModellCanvas) - _groesseStartMaus;
        if (!_hatGroesseGeaendert && delta.Length < 4) return;
        _hatGroesseGeaendert = true;

        double breite = Math.Max(MinBoxBreite, _groesseStartGroesse.Width + delta.X);
        double hoehe = Math.Max(MinBoxHoehe, _groesseStartGroesse.Height + delta.Y);
        _groessen[name] = new Size(breite, hoehe);

        AktualisiereBoxLayout(name);
        foreach (Verbindung v in _verbindungen)
            if (v.Von == name || v.Nach == name)
                AktualisierePfeil(v);
    }

    private void GriffMouseUp(string name, Polygon griff, MouseButtonEventArgs e)
    {
        if (_groesseName != name) return;
        griff.ReleaseMouseCapture();
        _groesseName = null;
        e.Handled = true;

        if (_hatGroesseGeaendert) Melde($"Station {name} in der Größe angepasst.");
        else if (_undoStack.Count > 0) _undoStack.Pop();   // nicht skaliert -> Schnappschuss verwerfen
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
        var stationszeiten = new Dictionary<string, List<double>>();
        var laeufe = new List<IReadOnlyDictionary<string, double[]>>(R);

        for (int r = 0; r < R; r++)
        {
            Laufergebnis lauf = sim.SimuliereLauf(K, rng);
            double[] takt = Simulation.Taktzeiten(lauf.Abgaenge);

            double summe = 0.0;
            for (int i = warmup; i < takt.Length; i++) summe += takt[i];
            laufMittel[r] = summe / (takt.Length - warmup);

            foreach ((string name, double[] werte) in lauf.Stationszeiten)
            {
                if (!stationszeiten.TryGetValue(name, out List<double>? liste))
                {
                    liste = new List<double>();
                    stationszeiten[name] = liste;
                }
                liste.AddRange(werte);
            }

            laeufe.Add(lauf.Stationszeiten);
        }

        KonfidenzIntervall ki = Statistik.Konfidenzintervall(laufMittel, niveau);
        ZeigeErgebnis(stationszeiten, laeufe, ki, R, K, warmup, niveau);
        Melde($"Simulation abgeschlossen: Taktzeit {ki.Mittelwert:0.0000} " +
              $"[{ki.Untere:0.0000}; {ki.Obere:0.0000}] ({niveau:P0}).");
    }

    /// <summary>Stationsnamen in Fließreihenfolge (Canvas-Position von links nach rechts).</summary>
    private IEnumerable<string> StationenGeordnet(IEnumerable<string> namen) =>
        namen.OrderBy(name => _positionen.TryGetValue(name, out Point p) ? p.X : double.MaxValue);

    // ---- Ergebnis-Panel (Stufe 1: Stationszeiten, Stufe 2: Gesamtanlagen-Taktzeit) ----
    private void ZeigeErgebnis(
        Dictionary<string, List<double>> stationszeiten, List<IReadOnlyDictionary<string, double[]>> laeufe,
        KonfidenzIntervall ki, int R, int K, int warmup, double niveau)
    {
        TxtErgebnisHinweis.Visibility = Visibility.Collapsed;
        PanelStufe1.Visibility = Visibility.Visible;
        PanelStufe2.Visibility = Visibility.Visible;

        // Stufe 1: Stationszeiten in Fließreihenfolge (Canvas-Position von links nach rechts).
        PanelStationszeiten.Children.Clear();
        foreach (string name in StationenGeordnet(stationszeiten.Keys))
        {
            (double mittelwert, double std) = Statistik.Kennzahlen(stationszeiten[name]);

            var zeile = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            zeile.Children.Add(new TextBlock
            {
                Text = name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = FarbeTextPrimaer
            });
            zeile.Children.Add(new TextBlock
            {
                Text = $"⌀ Zykluszeit {mittelwert:0.00}   σ {std:0.00}   (n={stationszeiten[name].Count})",
                FontSize = 11,
                Foreground = FarbeTextSekundaer
            });
            PanelStationszeiten.Children.Add(zeile);
        }

        // Stufe 2: Gesamtanlagen-Taktzeit inkl. Konfidenzintervall.
        PanelGesamtergebnis.Children.Clear();
        void Zeile(string bezeichnung, string wert)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock { Text = bezeichnung, FontSize = 12, Foreground = FarbeTextSekundaer };
            var wertblock = new TextBlock { Text = wert, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = FarbeTextPrimaer };
            Grid.SetColumn(wertblock, 1);

            grid.Children.Add(label);
            grid.Children.Add(wertblock);
            PanelGesamtergebnis.Children.Add(grid);
        }

        Zeile("Wiederholungen R", R.ToString());
        Zeile("Teile je Lauf K", K.ToString());
        Zeile("Warm-up (verworfen)", warmup.ToString());
        Zeile("Konfidenzniveau", niveau.ToString("P0"));
        Zeile("Mittlere Taktzeit", ki.Mittelwert.ToString("0.0000"));
        Zeile("Konfidenzintervall", $"[{ki.Untere:0.0000}; {ki.Obere:0.0000}]");
        Zeile("Halbbreite", ki.Halbbreite.ToString("0.0000"));

        // Details: je Lauf durchklickbare, ungekürzte Stationszeiten (siehe BtnDetails_Click).
        _laeufe = laeufe;
        CmbLauf.SelectionChanged -= CmbLauf_SelectionChanged;
        CmbLauf.Items.Clear();
        for (int r = 1; r <= laeufe.Count; r++) CmbLauf.Items.Add($"Lauf {r}");
        CmbLauf.SelectionChanged += CmbLauf_SelectionChanged;
        if (CmbLauf.Items.Count > 0) CmbLauf.SelectedIndex = 0;
    }

    private void BtnDetails_Click(object sender, RoutedEventArgs e)
    {
        bool zeigen = PanelDetails.Visibility != Visibility.Visible;
        PanelDetails.Visibility = zeigen ? Visibility.Visible : Visibility.Collapsed;
        BtnDetails.Content = zeigen ? "Details ausblenden" : "Details anzeigen";
    }

    private void CmbLauf_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int index = CmbLauf.SelectedIndex;
        if (index < 0 || index >= _laeufe.Count) return;
        ZeigeLaufDetails(_laeufe[index]);
    }

    /// <summary>Listet je Station die realisierten Stationszeiten des gewählten Laufs auf (bis <see cref="MaxDetailEintraege"/>).</summary>
    private void ZeigeLaufDetails(IReadOnlyDictionary<string, double[]> laufDaten)
    {
        PanelLaufDetails.Children.Clear();

        foreach (string name in StationenGeordnet(laufDaten.Keys))
        {
            double[] werte = laufDaten[name];
            int anzahlGezeigt = Math.Min(werte.Length, MaxDetailEintraege);

            var zeilen = new System.Text.StringBuilder();
            for (int i = 0; i < anzahlGezeigt; i++)
                zeilen.AppendLine($"{i + 1,3}:  {werte[i]:0.00}");

            PanelLaufDetails.Children.Add(new TextBlock
            {
                Text = werte.Length > anzahlGezeigt
                    ? $"{name}  (erste {anzahlGezeigt} von {werte.Length})"
                    : $"{name}  ({werte.Length})",
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = FarbeTextPrimaer,
                Margin = new Thickness(0, 10, 0, 4)
            });
            PanelLaufDetails.Children.Add(new TextBlock
            {
                Text = zeilen.ToString().TrimEnd(),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Foreground = FarbeTextSekundaer
            });
        }
    }

    // ---- Materialfluss-Visualisierung (zeitbasiert: Verweildauer je Station gemaess µ,
    // fortlaufende Nachlieferung ab den Quellen bis zum Stoppen; ohne Bezug zum Simulationsbereich) ----
    private void MaterialflussAbspielen_Click(object sender, RoutedEventArgs e)
    {
        if (_materialflussLaeuft)
        {
            MaterialflussStoppen();
            return;
        }

        if (_anlage.Quellen.Count == 0) { Melde("Materialfluss: bitte zuerst eine Quelle setzen."); return; }

        _materialflussLaeuft = true;
        BtnMaterialfluss.Content = "⏹ Stoppen";
        Melde("Materialfluss gestartet: Werkstücke werden fortlaufend ab den Quellen nachgeliefert.");

        MaterialflussWelleErzeugen();
        _materialflussTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SpawnIntervallMs) };
        _materialflussTimer.Tick += (_, _) => MaterialflussWelleErzeugen();
        _materialflussTimer.Start();
    }

    /// <summary>Stoppt die laufende Materialfluss-Animation und entfernt alle aktiven Token.</summary>
    private void MaterialflussStoppen(bool melden = true)
    {
        _materialflussTimer?.Stop();
        _materialflussTimer = null;
        _materialflussLaeuft = false;
        BtnMaterialfluss.Content = "▶ Abspielen";

        foreach (Ellipse token in _materialTokens)
        {
            token.BeginAnimation(Canvas.LeftProperty, null);
            token.BeginAnimation(Canvas.TopProperty, null);
            ModellCanvas.Children.Remove(token);
        }
        _materialTokens.Clear();

        if (melden) Melde("Materialfluss gestoppt.");
    }

    /// <summary>
    /// Startet je aktuell moeglichem Pfad (ab jeder Quelle bis zu einer Senke bzw. bis zum
    /// Zyklusschutz) ein neues Werkstueck-Token. Wird von <see cref="_materialflussTimer"/>
    /// wiederholt aufgerufen, wodurch der Fluss fortlaufend mit neuem Material gespeist wird.
    /// </summary>
    private void MaterialflussWelleErzeugen()
    {
        foreach (List<string> faden in ErmittleAlleFaeden())
        {
            if (_materialTokens.Count >= MaxAktiveToken) return;   // Schutz vor Explosion
            if (faden.Count < 2) continue;   // Quelle ohne Nachfolger: nichts zu bewegen

            MaterialflussTokenStarten(faden);
        }
    }

    /// <summary>
    /// Animiert ein einzelnes Werkstueck-Token entlang eines Pfads: An jeder Station verweilt es
    /// so lange, wie deren Erwartungswert µ vorgibt (siehe <see cref="VerweildauerMs"/>), dazwischen
    /// wechselt es kurz zur naechsten Station. Entfernt sich selbst, sobald der Pfad endet.
    /// </summary>
    private void MaterialflussTokenStarten(List<string> faden)
    {
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
        double t = 0;

        for (int i = 0; i < faden.Count; i++)
        {
            Rect r = BoxRechteck(faden[i]);
            double mitteX = r.X + r.Width / 2 - token.Width / 2;
            double mitteY = r.Y + r.Height / 2 - token.Height / 2;

            if (i > 0) t += UebergangsMillisekunden;   // Fahrt von der vorherigen Station hierher
            KeyTime ankunft = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t));
            animX.KeyFrames.Add(new LinearDoubleKeyFrame(mitteX, ankunft));
            animY.KeyFrames.Add(new LinearDoubleKeyFrame(mitteY, ankunft));

            t += VerweildauerMs(faden[i]);   // Bearbeitungszeit (µ) dieser Station
            KeyTime abfahrt = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t));
            animX.KeyFrames.Add(new LinearDoubleKeyFrame(mitteX, abfahrt));
            animY.KeyFrames.Add(new LinearDoubleKeyFrame(mitteY, abfahrt));
        }

        animX.Completed += (_, _) => MaterialflussTokenEntfernen(token);
        token.BeginAnimation(Canvas.LeftProperty, animX);
        token.BeginAnimation(Canvas.TopProperty, animY);
    }

    private void MaterialflussTokenEntfernen(Ellipse token)
    {
        ModellCanvas.Children.Remove(token);
        _materialTokens.Remove(token);
    }

    /// <summary>Bildet den Erwartungswert (µ) einer Station rein visuell auf eine Animationsdauer ab.</summary>
    private double VerweildauerMs(string name)
    {
        Knoten k = _anlage.Knoten.First(n => n.Name == name);
        return Math.Clamp(k.Mu * MillisekundenProZeiteinheit, MinVerweildauerMs, MaxVerweildauerMs);
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
        if (!_groessen.ContainsKey(name)) _groessen[name] = new Size(BoxBreite, BoxHoehe);

        var rect = new Rectangle
        {
            RadiusX = 6,
            RadiusY = 6,
            Stroke = FarbeBoxRand,
            StrokeThickness = 1.5,
            Fill = FarbeBoxFuellung,
            Cursor = Cursors.Hand,
            Tag = name
        };
        // Tag statt geschlossenem "name" verwenden, damit Umbenennen (Tag wird aktualisiert) korrekt bleibt.
        rect.MouseLeftButtonDown += (s, e) => StationMouseDown((string)((Rectangle)s!).Tag, rect, e);
        rect.MouseMove += (s, e) => StationMouseMove((string)((Rectangle)s!).Tag, e);
        rect.MouseLeftButtonUp += (s, e) => StationMouseUp((string)((Rectangle)s!).Tag, rect, e);
        ModellCanvas.Children.Add(rect);
        _boxen[name] = rect;

        var label = new TextBlock
        {
            Text = name,
            FontWeight = FontWeights.SemiBold,
            Foreground = FarbeTextPrimaer,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false
        };
        ModellCanvas.Children.Add(label);
        _beschriftungen[name] = label;

        Knoten k = _anlage.Knoten.First(n => n.Name == name);
        var untertitel = new TextBlock
        {
            Text = FormatiereEigenschaften(k),
            FontSize = 10,
            Foreground = FarbeTextSekundaer,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false
        };
        ModellCanvas.Children.Add(untertitel);
        _eigenschaften[name] = untertitel;

        // Groessengriff (Ecke unten rechts) zum Skalieren des Umrisses mit der Maus.
        var griff = new Polygon
        {
            Fill = FarbePfeil,
            Cursor = Cursors.SizeNWSE,
            Tag = name
        };
        Panel.SetZIndex(griff, 20);
        griff.MouseLeftButtonDown += (s, e) => GriffMouseDown((string)((Polygon)s!).Tag, (Polygon)s!, e);
        griff.MouseMove += (s, e) => GriffMouseMove((string)((Polygon)s!).Tag, e);
        griff.MouseLeftButtonUp += (s, e) => GriffMouseUp((string)((Polygon)s!).Tag, (Polygon)s!, e);
        ModellCanvas.Children.Add(griff);
        _griffe[name] = griff;

        AktualisiereBoxLayout(name);
    }

    /// <summary>
    /// Positioniert Umriss, Beschriftung, Untertitel und Groessengriff einer Station gemaess
    /// aktueller Position und Groesse. Aufgerufen beim Zeichnen, Verschieben und Skalieren.
    /// </summary>
    private void AktualisiereBoxLayout(string name)
    {
        Point p = _positionen[name];
        Size g = _groessen[name];

        Rectangle box = _boxen[name];
        box.Width = g.Width;
        box.Height = g.Height;
        Canvas.SetLeft(box, p.X);
        Canvas.SetTop(box, p.Y);

        TextBlock label = _beschriftungen[name];
        label.Width = g.Width;
        Canvas.SetLeft(label, p.X);
        Canvas.SetTop(label, p.Y + 6);

        TextBlock untertitel = _eigenschaften[name];
        untertitel.Width = g.Width;
        Canvas.SetLeft(untertitel, p.X);
        Canvas.SetTop(untertitel, p.Y + g.Height - 20);

        Polygon griff = _griffe[name];
        double rechts = p.X + g.Width;
        double unten = p.Y + g.Height;
        griff.Points = new PointCollection
        {
            new Point(rechts - GriffGroesse, unten),
            new Point(rechts, unten - GriffGroesse),
            new Point(rechts, unten)
        };
    }

    private static string FormatiereEigenschaften(Knoten k) =>
        $"µ={k.Mu:0.##}  σ={k.Sigma:0.##}";

    private Rect BoxRechteck(string name) =>
        new(_positionen[name].X, _positionen[name].Y, _groessen[name].Width, _groessen[name].Height);

    private void ZeichnePfeil(string von, string nach)
    {
        var (x1, y1, x2, y2, spitzenPunkte) = PfeilGeometrie(BoxRechteck(von), BoxRechteck(nach));
        var linie = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = FarbePfeil, StrokeThickness = 1.5 };
        var spitze = new Polygon { Points = spitzenPunkte, Fill = FarbePfeil };
        ModellCanvas.Children.Add(linie);
        ModellCanvas.Children.Add(spitze);
        _verbindungen.Add(new Verbindung(von, nach, linie, spitze));
    }

    private void AktualisierePfeil(Verbindung v)
    {
        var (x1, y1, x2, y2, spitzenPunkte) = PfeilGeometrie(BoxRechteck(v.Von), BoxRechteck(v.Nach));
        v.Linie.X1 = x1; v.Linie.Y1 = y1; v.Linie.X2 = x2; v.Linie.Y2 = y2;
        v.Spitze.Points = spitzenPunkte;
    }

    /// <summary>
    /// Berechnet die Pfeilgeometrie zwischen zwei Stationsumrissen. Der Pfeil verlaeuft entlang
    /// der Verbindungslinie der beiden Boxmittelpunkte und wird an den Umrissen abgeschnitten,
    /// sodass er stets an der zugewandten Seite andockt und nie durch eine Station selbst fuehrt.
    /// </summary>
    private static (double x1, double y1, double x2, double y2, PointCollection spitze) PfeilGeometrie(Rect von, Rect nach)
    {
        var vonMitte = new Point(von.X + von.Width / 2, von.Y + von.Height / 2);
        var nachMitte = new Point(nach.X + nach.Width / 2, nach.Y + nach.Height / 2);

        Point start = SchnittMitRand(von, vonMitte, nachMitte);
        Point ende = SchnittMitRand(nach, nachMitte, vonMitte);

        // Pfeilspitze am Zielrand, ausgerichtet entlang der Verbindungsstrecke.
        double winkel = Math.Atan2(ende.Y - start.Y, ende.X - start.X);
        const double Laenge = 10;
        var p1 = new Point(ende.X - Laenge * Math.Cos(winkel - Math.PI / 6),
                           ende.Y - Laenge * Math.Sin(winkel - Math.PI / 6));
        var p2 = new Point(ende.X - Laenge * Math.Cos(winkel + Math.PI / 6),
                           ende.Y - Laenge * Math.Sin(winkel + Math.PI / 6));

        return (start.X, start.Y, ende.X, ende.Y, new PointCollection { ende, p1, p2 });
    }

    /// <summary>
    /// Liefert den Punkt auf dem Rand von <paramref name="box"/>, an dem die Strecke von
    /// <paramref name="mitte"/> in Richtung <paramref name="ziel"/> den Rand verlaesst.
    /// </summary>
    private static Point SchnittMitRand(Rect box, Point mitte, Point ziel)
    {
        double dx = ziel.X - mitte.X;
        double dy = ziel.Y - mitte.Y;
        if (dx == 0 && dy == 0) return mitte;

        double skala = double.PositiveInfinity;
        if (dx != 0) skala = Math.Min(skala, box.Width / 2 / Math.Abs(dx));
        if (dy != 0) skala = Math.Min(skala, box.Height / 2 / Math.Abs(dy));

        return new Point(mitte.X + dx * skala, mitte.Y + dy * skala);
    }

    // ---- Hilfen ----
    private static bool TryParseZahl(string s, out double wert) =>
        double.TryParse(s.Replace(',', '.'), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out wert);

    private void Melde(string text) => TxtErgebnis.Text = text;
}
