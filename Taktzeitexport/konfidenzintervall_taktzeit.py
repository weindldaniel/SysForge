import os
os.environ.setdefault("MKL_THREADING_LAYER", "SEQUENTIAL")  # siehe Bsc-Projekt-Notebooks: MKL/OpenMP-Konflikt in bsc-thesis-Env

import json
from pathlib import Path

import numpy as np
from scipy.stats import t as t_verteilung

# ---- Pfad (relativ zum Skript, unabhaengig vom Arbeitsverzeichnis) ----
SKRIPT_VERZEICHNIS = Path(__file__).resolve().parent
JSON_PFAD = SKRIPT_VERZEICHNIS / "taktzeiten.json"

# ---- Konfidenzniveau ----
ALPHA = 0.01   # Irrtumswahrscheinlichkeit alpha -> (1-alpha)*100 %-Konfidenzintervall (wie SysForge Program.cs: niveau=0.99)

# ---- JSON einlesen, alle Taktzeiten aller Laeufe zu einer Stichprobe poolen ----
with open(JSON_PFAD, encoding="utf-8") as fh:
    daten = json.load(fh)

taktzeiten = np.concatenate([np.array(lauf["Taktzeiten"]) for lauf in daten["Ergebnisse"]])

# ---- Konfidenzintervall fuer den Erwartungswert (t-Verfahren, unbekannte Varianz) ----
n = len(taktzeiten)
x_bar = taktzeiten.mean()
s = taktzeiten.std(ddof=1)
standardfehler = s / np.sqrt(n)

t_krit = t_verteilung.ppf(1 - ALPHA / 2, df=n - 1)
halbbreite = t_krit * standardfehler
konfidenz_pct = (1 - ALPHA) * 100

print(f"Anlage:              {daten['Anlage']}")
print(f"Laeufe:              {daten['AnzahlLaeufe']}  (je {daten['TeileProLauf']} Teile)")
print(f"Stichprobenumfang n: {n}")
print(f"Mittelwert x_bar:    {x_bar:.4f} s")
print(f"Std.-Abweichung s:   {s:.4f} s")
print(f"Standardfehler:      {standardfehler:.4f} s")
print(f"t-Quantil (df={n - 1}): {t_krit:.4f}")
print(f"Halbbreite:          {halbbreite:.4f} s")
print(f"{konfidenz_pct:.0f}%-Konfidenzintervall: [{x_bar - halbbreite:.4f} ; {x_bar + halbbreite:.4f}] s")
