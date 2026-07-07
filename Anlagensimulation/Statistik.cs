using MathNet.Numerics.Distributions;
using MathNet.Numerics.Statistics;

namespace Anlagensimulation
{
    /// <summary>Ergebnis einer Konfidenzintervall-Berechnung.</summary>
    public record KonfidenzIntervall(
        double Mittelwert,
        double Std,
        double Standardfehler,
        double TQuantil,
        double Halbbreite,
        double Untere,
        double Obere);

    public static class Statistik
    {
        /// <summary>
        /// Zweiseitiges Konfidenzintervall fuer den Erwartungswert.
        /// Nutzt die t-Verteilung mit R-1 Freiheitsgraden, da die
        /// Standardabweichung aus den Werten geschaetzt wird.
        /// </summary>
        public static KonfidenzIntervall Konfidenzintervall(
            IReadOnlyList<double> werte, double niveau)
        {
            int r = werte.Count;
            double mittelwert = Statistics.Mean(werte);
            double std = Statistics.StandardDeviation(werte);   // Stichprobe (N-1)
            double standardfehler = std / Math.Sqrt(r);

            double alpha = 1.0 - niveau;
            double tKrit = StudentT.InvCDF(0.0, 1.0, r - 1, 1.0 - alpha / 2.0);
            double halbbreite = tKrit * standardfehler;

            return new KonfidenzIntervall(
                Mittelwert: mittelwert,
                Std: std,
                Standardfehler: standardfehler,
                TQuantil: tKrit,
                Halbbreite: halbbreite,
                Untere: mittelwert - halbbreite,
                Obere: mittelwert + halbbreite);
        }
    }
}
