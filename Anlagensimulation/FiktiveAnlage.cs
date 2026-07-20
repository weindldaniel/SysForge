namespace Anlagensimulation
{
    /// <summary>
    /// Fiktive Anlage aus Kapitel 4.3 (Bewertung der Ergebnisunsicherheit von Simulationen
    /// mittels Konfidenzintervallen): Quelle -> Stn 1 - Bohren -> Stn 2 - Schweißen ->
    /// Stn 3 - Fräsen -> Senke, sequentiell ohne Puffer.
    /// </summary>
    public static class FiktiveAnlage
    {
        public static readonly string[] Reihenfolge =
        {
            "Quelle", "Stn 1 - Bohren", "Stn 2 - Schweißen", "Stn 3 - Fräsen", "Senke"
        };

        public static Anlage Erstellen()
        {
            var anlage = new Anlage();
            anlage.FuegeStationHinzu("Quelle", 0.0, 0.0);
            anlage.FuegeStationHinzu("Stn 1 - Bohren", 110.0, 4.0);
            anlage.FuegeStationHinzu("Stn 2 - Schweißen", 110.0, 4.0);
            anlage.FuegeStationHinzu("Stn 3 - Fräsen", 110.0, 4.0);
            anlage.FuegeStationHinzu("Senke", 0.0, 0.0);

            for (int i = 0; i < Reihenfolge.Length - 1; i++)
                anlage.Verbinde(Reihenfolge[i], Reihenfolge[i + 1]);

            anlage.SetzeQuellen("Quelle");

            // Aufgabenspezifikation und Systeminformationen wie in Kapitel 4.3.2 beschrieben.
            anlage.Ziel = Zielkategorie.ProofOfConcept;
            anlage.Level = 3;
            anlage.Meta = new SystemMetaInformationen
            {
                Systembezeichnung = "Fiktive Produktionsanlage",
                Systemgrenzen = "von der Quelle bis zur Senke",
                Eingangsgroessen = "Rohbauteil, Auftragsdaten",
                Ausgangsgroessen = "fertig bearbeitetes Bauteil",
                AblaufstrukturUebergeordnet = "Sequentiell ohne Puffer, wie im Ablaufdiagramm dargestellt",
                Bauteile = "ein fiktives Bauteil",
                Systemklassifikation = "Ereignisdiskret",
                AnnahmenVereinfachungen = "gesättigte Quelle, keine Ausschussteile, keine Puffer"
            };

            FuelleSystemelementInfo(anlage, "Quelle", "x", "Rohbauteil");
            FuelleSystemelementInfo(anlage, "Stn 1 - Bohren", "Rohbauteil", "gebohrtes Bauteil");
            FuelleSystemelementInfo(anlage, "Stn 2 - Schweißen", "gebohrtes Bauteil", "geschweißtes Bauteil");
            FuelleSystemelementInfo(anlage, "Stn 3 - Fräsen", "geschweißtes Bauteil", "gefrästes Bauteil");
            FuelleSystemelementInfo(anlage, "Senke", "gefrästes Bauteil", "x");

            return anlage;
        }

        private static void FuelleSystemelementInfo(Anlage anlage, string name, string eingang, string ausgang)
        {
            Knoten k = anlage.Knoten.First(n => n.Name == name);
            k.Systeminformationen[SystemelementFelder.Eingangsgroessen] = eingang;
            k.Systeminformationen[SystemelementFelder.Ausgangsgroessen] = ausgang;
            k.Systeminformationen[SystemelementFelder.KonstanteAttribute] = "Bauteilkapazität = 1";
            k.Systeminformationen[SystemelementFelder.VariableAttribute] = "aktueller Zustand, Werkstückzähler";
            k.Systeminformationen[SystemelementFelder.MoeglicheZustaende] = "bereit, in Bearbeitung, blockiert";
            k.Systeminformationen[SystemelementFelder.InnereAblaufstruktur] = $"siehe Algorithmus \"{name}\" (Kapitel 4.3.2)";
            k.Systeminformationen[SystemelementFelder.Zustandsuebergaenge] = "Bedingung je Übergang, z. B. Nachfolger frei";
        }
    }
}
