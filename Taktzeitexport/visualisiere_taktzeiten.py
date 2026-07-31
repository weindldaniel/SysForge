import os
os.environ.setdefault("MKL_THREADING_LAYER", "SEQUENTIAL")  # siehe Bsc-Projekt-Notebooks: MKL/OpenMP-Konflikt in bsc-thesis-Env

import json
from pathlib import Path

import numpy as np
import matplotlib.pyplot as plt
from scipy.stats import norm

# ---- Pfade (relativ zum Skript, unabhaengig vom Arbeitsverzeichnis) ----
SKRIPT_VERZEICHNIS = Path(__file__).resolve().parent
JSON_PFAD = SKRIPT_VERZEICHNIS / "taktzeiten.json"
PNG_PFAD = SKRIPT_VERZEICHNIS / "taktzeiten.png"

# ---- Grafikstil (LaTeX-kompatibel, analog zu den Bsc-Projekt-Notebooks) ----
plt.rcParams.update({
    "font.family": "serif",
    "font.serif": ["Latin Modern Roman", "CMU Serif", "DejaVu Serif"],
    "mathtext.fontset": "cm",
    "font.size": 11,
    "axes.titlesize": 11,
    "axes.labelsize": 11,
    "axes.spines.top": False,
    "axes.spines.right": False,
    "axes.linewidth": 0.8,
    "axes.grid": True,
    "grid.alpha": 0.3,
    "grid.linestyle": "--",
    "savefig.dpi": 200,
    "savefig.bbox": "tight",
    "savefig.pad_inches": 0.15,
    "savefig.facecolor": "white",
})

C_BLUE = "#0072B2"
C_RED = "#D55E00"
C_GRAY = "#999999"

# ---- JSON einlesen ----
with open(JSON_PFAD, encoding="utf-8") as fh:
    daten = json.load(fh)

taktzeiten = np.concatenate([np.array(lauf["Taktzeiten"]) for lauf in daten["Ergebnisse"]])

n = len(taktzeiten)
x_bar = taktzeiten.mean()
s = taktzeiten.std(ddof=1)

print(f"Anlage:          {daten['Anlage']}")
print(f"Laeufe:          {daten['AnzahlLaeufe']}  (je {daten['TeileProLauf']} Teile)")
print(f"Taktzeiten (n):  {n}")
print(f"Mittelwert:      {x_bar:.4f} s")
print(f"Std.-Abweichung: {s:.4f} s")
print(f"Minimum/Maximum: {taktzeiten.min():.2f} s / {taktzeiten.max():.2f} s")

# ---- Visualisierung ----
fig, ax = plt.subplots(figsize=(8, 5))
ax.hist(taktzeiten, bins=40, density=True, color=C_BLUE, alpha=0.75, edgecolor="white", zorder=2)

xs = np.linspace(taktzeiten.min(), taktzeiten.max(), 300)
ax.plot(xs, norm.pdf(xs, loc=x_bar, scale=s), color=C_RED, linewidth=2, zorder=3,
        label=r"$\mathcal{N}(\bar{x}, s^2)$")
ax.axvline(x_bar, color=C_GRAY, linewidth=1.3, linestyle="--", zorder=2,
           label=fr"$\bar{{x}} = {x_bar:.2f}$ s")

ax.set_xlabel("Taktzeit [s]")
ax.set_ylabel("Dichte")
ax.legend(loc="upper right")

fig.suptitle("Taktzeit — Simulierte Anlage (SysForge)", fontsize=14, fontweight="bold", y=0.99)
ax.set_title(f"{daten['AnzahlLaeufe']} Läufe à {daten['TeileProLauf']} Teile   |   n={n}   |   "
             f"x̄={x_bar:.2f} s   |   s={s:.2f} s", fontsize=10)

fig.tight_layout(rect=(0, 0, 1, 0.94))
fig.savefig(PNG_PFAD)
print(f"\nPNG gespeichert: {PNG_PFAD}")
