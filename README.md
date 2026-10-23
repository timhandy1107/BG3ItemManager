# BG3 Item Manager – Entwicklungsstand v0.8

**Ziel:** Portable, eigenständige Windows-Anwendung für Baldur's Gate 3 mit Suche nach deutschen/englischen Namen und UUIDs, Kategorien, Original-Icons sowie importierten Daten aus Basisspiel und Mods. Die Anwendung liest die vorhandenen Spieldateien, verändert sie aber nicht.

## Neu in v0.8: lokale Script-Extender-Brücke (Quellcode)

- `Services/ExtenderBridgeService.cs`: Host-lokaler JSON-Befehlskanal mit eindeutiger Anfrage-ID, atomischer Befehlsdatei, Timeout, Heartbeat-Erkennung und Validierung (kein Netzwerkport).
- `BridgeMod/Mods/BG3ItemManagerBridge/`: einfacher Lua-Begleitmod mit `BootstrapServer.lua`, Meta-Datei und Script-Extender-Konfiguration. **Er muss im BG3-Host geladen werden.** Die Windows-Oberfläche selbst bleibt extern.
- `Brücke prüfen` sendet `ping` und meldet, ob eine Spielsession geladen ist. `Charaktere laden` holt Host und die aktuell registrierten Gruppenmitglieder.
- Im Rechtsklickmenü gibt es nun `An Charakter geben ...`, mit Empfänger und Menge 1 bis 99. Die Lua-Seite ruft `Osi.TemplateAddTo(template, target, count, 1)` auf.
- Da `TemplateAddTo` keine eindeutige Gegenstands-ID als Ergebnis liefert, wird lediglich **der ausgeführte Aufruf** bestätigt; der tatsächliche Erhalt muss im Inventar geprüft werden. Nur Root-Template-UUIDs funktionieren.
- `Tests/TestProgram.cs`: simuliert den lokalen Befehlskanal und prüft Heartbeat, Antworten und ungültige Eingaben ohne laufendes Spiel.
- `Tests/BridgeLuaSimulation.lua`: ohne BG3 ausführbarer Lua-Test für Ping, Gruppenmitglieder, Gegenstandsvergabe, Deduplikation und Verfallsprüfung. Der Test wurde hier unter Lua 5.4 erfolgreich ausgeführt. Ein Test mit echter BG3-Lua-Laufzeit ist das nicht.

**Koop-Einschränkung:** Eine lokale Lua-Brücke ist technisch ein separater BG3-Mod-Bestandteil. Dass sie wirklich nur auf dem Host aktivierbar ist, **ohne** dass Freunde eine Mod-Inkompatibilitätsmeldung erhalten, ist bisher nicht nachgewiesen. Keine Zusage über Unsichtbarkeit/Kompatibilität.

### Einrichtung des Lua-Bausteins

Eine Entwickler-Anleitung liegt unter `BridgeMod/INSTALLATION.txt`. Ein erster Weg ist das Kopieren der losen Dateien nach `<Spielordner>/Data/Mods/BG3ItemManagerBridge`. Das **allein garantiert nicht**, dass die Mod geladen wird; abhängig vom Mod-Setup muss sie als `.pak` verpackt/aktiviert werden. Die Bridge wird erst dann funktionieren, wenn der `BootstrapServer.lua` des BG3 Script Extenders tatsächlich läuft.

Kommunikationsverzeichnis: `%LOCALAPPDATA%\Larian Studios\Baldur's Gate 3\Script Extender\BG3ItemManager\` mit `request.json`, `response.json` und `status.json`. Es wird kein HTTP-Server und kein offener Netzwerkport verwendet. Ein frischer Status-Dateizeitstempel gilt als Heartbeat, nicht als Beweis eines erfolgreichen Item-Spawns.

**Auf keinen Fall bereits als live oder kompatibel ausgeben:** Windows- und BG3-Laufzeittest wurden hier nicht durchgeführt. Weder eine vollständige Item-Datenbank noch eine funktionierende Multiplayer-Freigabe ist nachgewiesen. Vor Tests einen separaten BG3-Spielstand verwenden.

## Neu in v0.7: Icon-Import aus den lokalen BG3-Dateien

- `IconAtlasIndex` liest die Icon-IDs (`MapKey`) und UV-Koordinaten (`U1/U2/V1/V2`) aus `TextureAtlasInfo` und `IconUVList` in LSX-Dateien. Getrennte Regionen werden berücksichtigt.
- `IconService` verknüpft die Icon-Definition mit dem zugrunde liegenden Bild **auch dann, wenn DDS/PNG und LSX aus verschiedenen PAK-Archiven stammen**. Mehrdeutige Zuordnungen werden lieber übersprungen, statt zufällige Bilder anzuzeigen.
- DDS-Bilder werden über **Microsoft DirectXTex `texconv.exe`** nach PNG umgewandelt. Nur tatsächlich referenzierte Bildausschnitte werden als separate kleine PNGs unter `Data/IconCache/` gespeichert. Als einzeln vorliegende PNG/JPEG-Icons werden direkt verwendet.
- Die Item-Liste zeigt nun kleine Bildvorschauen; die Detailansicht weiterhin das größere Icon. Fehlende Icons werden kenntlich gemacht.
- Ein dedizierter STA-Import-Thread verhindert, dass das Fenster während der zeitintensiven Bildverarbeitung einfriert.
- Ein zusätzlicher synthetischer Windows-Regressionstest deckt archivübergreifende Atlasauflösung, UV-Ausschnitt, PNG-Ausgabe und Farbzuordnung ab.
- Ein unterbrochener/fehlgeschlagener LSF-Konvertierungsvorgang wird beim nächsten Import erneut versucht.

## Voraussetzungen für den Import

1. Baldur's Gate 3 ist lokal installiert. Die Anwendung versucht, seinen Datenordner selbst zu erkennen. Alternativ **BG3-Ordner wählen**.
2. [LSLib/Divine.exe](https://github.com/Norbyte/lslib/releases) samt benötigten Dateien bereitstellen und über **Divine.exe auswählen** konfigurieren. Divine liest die `.pak`-Archive und konvertiert benötigte LSF-Ressourcen nach LSX.
3. Für DDS-Bilder zusätzlich [Microsoft DirectXTex / texconv.exe](https://github.com/microsoft/DirectXTex/wiki/texconv) bereitstellen und über **texconv.exe auswählen** konfigurieren. Einzeln vorliegende PNG/JPEG-Bilder funktionieren ohne texconv.
4. **Spiel + Mods einlesen** anklicken. Die App entpackt ausgewählte Original- und Mod-Archive in `Data/ModCache/` und speichert daraus abgeleitete Bilder in `Data/IconCache/`.

**Wichtig:** Die portable BG3ItemManager.exe benötigt kein separates .NET SDK auf dem Ziel-PC. Die unabhängigen Fremdwerkzeuge `Divine.exe` und `texconv.exe` müssen für den jeweiligen Import trotzdem vorhanden sein (und können eigene Laufzeitabhängigkeiten haben). Die Anwendung installiert oder lädt diese Tools nicht selbst herunter. Quellcode/Buildpaket enthält keine urheberrechtlich geschützten BG3-Spieldaten.

## Lokal gespeicherte Dateien

- `Data/game-settings.json` – BG3-Spielverzeichnis.
- `Data/tool-settings.json` – Divine-Pfad.
- `Data/texconv-settings.json` – Texconv-Pfad.
- `Data/ModCache/` – entpackte Archive und konvertierte LSX-Ressourcen.
- `Data/IconCache/` – konvertierte DDS-Atlanten und zugeschnittene Gegenstands-Icons.
- `Data/imported-items.json` – automatisch eingelesene Gegenstandsdaten.
- `Data/items.json` – optional gepflegte eigene Gegenstände.

## Bekannte Grenzen / noch offen

- **Noch nicht mit einer echten BG3-Installation oder unter Windows kompiliert/getestet.** Eine vollumfängliche Icon-Abdeckung kann daher nicht zugesichert werden. Bei abweichenden Dateiformaten, verschachtelten Atlasdefinitionen und Mod-Konflikten können Bilder fehlen.
- Die Atlas-Auflösung orientiert sich zunächst an Icon-IDs und virtuellen Pfaden. Mehrfach vergebene Icon-IDs verschiedener Mods und deren tatsächliche Ladereihenfolge sind noch nicht vollständig abgebildet.
- Große Spielarchive benötigen beim ersten Import Zeit und lokalen Speicherplatz. Erneute Importe nutzen einen Cache; die gespeicherten Spieldaten werden nicht verändert.
- Beschreibung und Werte sind derzeit **importierte Rohdaten**, keine exakte Nachbildung des Ingame-Untersuchenfensters.
- **Script-Extender-IPC und Vergabe sind als ungetestete Quellcode-Prototypen enthalten.** Noch offen sind die vollständige Prüfung spawnfähiger Root-Templates, End-to-End-Test, F8-Hotkey und exakte Ingame-Werte.
- Die App wurde bisher nicht als fertige Windows-EXE ausgeliefert. Dies ist ein **ungetesteter Quellcode-Entwicklungsstand**.

## Build / Tests

Auf Windows mit .NET 8 SDK:

```powershell
dotnet run --project Tests/BG3ItemManager.ImportTests.csproj -c Release
.\Build-Portable-EXE.ps1
```

Optionaler Lua-Simulationstest (falls ein Lua-5.4-Interpreter vorhanden ist): `lua Tests/BridgeLuaSimulation.lua`.

Oder per GitHub Actions `Build portable Windows EXE`: der Workflow führt die Tests aus und erstellt bei Erfolg ein portables Win-x64-Paket. Es wurde in dieser Umgebung **kein erfolgreicher Windows-Testlauf** durchgeführt.

## Technische Quellen

- [BG3-Modding: Item icons](https://bg3.wiki/wiki/Modding:Item_icons)
- [BG3 Modding: Icons & Atlas](https://docs.baldursgate3.game/Adding_Skill_and_Item_Icons)
- [Microsoft DirectXTex texconv](https://github.com/microsoft/DirectXTex/wiki/texconv)
- [Norbyte LSLib](https://github.com/Norbyte/lslib)
