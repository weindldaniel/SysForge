# SysForge

C# WPF-Projekt — praktische Umsetzung der Bachelorarbeit von Daniel Weindl.

## Fachliche Grundlage

Definitionen, Konzepte, Modelle und Formeln stehen im LaTeX-Repo `..\Bsc-Projekt`, vor allem unter
`Text/` (Kapitel Grundlagen: `0201`-`0204`, Konzept: `0400`-`0408`).

**Bei Implementierungsaufgaben, die sich auf Konzepte aus der Arbeit beziehen: zuerst den relevanten
Abschnitt in `..\Bsc-Projekt\Text\` lesen, bevor Code geschrieben oder geändert wird.**

Code soll konsistent mit der Notation/Terminologie der Arbeit sein (Variablennamen, Bezeichnungen von
Konzepten etc.).

## Struktur

- `AnlagenEditor/` — WPF-UI
- `Anlagensimulation/` — Kernlogik: `Anlage.cs`, `Knoten.cs`, `Simulation.cs`, `Statistik.cs`