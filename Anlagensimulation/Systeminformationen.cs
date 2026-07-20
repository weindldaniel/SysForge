namespace Anlagensimulation
{
    /// <summary>Zielkategorien der Zielbeschreibung (Kapitel 4.1.1).</summary>
    public enum Zielkategorie { Produktionsplanung, ProofOfConcept, Rendering }

    /// <summary>
    /// Meta Systeminformationen auf Systemebene (Kapitel 4.1.5, Datenbeschaffung).
    /// Produktionsplan/MtbfMttr sind nur fuer Level 1-2 relevant.
    /// </summary>
    public sealed class SystemMetaInformationen
    {
        public string Systembezeichnung { get; set; } = "";
        public string Systemgrenzen { get; set; } = "";
        public string Eingangsgroessen { get; set; } = "";
        public string Ausgangsgroessen { get; set; } = "";
        public string AblaufstrukturUebergeordnet { get; set; } = "";
        public string Bauteile { get; set; } = "";
        public string Systemklassifikation { get; set; } = "dynamisch, ereignisdiskret";
        public string AnnahmenVereinfachungen { get; set; } = "";

        public string Produktionsplan { get; set; } = "";
        public string MtbfMttr { get; set; } = "";
    }
}
