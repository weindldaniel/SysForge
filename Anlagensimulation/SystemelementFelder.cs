namespace Anlagensimulation
{
    /// <summary>
    /// Felder der Systemelement-Informationsliste je Detaillierungslevel (Kapitel 4.1.5,
    /// Datenbeschaffung). "Dauer der Zustände" ist bewusst ausgenommen, da sie bereits
    /// über Mu/Sigma des Knotens erfasst wird; die Level-Tabellen sind kumulativ, jedes
    /// höhere Level ergänzt die Felder der vorherigen.
    /// </summary>
    public static class SystemelementFelder
    {
        public const string Eingangsgroessen = "Eingangsgröße(n)";
        public const string Ausgangsgroessen = "Ausgangsgröße(n)";
        public const string KonstanteAttribute = "Konstante Attribute";
        public const string VariableAttribute = "Variable Attribute (Zustandsgrößen)";
        public const string MoeglicheZustaende = "Mögliche Zustände (Betriebszustände)";
        public const string InnereAblaufstruktur = "Innere Ablaufstruktur";
        public const string Zustandsuebergaenge = "Zustandsübergänge (Auslöser)";
        public const string GeometrischeDaten = "Geometrische Daten des Systems";     // ab Level 4
        public const string Roboter = "Roboter";                                      // ab Level 5
        public const string Prozessinformationen = "Prozessinformationen";           // ab Level 6
        public const string KinematischeInformationen = "Kinematische Informationen"; // ab Level 6
        public const string Robotersteuerung = "Robotersteuerung";                    // ab Level 8
        public const string PlcSteuerung = "PLC-Steuerung";                          // ab Level 8
        public const string RobotercodeStandard = "Robotercode-Standard";            // ab Level 8

        // Level 9 (Rendering) hat ein eigenes, unabhängiges Feldset.
        public const string MaterialUndOberflaeche = "Material- und Oberflächeneigenschaften";
        public const string AblaufBzwProzessbeschreibung = "Ablauf- bzw. Prozessbeschreibung";
        public const string Bewegungsdefinitionen = "Bewegungsdefinitionen";
        public const string KamerapfadeUndPerspektiven = "Kamerapfade und Perspektiven";
        public const string Szenarien = "Szenarien";
        public const string Visualisierungsqualitaet = "Visualisierungsqualität";

        /// <summary>Level 1-2 haben keine eigene Systemelement-Tabelle (nur Meta-Informationen).</summary>
        public static IReadOnlyList<string> FelderFuerLevel(int level)
        {
            if (level <= 2) return Array.Empty<string>();

            if (level == 9)
            {
                return new[]
                {
                    MaterialUndOberflaeche, AblaufBzwProzessbeschreibung, Bewegungsdefinitionen,
                    KamerapfadeUndPerspektiven, Szenarien, Visualisierungsqualitaet
                };
            }

            var felder = new List<string>
            {
                Eingangsgroessen, Ausgangsgroessen, KonstanteAttribute, VariableAttribute,
                MoeglicheZustaende, InnereAblaufstruktur, Zustandsuebergaenge
            };
            if (level >= 4) felder.Add(GeometrischeDaten);
            if (level >= 5) felder.Add(Roboter);
            if (level >= 6) { felder.Add(Prozessinformationen); felder.Add(KinematischeInformationen); }
            if (level >= 8) { felder.Add(Robotersteuerung); felder.Add(PlcSteuerung); felder.Add(RobotercodeStandard); }
            return felder;
        }
    }
}
