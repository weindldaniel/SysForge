namespace Anlagensimulation
{
    /// <summary>Ein Systemdetaillierungslevel wie in Kapitel 4.1.2 (Aufgabendefinition) definiert.</summary>
    public sealed record LevelInfo(int Level, string Ebene, string Beschreibung);

    /// <summary>Katalog der neun Detaillierungslevel samt Zuordnung zu den Zielkategorien (Kapitel 4.2.3).</summary>
    public static class LevelKatalog
    {
        public static readonly IReadOnlyList<LevelInfo> Alle = new List<LevelInfo>
        {
            new(1, "WERK", "Zusammenwirken der Fertigungs- und Montagebereiche mit Informationen über den Output des Systems."),
            new(2, "WERK", "Zusammenwirken der Fertigungs- und Montagebereiche mit Informationen über den Output des Systems und der OEE."),
            new(3, "PRODUKTIONSBEREICH", "Fertigungsabläufe und Materialfluss ohne maßstäbliche Geometrien (zeitbasiert)."),
            new(4, "PRODUKTIONSBEREICH", "Fertigungsabläufe und Materialfluss mit maßstäblichen Geometrien (zeitbasiert)."),
            new(5, "ANLAGE", "Funktions- und Kollisionsbetrachtung (klassische Erreichbarkeitsanalyse)."),
            new(6, "ANLAGE", "Funktions- und Kollisionsbetrachtung mit kinematischer Darstellung aller Anlagenteile."),
            new(7, "KOMPONENTE", "Einzelprozessbetrachtung von Schleifen, Schweißen, Prüfen, Vision-Systeme usw."),
            new(8, "KOMPONENTE", "Einzelprozessbetrachtung … mit anschließender Verwendung von Robotercode."),
            new(9, "SONSTIGES", "Kundenanimation (fotorealistisches Rendering von Bildern oder Videos)."),
        };

        /// <summary>Level, die laut Vorgehensmodell (Kapitel 4.2.3) zu einer Zielkategorie zur Auswahl stehen.</summary>
        public static IEnumerable<LevelInfo> FuerZiel(Zielkategorie ziel) => ziel switch
        {
            Zielkategorie.Produktionsplanung => Alle.Where(l => l.Level is 1 or 2),
            Zielkategorie.ProofOfConcept => Alle.Where(l => l.Level is >= 3 and <= 8),
            Zielkategorie.Rendering => Alle.Where(l => l.Level == 9),
            _ => Alle
        };
    }
}
