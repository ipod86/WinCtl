<img src="assets/winctl.png" alt="Logo" width="128">

# WinCtl

Schlanker Windows-Agent, der einen PC über **MQTT** fernsteuerbar macht - gebaut als Gegenstück für einen ioBroker-Adapter.
Eine einzelne `WinCtl.exe` (~600 KB), kein Installer, keine Zusatz-Runtime (läuft auf dem in Windows 10/11 enthaltenen .NET Framework 4.x; für Webseiten nutzt sie den in Windows enthaltenen Edge-Browser über WebView2).

* Tray-Icon mit Statuspunkt unten rechts (grün = verbunden, rot = getrennt), Einstellungsdialog beim ersten Start, Autostart per Klick
* **Selbstbeschreibend:** der Agent veröffentlicht alle Befehle, Parameter und States als JSON-Manifest - der Adapter braucht keine hartcodierten Listen
* Läuft im Benutzerkontext (kein Dienst, kein Admin) - deshalb funktionieren Popups, Vollbild, Screenshots und Tastatursteuerung
* 53 Befehle, 26 States, Events, Broadcast an alle PCs - siehe [`docs/COMMANDS.md`](docs/COMMANDS.md)

## Schnellstart

1. `WinCtl.exe` aus den [Releases](https://github.com/ipod86/WinCtl/releases/latest) herunterladen und starten (z. B. nach `C:\Tools\WinCtl\` kopiert; die Config liegt daneben, ist der Ordner nicht beschreibbar unter `%APPDATA%\WinCtl`).
2. Im Einstellungsdialog MQTT-Broker, Benutzer/Passwort und ggf. Geräte-ID eintragen → Speichern. Das Tray-Icon wird grün (welchen Broker → siehe unten).
3. In ioBroker (Adapter desktop-control) erscheint der PC als Gerät. Zum Testen z. B. in `cmd.speak` „Hallo“ schreiben.

### Welcher MQTT-Broker?

WinCtl braucht einen MQTT-Broker als Zwischenstück zu ioBroker. Zwei Möglichkeiten:

* **Eingebauter Broker des Adapters (empfohlen):** Der desktop-control-Adapter bringt selbst einen Broker mit. In WinCtl die **IP-Adresse des ioBroker-Servers**, Port **1886** und – falls im Adapter gesetzt – Benutzer/Passwort eintragen. Sonst muss nichts installiert werden.
* **Vorhandener Broker:** Läuft schon Mosquitto oder der ioBroker-MQTT-Adapter, im desktop-control-Adapter „Vorhandener Broker“ wählen und in Adapter **und** WinCtl denselben Broker (Adresse, Port, meist 1883, Anmeldung) eintragen. Sinnvoll, wenn ACLs/TLS gebraucht werden oder andere Programme mitlesen sollen.

## Was kann er?

| Bereich | Befehle |
|---|---|
| **Programme** | `run` (Alias oder Pfad, mit Args/Arbeitsverzeichnis/versteckt/Admin/warten), `open`, `kill`, `list_processes`, `list_windows`, `focus_window`, `close_window`, `minimize_all`, `shell` (PowerShell/cmd, mit Ausgabe), `empty_recycle_bin` |
| **Anzeige** | `show_image` (Popup / Vollbild / Ecke, Datei·URL·Base64, auch GIF), `show_video` (mit Edge: mp4, WebM, …; Loop, Ton), `show_web` (vis/Lovelace/Dashboard als Kiosk mit Edge/Chromium, Zoom, Diashow mehrerer Seiten, aktuelle Seite als State `web_url`), `show_text` (klickdurchlässiges Overlay), `ask` (Dialog mit Buttons/Eingabefeld, **Antwort zurück an ioBroker**), `notify`, `close_popups`, `screenshot` (ans MQTT zurück), `set_wallpaper`, `brightness` (Laptop) |
| **Audio** | `volume`, `volume_change`, `mute`, `media` (Play/Pause/Next/…), `speak` (TTS mit Stimmenwahl), `list_voices`, `play_sound` (mp3/wav, URL oder Datei), `stop_sound`, `beep` |
| **Energie** | `lock`, `sleep`, `hibernate`, `shutdown`, `restart`, `logoff`, `cancel_shutdown`, `monitor_off`, `monitor_on`, `screensaver`, `keep_awake` |
| **Eingabe** | `start_menu`, `send_keys`, `type_text`, `mouse` (bewegen/klicken/scrollen), `clipboard_get`, `clipboard_set` |
| **Überwachung** | States: Sperrstatus, Anwesenheit (`user_present`), Idle-Zeit, aktives Fenster/Prozess, Lautstärke, CPU, RAM, Uptime, Akku, Laufwerke, überwachte Prozesse (`WatchProcesses`), **Kamera/Mikrofon in Benutzung** (`webcam_in_use`, `mic_in_use` + Programmname – z. B. für eine „On Air“-Lampe) |
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

## Update

WinCtl prüft beim Start und danach alle 12 Stunden die [Releases](https://github.com/ipod86/WinCtl/releases) (abschaltbar in den Einstellungen → „Allgemein“). Es **installiert nicht von selbst**, sondern meldet nur:

* `update_available` – `true`, wenn es eine neuere Version gibt
* `latest_version` – die neueste Version auf GitHub (die installierte steht im Manifest bzw. in ioBroker unter `info.version`)

Installiert wird auf einem dieser Wege:

* **Am PC:** Tray-Icon → **„Nach Update suchen …“** – zeigt „aktuell“ oder die Neuerungen und fragt, ob installiert werden soll.
* **Aus ioBroker:** Knopf `cmd.update` (prüft sofort: `cmd.check_update`). Per Skript lässt sich das automatisieren, z. B. sobald `update_available` auf `true` geht.

Ablauf: neue exe herunterladen → Prüfsumme (SHA-256 aus dem Release) prüfen → alte exe als `WinCtl.exe.old` beiseitelegen → neue einsetzen → neu starten. Nach ein paar Sekunden ist der PC wieder online. Die Einstellungen bleiben erhalten.
Voraussetzung: Die exe liegt in einem beschreibbaren Ordner (z. B. `C:\Tools\WinCtl`, nicht `C:\Program Files`).

## Sicherheit

Wer auf `…/cmd/#` schreiben darf, steuert den PC. Deshalb:

* **Broker absichern:** Benutzer/Passwort, ACL (nur der Adapter darf auf `winctl/+/cmd/#` und `winctl/all/cmd/#` schreiben), im Zweifel TLS.
* Standardmäßig startet `run` **nur Aliase** aus `Apps`; `shell` ist **aus**. Beides bewusst per Config freischalten.
* Das Passwort steht im Klartext in der Config-Datei - Ablage im Benutzerprofil/Programmordner mit passenden Dateirechten.
* Läuft ohne Adminrechte; `run` mit `admin:true` löst eine UAC-Abfrage am PC aus (keine stille Rechteausweitung).

## Bauen

Der Agent ist reines C# gegen .NET Framework 4.x und wird mit dem Mono-Compiler erzeugt (MQTT-Client ist eingebaut). `build.sh` lädt einmalig von NuGet (Prüfsumme geprüft, Cache in `.refs/`) die .NET-4.6.2-Referenzassemblies, damit keine Mono-only-APIs in die exe rutschen, und das Microsoft-Edge-WebView2-SDK, dessen DLLs gzip-komprimiert in die exe eingebettet werden:

```bash
sudo apt install mono-mcs libmono-system-windows-forms4.0-cil libmono-system-web-extensions4.0-cil libmono-system-management4.0-cil
./build.sh            # -> dist/WinCtl.exe
python3 tools/gen_docs.py   # docs/COMMANDS.md aus der Befehls-Registry neu erzeugen
```

Der GitHub-Workflow baut bei jedem Push und hängt die exe bei Tags `v*` an ein Release. Unter Windows geht es auch mit `csc` aus Visual Studio / Build Tools (`csc /target:winexe /out:WinCtl.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Management.dll src\*.cs`) - der Code nutzt höchstens C# 6.

## Bekannte Grenzen

* `show_web` und `show_video` nutzen **Edge WebView2** (Chromium). Die Runtime ist in Windows 11 und aktuellen Windows-10-Versionen enthalten; fehlt sie, fallen beide auf das alte IE11-Control zurück (dann nur mp4/H.264) (Ergebnis `engine: "ie"`, Runtime-Download: https://go.microsoft.com/fwlink/p/?LinkId=2124703). Die WebView2-DLLs stecken in der exe und werden beim ersten Gebrauch nach `%LOCALAPPDATA%\WinCtl\webview2` ausgepackt.
* `screenshot` geht nicht bei gesperrtem Bildschirm, offener UAC-Abfrage oder getrennter/minimierter Remotedesktop-Sitzung (dann gibt es kein Bild zum Aufnehmen).
* `brightness` funktioniert nur bei internen Laptop-Displays (WMI), nicht bei externen Monitoren.
* Aktionen, die eine Benutzersitzung brauchen, gehen nicht bei gesperrtem Bildschirm/ohne angemeldeten Benutzer (`lock`, `monitor_*`, `shutdown` schon).
* Bildschirmtastatur-/UAC-Dialoge (sicherer Desktop) sind für `send_keys`/`mouse` unerreichbar - Windows-Sicherheitsfeature.

## Weiterentwicklung

Siehe [`docs/PROTOCOL.md`](docs/PROTOCOL.md) für den Vertrag mit dem ioBroker-Adapter (Topics, Ergebnis-Korrelation, Manifest, Objektmodell-Vorschlag).
