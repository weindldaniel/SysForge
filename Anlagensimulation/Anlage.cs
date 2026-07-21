namespace Anlagensimulation
{
    /// <summary>
    /// Materialflussmodell als gerichteter Graph aus Stationen (Knoten) und
    /// Flusskanten. Erlaubt beliebige Topologien: seriell, parallel, verzweigt.
    /// Senken sind Knoten ohne Nachfolger.
    /// </summary>
    public class Anlage
    {
        private readonly Dictionary<string, Knoten> _knoten = new();
        private readonly List<Knoten> _quellen = new();

        public IReadOnlyCollection<Knoten> Knoten => _knoten.Values;
        public IReadOnlyList<Knoten> Quellen => _quellen;

        // ---- Aufgabenspezifikation (Kapitel 4.1.1/4.1.2/4.1.5) ----
        public Zielkategorie? Ziel { get; set; }
        public int? Level { get; set; }
        public SystemMetaInformationen Meta { get; set; } = new();

        /// <summary>Fuegt eine Station mit Erwartungswert und Standardabweichung hinzu.</summary>
        public Knoten FuegeStationHinzu(string name, double mu, double sigma)
        {
            var k = new Knoten(name, mu, sigma);
            _knoten[name] = k;
            return k;
        }

        /// <summary>Verbindet zwei Stationen mit einer gerichteten Flusskante.</summary>
        public void Verbinde(string von, string nach)
        {
            Knoten a = _knoten[von];
            Knoten b = _knoten[nach];
            a.Nachfolger.Add(b);
            b.Vorgaenger.Add(a);
        }

        /// <summary>Legt die Einspeisestationen fest (gesaettigte Quellen).</summary>
        public void SetzeQuellen(params string[] namen)
        {
            _quellen.Clear();
            foreach (string n in namen)
                _quellen.Add(_knoten[n]);
        }

        /// <summary>
        /// Benennt eine Station um. Liefert false, wenn der neue Name bereits vergeben ist
        /// oder die alte Station nicht existiert; die Anlage bleibt dabei unveraendert.
        /// </summary>
        public bool UmbenenneStation(string alterName, string neuerName)
        {
            if (alterName == neuerName) return true;
            if (!_knoten.TryGetValue(alterName, out Knoten? k)) return false;
            if (_knoten.ContainsKey(neuerName)) return false;

            _knoten.Remove(alterName);
            k.Name = neuerName;
            _knoten[neuerName] = k;
            return true;
        }

        /// <summary>Entfernt eine Station samt aller ein-/ausgehenden Flusskanten.</summary>
        public void EntferneStation(string name)
        {
            if (!_knoten.TryGetValue(name, out Knoten? k)) return;

            foreach (Knoten vorgaenger in k.Vorgaenger) vorgaenger.Nachfolger.Remove(k);
            foreach (Knoten nachfolger in k.Nachfolger) nachfolger.Vorgaenger.Remove(k);

            _knoten.Remove(name);
            _quellen.Remove(k);
        }

        public void Reset()
        {
            foreach (Knoten k in _knoten.Values)
                k.Reset();
        }
    }
}
