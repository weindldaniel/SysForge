using MathNet.Numerics.Distributions;

namespace Anlagensimulation
{
    /// <summary>
    /// Ein Knoten des Materialflussgraphen (eine Station) mit stochastischer
    /// Prozesszeit. Kann beliebig viele Vorgaenger und Nachfolger haben und
    /// bildet damit serielle, parallele und verzweigte Fluesse ab.
    /// </summary>
    public class Knoten
    {
        public string Name { get; }
        public double Mu { get; set; }
        public double Sigma { get; set; }

        public List<Knoten> Nachfolger { get; } = new();
        public List<Knoten> Vorgaenger { get; } = new();

        /// <summary>Systemelement-Informationen (Kapitel 4.1.5), Feld -> Wert; Felder je Level siehe SystemelementFelder.</summary>
        public Dictionary<string, string> Systeminformationen { get; } = new();

        // --- Laufzeitzustand ---
        public bool Belegt { get; set; }      // Teil in der Station (Bearbeitung oder blockiert)
        public bool Fertig { get; set; }      // fertig bearbeitet, wartet auf Weitergabe
        public double FertigAb { get; set; }  // Zeitpunkt des Bearbeitungsendes

        public Knoten(string name, double mu, double sigma)
        {
            Name = name;
            Mu = mu;
            Sigma = sigma;
        }

        /// <summary>Senke, wenn kein Nachfolger existiert.</summary>
        public bool IstSenke => Nachfolger.Count == 0;

        /// <summary>
        /// Zieht eine Prozesszeit aus N(Mu, Sigma^2); deterministisch, falls Sigma = 0.
        /// Negative Werte werden auf 0 gesetzt.
        /// </summary>
        public double ZieheZeit(System.Random rng)
        {
            if (Sigma <= 0.0) return Mu;
            return Math.Max(0.0, Normal.Sample(rng, Mu, Sigma));
        }

        public void Reset()
        {
            Belegt = false;
            Fertig = false;
            FertigAb = 0.0;
        }
    }
}
