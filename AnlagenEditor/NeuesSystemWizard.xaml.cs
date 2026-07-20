using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Anlagensimulation;

namespace AnlageEditor;

/// <summary>
/// Gefuehrter 3-Schritte-Dialog fuer ein neues System, nach dem Vorgehensmodell aus
/// Kapitel 4.1/4.2.3: Zielbeschreibung -> Aufgabendefinition (Level) -> Meta-Systeminformationen.
/// Bei DialogResult == true stehen GewaehltesZiel, GewaehltesLevel und Meta befuellt bereit.
/// </summary>
public partial class NeuesSystemWizard : Window
{
    public Zielkategorie? GewaehltesZiel { get; private set; }
    public int? GewaehltesLevel { get; private set; }
    public SystemMetaInformationen Meta { get; } = new();

    private int _schritt;

    public NeuesSystemWizard()
    {
        InitializeComponent();
        ZeigeSchritt(0);
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
        GewaehltesLevel = null;
        if (GewaehltesZiel is null) return;

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

            var radio = new RadioButton { GroupName = "Level", Style = stilKarte, Content = inhalt };
            int level = info.Level;
            radio.Checked += (_, _) => GewaehltesLevel = level;
            PanelLevelKarten.Children.Add(radio);
        }
    }

    private void ZeigeSchritt(int schritt)
    {
        _schritt = schritt;
        SchrittZiel.Visibility = schritt == 0 ? Visibility.Visible : Visibility.Collapsed;
        SchrittLevel.Visibility = schritt == 1 ? Visibility.Visible : Visibility.Collapsed;
        SchrittMeta.Visibility = schritt == 2 ? Visibility.Visible : Visibility.Collapsed;

        (string titel, string untertitel) = schritt switch
        {
            0 => ("Zielbeschreibung", "Welches Ziel soll mit der Simulation verfolgt werden?"),
            1 => ("Aufgabendefinition", "Welcher Detaillierungsgrad (Level) passt zum gewählten Ziel?"),
            _ => ("Systemanalyse & Datenbeschaffung", "Meta-Systeminformationen auf Systemebene erfassen.")
        };
        TxtSchritt.Text = $"Schritt {schritt + 1} von 3";
        TxtTitel.Text = titel;
        TxtUntertitel.Text = untertitel;

        BtnZurueck.Visibility = schritt == 0 ? Visibility.Collapsed : Visibility.Visible;
        BtnWeiter.Content = schritt == 2 ? "System anlegen" : "Weiter";

        if (schritt == 2)
        {
            PanelLevel1Bis2Felder.Visibility =
                GewaehltesLevel is 1 or 2 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void Weiter_Click(object sender, RoutedEventArgs e)
    {
        switch (_schritt)
        {
            case 0:
                if (GewaehltesZiel is null)
                {
                    MessageBox.Show(this, "Bitte ein Ziel auswählen.", "Zielbeschreibung");
                    return;
                }
                BefuelleLevelKarten();
                ZeigeSchritt(1);
                break;

            case 1:
                if (GewaehltesLevel is null)
                {
                    MessageBox.Show(this, "Bitte einen Level auswählen.", "Aufgabendefinition");
                    return;
                }
                ZeigeSchritt(2);
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

                DialogResult = true;
                break;
        }
    }

    private void Zurueck_Click(object sender, RoutedEventArgs e)
    {
        if (_schritt == 2) ZeigeSchritt(1);
        else if (_schritt == 1) ZeigeSchritt(0);
    }

    private void Abbrechen_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
