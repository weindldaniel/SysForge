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
            return anlage;
        }
    }
}
