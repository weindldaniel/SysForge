namespace Anlagensimulation
{
    /// <summary>
    /// Ereignisorientierte Simulation des Materialflusses ohne Puffer, mit
    /// Blockierung nach Bearbeitung und gesaettigten Quellen.
    ///
    /// Ablauf: Ein Ereigniskalender haelt die Zeitpunkte "Bearbeitung fertig".
    /// Nach jedem Ereignis ruecken fertige Teile in freie Nachfolger; Quellen
    /// werden nachgefuellt. Das bildet Verzweigung und Zusammenfuehrung ab.
    ///
    /// Modellannahmen:
    ///  - Verzweigung: ein Teil geht zur ersten freien Nachfolgerin (parallele Kapazitaet).
    ///  - Zusammenfuehrung: die Station nimmt, was zuerst ankommt (einfache Vereinigung).
    ///    Eine echte Montage (warten auf je ein Teil pro Eingang) waere ein eigener Knotentyp.
    /// </summary>
    public class Simulation
    {
        private readonly Anlage _anlage;

        public Simulation(Anlage anlage) => _anlage = anlage;

        /// <summary>
        /// Fuehrt einen Lauf durch, bis <paramref name="anzahlTeile"/> Teile die Senken
        /// verlassen haben, und liefert deren Abgangszeitpunkte.
        /// </summary>
        public double[] SimuliereLauf(int anzahlTeile, System.Random rng)
        {
            _anlage.Reset();

            // Ereigniskalender; die Sequenz macht die Ordnung bei Zeitgleichheit eindeutig.
            var kalender = new PriorityQueue<Knoten, (double Zeit, long Seq)>();
            long seq = 0;
            var abgaenge = new List<double>();

            void Starte(Knoten k, double jetzt)
            {
                k.Belegt = true;
                k.Fertig = false;
                k.FertigAb = jetzt + k.ZieheZeit(rng);
                kalender.Enqueue(k, (k.FertigAb, seq++));
            }

            void Propagiere(double jetzt)
            {
                bool geaendert = true;
                while (geaendert)
                {
                    geaendert = false;

                    // 1) Fertige Teile in freie Nachfolger ruecken oder abgehen lassen.
                    foreach (Knoten k in _anlage.Knoten)
                    {
                        if (!(k.Belegt && k.Fertig)) continue;

                        if (k.IstSenke)
                        {
                            abgaenge.Add(jetzt);
                            k.Belegt = false;
                            k.Fertig = false;
                            geaendert = true;
                        }
                        else
                        {
                            foreach (Knoten j in k.Nachfolger)
                            {
                                if (j.Belegt) continue;   // erste freie Nachfolgerin waehlen
                                k.Belegt = false;
                                k.Fertig = false;
                                Starte(j, jetzt);
                                geaendert = true;
                                break;
                            }
                        }
                    }

                    // 2) Gesaettigte Quellen nachfuellen.
                    foreach (Knoten q in _anlage.Quellen)
                    {
                        if (!q.Belegt)
                        {
                            Starte(q, jetzt);
                            geaendert = true;
                        }
                    }
                }
            }

            Propagiere(0.0);
            while (abgaenge.Count < anzahlTeile && kalender.Count > 0)
            {
                kalender.TryDequeue(out Knoten? k, out (double Zeit, long Seq) prio);
                k!.Fertig = true;
                Propagiere(prio.Zeit);
            }

            int anzahl = Math.Min(anzahlTeile, abgaenge.Count);
            return abgaenge.GetRange(0, anzahl).ToArray();
        }

        /// <summary>Taktzeiten als Abstaende aufeinanderfolgender Abgaenge.</summary>
        public static double[] Taktzeiten(double[] abgaenge)
        {
            var takt = new double[abgaenge.Length - 1];
            for (int i = 1; i < abgaenge.Length; i++)
                takt[i - 1] = abgaenge[i] - abgaenge[i - 1];
            return takt;
        }
    }
}
