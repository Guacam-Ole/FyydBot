# FyydBot

(This bot uses an Api that only offers descriptions in german; So all the documentation here is in german, too)


Der FyydBot erlaubt es in natürlicher Sprache nach bestimmten Podcasts zu suchen. Es handelt sich um einen Mastodon-Bot.

Gültige Suchanfragen sind z.B:

```Ich suche einen Podcast aus 2020 übers Fahrrad```
```Welche Episode des Blathering Podcast handelt von Walen```
```Ich suche Folgen über Hamburg aus diesem Jahr```

## Vorbereitung
Der Bot arbeitet mit lokalem LLama. Dafür benötigt er eine sog. `GGUF`-Datei. Lade herunter. Getestet mit `openhermes-2.5-mistral-7b.Q4_K_M.gguf`. 

Zur Konfiguration einfach die `secrets.example.json` in `secrets.json` umbenennen und die Secrets/pfade entsprechend anpassen.
