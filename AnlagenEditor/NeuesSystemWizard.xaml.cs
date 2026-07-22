using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Anlagensimulation;

namespace AnlageEditor;

/// <summary>Welche Schritte des Wizards gezeigt werden (Issue #5: Reihenfolge optimieren).</summary>
public enum WizardModus
{
    /// <summary>Alle 3 Schritte: Zielbeschreibung -> Aufgabendefinition -> Meta-Systeminformationen.</summary>
    Alle,
    /// <summary>Nur Meta-Systeminformationen (Systemdaten zuerst anlegen, ohne Ziel/Level).</summary>
    NurMeta,
    /// <summary>Nur Zielbeschreibung + Aufgabendefinition ("Systeminformation erhalten").</summary>
    NurZielUndLevel
}

/// <summary>
/// Gefuehrter Dialog fuer Zielbeschreibung, Aufgabendefinition (Level) und Meta-Systeminformationen
/// nach Kapitel 4.1/4.2.3. Je nach <see cref="WizardModus"/> werden nur die dafuer relevanten
/// Schritte gezeigt (Issue #5: erst Systemdaten anlegen, Ziel/Level erst spaeter bei Bedarf ueber
/// "Systeminformation erhalten" abfragen). Bei DialogResult == true stehen die fuer den jeweiligen
/// Modus relevanten Werte (GewaehltesZiel/GewaehltesLevel und/oder Meta) befuellt bereit.
/// </summary>
public partial class NeuesSystemWizard : Window
{
    public Zielkategorie? GewaehltesZiel { get; private set; }
    public int? GewaehltesLevel { get; private set; }
    public SystemMetaInformationen Meta { get; } = new();

    private readonly List<int> _schrittSequenz;   // logische Schritte: 0=Ziel, 1=Level, 2=Meta
    private int _position;

    public NeuesSystemWizard(
        WizardModus modus = WizardModus.Alle,
        Zielkategorie? vorhandenesZiel = null,
        int? vorhandenesLevel = null,
        SystemMetaInformationen? vorhandeneMeta = null)
    {
        InitializeComponent();

        _schrittSequenz = modus switch
        {
            WizardModus.NurMeta => new List<int> { 2 },
            WizardModus.NurZielUndLevel => new List<int> { 0, 1 },
            _ => new List<int> { 0, 1, 2 }
        };

        if (vorhandeneMeta is not null) VorbefuelleMeta(vorhandeneMeta);

        if (vorhandenesZiel is not null)
        {
            GewaehltesZiel = vorhandenesZiel;
            RadioButton? rbZiel = vorhandenesZiel switch
            {
                Zielkategorie.Produktionsplanung => RbZielProduktionsplanung,
                Zielkategorie.ProofOfConcept => RbZielProofOfConcept,
                Zielkategorie.Rendering => RbZielRendering,
                _ => null
            };
            if (rbZiel is not null) rbZiel.IsChecked = true;
            BefuelleLevelKarten();

            if (vorhandenesLevel is not null)
            {
                foreach (RadioButton rb in PanelLevelKarten.Children.OfType<RadioButton>())
                {
                    if ((int)rb.Tag == vorhandenesLevel.Value) { rb.IsChecked = true; break; }
                }
            }
        }

        ZeigeSchritt(_schrittSequenz[0]);
    }

    private void VorbefuelleMeta(SystemMetaInformationen m)
    {
        TxtSystembezeichnung.Text = m.Systembezeichnung;
        TxtSystemgrenzen.Text = m.Systemgrenzen;
        TxtEingangsgroessen.Text = m.Eingangsgroessen;
        TxtAusgangsgroessen.Text = m.Ausgangsgroessen;
        TxtAblaufstruktur.Text = m.AblaufstrukturUebergeordnet;
        TxtBauteile.Text = m.Bauteile;
        TxtSystemklassifikation.Text = m.Systemklassifikation;
        TxtAnnahmen.Text = m.AnnahmenVereinfachungen;
        TxtProduktionsplan.Text = m.Produktionsplan;
        TxtMtbfMttr.Text = m.MtbfMttr;
    }

    private void Ziel_Checked(object sender, RoutedEventArgs e)
    {
        GewaehltesZiel = sender switch
        {
            _ when ReferenceEquals(sender, RbZielProduktionsplanung) => Zielkategorie.Produktionsplanung,
            _ when ReferenceEquals(sender, RbZielProofOfConcept) => Zielkategorie.ProofOfConcept,
            _ when ReferenceEquals(sender, RbZielRendering) => Zielkategorie.Rendering,
            _ => GewaehltesZiel
        };
    }

    /// <summary>Baut die Level-Auswahlkarten fuer den zuvor gewaehlten Ziel neu auf.</summary>
    private void BefuelleLevelKarten()
    {
        PanelLevelKarten.Children.Clear();
        if (GewaehltesZiel is null) { GewaehltesLevel = null; return; }

        var textPrimaer = (Brush)FindResource("TextPrimary");
        var textSekundaer = (Brush)FindResource("TextSecondary");
        var stilKarte = (Style)FindResource("AuswahlKarte");

        foreach (LevelInfo info in LevelKatalog.FuerZiel(GewaehltesZiel.Value))
        {
            var inhalt = new StackPanel();
            inhalt.Children.Add(new TextBlock
            {
                Text = $"Level {info.Level} · {info.Ebene}",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = textPrimaer
            });
            inhalt.Children.Add(new TextBlock
            {
                Text = info.Beschreibung,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = textSekundaer,
                Margin = new Thickness(0, 4, 0, 0)
            });

            int level = info.Level;
            var radio = new RadioButton { GroupName = "Level", Style = stilKarte, Content = inhalt, Tag = level };
            radio.Checked += (_, _) => GewaehltesLevel = level;
            PanelLevelKarten.Children.Add(radio);
        }
    }

    private void ZeigeSchritt(int schritt)
    {
        SchrittZiel.Visibility = schritt == 0 ? Visibility.Visible : Visibility.Collapsed;
        SchrittLevel.Visibility = schritt == 1 ? Visibility.Visible : Visibility.Collapsed;
        SchrittMeta.Visibility = schritt == 2 ? Visibility.Visible : Visibility.Collapsed;

        (string titel, string untertitel) = schritt switch
        {
            0 => ("Zielbeschreibung", "Welches Ziel soll mit der Simulation verfolgt werden?"),
            1 => ("Aufgabendefinition", "Welcher Detaillierungsgrad (Level) passt zum gewählten Ziel?"),
            _ => ("Systemdaten", "Meta-Systeminformationen auf Systemebene erfassen.")
        };
        TxtSchritt.Text = $"Schritt {_position + 1} von {_schrittSequenz.Count}";
        TxtTitel.Text = titel;
        TxtUntertitel.Text = untertitel;

        BtnZurueck.Visibility = _position == 0 ? Visibility.Collapsed : Visibility.Visible;
        bool letzterSchritt = _position == _schrittSequenz.Count - 1;
        BtnWeiter.Content = !letzterSchritt ? "Weiter" : schritt switch
        {
            1 => "Übernehmen",
            2 => "Speichern",
            _ => "System anlegen"
        };

        if (schritt == 2)
        {
            PanelLevel1Bis2Felder.Visibility =
                GewaehltesLevel is 1 or 2 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void NaechsterSchrittOderFertig()
    {
        if (_position < _schrittSequenz.Count - 1)
        {
            _position++;
            ZeigeSchritt(_schrittSequenz[_position]);
        }
        else
        {
            DialogResult = true;
        }
    }

    private void Weiter_Click(object sender, RoutedEventArgs e)
    {
        switch (_schrittSequenz[_position])
        {
            case 0:
                if (GewaehltesZiel is null)
                {
                    MessageBox.Show(this, "Bitte ein Ziel auswählen.", "Zielbeschreibung");
                    return;
                }
                if (_schrittSequenz.Contains(1)) BefuelleLevelKarten();
                NaechsterSchrittOderFertig();
                break;

            case 1:
                if (GewaehltesLevel is null)
                {
                    MessageBox.Show(this, "Bitte einen Level auswählen.", "Aufgabendefinition");
                    return;
                }
                NaechsterSchrittOderFertig();
                break;

            default:
                if (string.IsNullOrWhiteSpace(TxtSystembezeichnung.Text))
                {
                    MessageBox.Show(this, "Bitte zumindest die Systembezeichnung angeben.", "Systeminformationen");
                    return;
                }

                Meta.Systembezeichnung = TxtSystembezeichnung.Text.Trim();
                Meta.Systemgrenzen = TxtSystemgrenzen.Text.Trim();
                Meta.Eingangsgroessen = TxtEingangsgroessen.Text.Trim();
                Meta.Ausgangsgroessen = TxtAusgangsgroessen.Text.Trim();
                Meta.AblaufstrukturUebergeordnet = TxtAblaufstruktur.Text.Trim();
                Meta.Bauteile = TxtBauteile.Text.Trim();
                Meta.Systemklassifikation = TxtSystemklassifikation.Text.Trim();
                Meta.AnnahmenVereinfachungen = TxtAnnahmen.Text.Trim();
                Meta.Produktionsplan = TxtProduktionsplan.Text.Trim();
                Meta.MtbfMttr = TxtMtbfMttr.Text.Trim();

                NaechsterSchrittOderFertig();
                break;
        }
    }

    private void Zurueck_Click(object sender, RoutedEventArgs e)
    {
        if (_position == 0) return;
        _position--;
        ZeigeSchritt(_schrittSequenz[_position]);
    }

    private void Abbrechen_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
