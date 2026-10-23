# WinCtl

Schlanker Windows-Agent, der einen PC über **MQTT** fernsteuerbar macht - gebaut als Gegenstück für einen ioBroker-Adapter.
Eine einzelne `WinCtl.exe` (~140 KB), kein Installer, keine Zusatz-Runtime (läuft auf dem in Windows 10/11 enthaltenen .NET Framework 4.x).

* Tray-Icon (grün = verbunden, rot = getrennt), Einstellungsdialog beim ersten Start, Autostart per Klick
* **Selbstbeschreibend:** der Agent veröffentlicht alle Befehle, Parameter und States als JSON-Manifest - der Adapter braucht keine hartcodierten Listen
* Läuft im Benutzerkontext (kein Dienst, kein Admin) - deshalb funktionieren Popups, Vollbild, Screenshots und Tastatursteuerung
* 49 Befehle, 20 States, Events, Broadcast an alle PCs - siehe [`docs/COMMANDS.md`](docs/COMMANDS.md)

## Schnellstart

1. `WinCtl.exe` starten (z. B. nach `C:\Tools\WinCtl\` kopiert; die Config liegt daneben, ist der Ordner nicht beschreibbar unter `%APPDATA%\WinCtl`).
2. Im Einstellungsdialog MQTT-Broker, Benutzer/Passwort und ggf. Geräte-ID eintragen → Speichern. Das Tray-Icon wird grün.
3. Testen:

```bash
mosquitto_sub -h BROKER -t 'winctl/#' -v &
mosquitto_pub -h BROKER -t 'winctl/MEIN-PC/cmd/speak' -m 'Hallo aus ioBroker'
mosquitto_pub -h BROKER -t 'winctl/MEIN-PC/cmd/show_image' -m '{"source":"https://picsum.photos/800/450","mode":"corner","seconds":8,"caption":"Test"}'
mosquitto_pub -h BROKER -t 'winctl/MEIN-PC/cmd/ask' -m '{"text":"Kino starten?","buttons":["Ja","Nein"],"timeout":30,"id":"q1"}'   # Antwort kommt auf .../result
```

Oder automatisiert: `python tools/smoke_test.py --host BROKER --id mein-pc`

## Was kann er?

| Bereich | Befehle |
|---|---|
| **Programme** | `run` (Alias oder Pfad, mit Args/Arbeitsverzeichnis/versteckt/Admin/warten), `open`, `kill`, `list_processes`, `list_windows`, `focus_window`, `close_window`, `minimize_all`, `shell` (PowerShell/cmd, mit Ausgabe), `empty_recycle_bin` |
| **Anzeige** | `show_image` (Popup / Vollbild / Ecke, Datei·URL·Base64, auch GIF), `show_video` (mp4, Loop, Ton), `show_web` (Dashboard/Kiosk), `show_text` (klickdurchlässiges Overlay), `ask` (Dialog mit Buttons/Eingabefeld, **Antwort zurück an ioBroker**), `notify`, `close_popups`, `screenshot` (ans MQTT zurück), `set_wallpaper`, `brightness` (Laptop) |
| **Audio** | `volume`, `volume_change`, `mute`, `media` (Play/Pause/Next/…), `speak` (TTS mit Stimmenwahl), `list_voices`, `play_sound` (mp3/wav, URL oder Datei), `stop_sound`, `beep` |
| **Energie** | `lock`, `sleep`, `hibernate`, `shutdown`, `restart`, `logoff`, `cancel_shutdown`, `monitor_off`, `monitor_on`, `keep_awake` |
| **Eingabe** | `send_keys`, `type_text`, `mouse` (bewegen/klicken/scrollen), `clipboard_get`, `clipboard_set` |
| **Überwachung** | States: Sperrstatus, Anwesenheit (`user_present`), Idle-Zeit, aktives Fenster/Prozess, Lautstärke, CPU, RAM, Uptime, Akku, Laufwerke, überwachte Prozesse (`WatchProcesses`) |
| **Events** | `lock`, `unlock`, `logon`, `logoff`, `suspend`, `resume`, `remote_*` und **globale Hotkeys** (`Hotkeys` in der Config → Event `hotkey`; der PC wird zur Szenen-Tastatur) |

Besonderheiten für Smart-Home-Anwendungsfälle: `slot` ersetzt ein bestehendes Popup (Kamerabild alle 5 s aktualisieren), `mode: corner` blendet z. B. die Türklingel-Kamera unten rechts ein,
`ask` macht den PC zum Rückkanal („Soll ich das Licht dimmen?“), `keep_awake` hält Präsentations-/Kiosk-PCs wach, und `{base}/all/cmd/…` steuert alle PCs gleichzeitig.

## Einstellungen

Alles wird im Einstellungsdialog gesetzt (öffnet sich beim ersten Start, danach Tray-Icon → „Einstellungen …“). Reiter:

| Reiter | Inhalt |
|---|---|
| **Verbindung** | MQTT-Broker, Port, Benutzer/Passwort, TLS (optional selbstsigniertes Zertifikat), Topic-Basis, Geräte-ID (leer = Hostname) |
| **Allgemein** | Mit Windows starten, Sendeintervall der States, Abwesenheit nach x s ohne Eingabe, selbstsignierte https-Medienquellen, Log-Datei |
| **Programme** | Aliase für `run`/`open` (Alias → Pfad, „...“ zum Auswählen), optional beliebige Programme erlauben |
| **Hotkeys** | Globale Tastenkürzel → Event `hotkey` an ioBroker (Kürzel ins Feld klicken und drücken) |
| **Prozesse** | Überwachte Programme → `state/proc/<name>` (auch aus laufenden Prozessen wählbar) |
| **Befehle** | `shell` freischalten, einzelne Befehle abschalten |

Gespeichert wird in `winctl.config.json` neben der exe (ist der Ordner nicht beschreibbar: `%APPDATA%\WinCtl`). Änderungen gelten sofort nach „Speichern“.
Die Datei kann auch von Hand bearbeitet werden (Tray → „Config-Datei öffnen“, danach „Config neu laden“ oder Befehl `reload_config`):

```jsonc
{
  "Host": "192.168.1.5", "Port": 1883, "Username": "winctl", "Password": "…", "Tls": false,
  "BaseTopic": "winctl", "DeviceId": "",
  "AllowAnyRun": false, "AllowShell": false,
  "DisabledCommands": ["restart","logoff"],
  "Apps": { "notepad": "notepad.exe", "kodi": "C:\\Program Files\\Kodi\\kodi.exe" },
  "Hotkeys": { "ctrl+alt+1": "szene_kino" },
  "WatchProcesses": ["steam", "obs64"],
  "StateIntervalSec": 15, "IdleThresholdSec": 120
}
```

## Sicherheit

Wer auf `…/cmd/#` schreiben darf, steuert den PC. Deshalb:

* **Broker absichern:** Benutzer/Passwort, ACL (nur der Adapter darf auf `winctl/+/cmd/#` und `winctl/all/cmd/#` schreiben), im Zweifel TLS.
* Standardmäßig startet `run` **nur Aliase** aus `Apps`; `shell` ist **aus**. Beides bewusst per Config freischalten.
* Das Passwort steht im Klartext in der Config-Datei - Ablage im Benutzerprofil/Programmordner mit passenden Dateirechten.
* Läuft ohne Adminrechte; `run` mit `admin:true` löst eine UAC-Abfrage am PC aus (keine stille Rechteausweitung).

## Bauen

Der Agent ist reines C# gegen .NET Framework 4.x und wird mit dem Mono-Compiler erzeugt (kein NuGet nötig, MQTT-Client ist eingebaut):

```bash
sudo apt install mono-mcs libmono-system-windows-forms4.0-cil libmono-system-web-extensions4.0-cil libmono-system-management4.0-cil
./build.sh            # -> dist/WinCtl.exe
python3 tools/gen_docs.py   # docs/COMMANDS.md aus der Befehls-Registry neu erzeugen
```

Der GitHub-Workflow baut bei jedem Push und hängt die exe bei Tags `v*` an ein Release. Unter Windows geht es auch mit `csc` aus Visual Studio / Build Tools (`csc /target:winexe /out:WinCtl.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Management.dll src\*.cs`) - der Code nutzt höchstens C# 6.

## Bekannte Grenzen

* `show_video`/`show_web` nutzen das eingebaute IE11-Control: mp4/H.264, WebM nicht garantiert, moderne Web-Apps teils eingeschränkt. Upgrade-Pfad: WebView2.
* `brightness` funktioniert nur bei internen Laptop-Displays (WMI), nicht bei externen Monitoren.
* Aktionen, die eine Benutzersitzung brauchen, gehen nicht bei gesperrtem Bildschirm/ohne angemeldeten Benutzer (`lock`, `monitor_*`, `shutdown` schon).
* Bildschirmtastatur-/UAC-Dialoge (sicherer Desktop) sind für `send_keys`/`mouse` unerreichbar - Windows-Sicherheitsfeature.

## Weiterentwicklung

Siehe [`docs/PROTOCOL.md`](docs/PROTOCOL.md) für den Vertrag mit dem ioBroker-Adapter (Topics, Ergebnis-Korrelation, Manifest, Objektmodell-Vorschlag).
