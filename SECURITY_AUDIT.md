# Security Audit Report -- WriteSpeech.NET

**Datum:** 2026-02-28 (Update; Erstaudit: 2026-02-26)
**Scope:** Vollstaendiger Source Code Review aller `.cs`, `.xaml`, Config-Dateien
**Methode:** Automatisierte Code-Analyse + manueller Review

## Zusammenfassung

Follow-up Audit nach Implementierung der Fixes aus dem Erstaudit (2026-02-26). Von 14 urspruenglichen Findings wurden 5 vollstaendig behoben, 3 teilweise behoben. 4 neue Findings identifiziert. Angriffsflaeche bleibt durch Single-User Desktop-App ohne eingehende Netzwerkverbindungen begrenzt.

**Status Erstaudit:** 5 Fixed | 3 Partially Fixed | 4 Unchanged (Low/Info) | 2 Info (akzeptiert)
**Neue Findings:** 1 High | 2 Medium | 1 Low
**Gesamt offen:** 1 High | 4 Medium | 4 Low | 2 Info

---

## Status der Findings aus Erstaudit (2026-02-26)

| Finding | Severity | Status | Details |
|---------|----------|--------|---------|
| F1: API Key in git | HIGH | **FIXED** | `appsettings.json` in `.gitignore`, `appsettings.template.json` als tracked Template, `EnsureAppSettings()` kopiert bei Erststart |
| F3: Model-Integritaet | HIGH | **TEILWEISE** | `ModelDownloadHelper.DownloadToFileAsync` hat SHA-256 Support, aber kein Caller uebergibt Hashes |
| F4: Prompt Injection | MEDIUM | **FIXED** | User-Text in `<transcription>`-Tags gewrappt in `CloudTextCorrectionServiceBase` |
| F5: IDE Identifier Filter | HIGH | **TEILWEISE** | `SensitiveIdentifiers` Deny-List in `SourceFileParser.cs` hinzugefuegt, aber unvollstaendig |
| F7: Clipboard try/finally | MEDIUM | **FIXED** | `try/finally` in `TextInsertionService.cs` implementiert |
| F9: Atomare Settings-Writes | MEDIUM | **FIXED** | Temp-Datei + `File.Move` Pattern |
| F10: Input-Laengenlimits | MEDIUM | **TEILWEISE** | `MaxInputLength = 50_000` in `CloudTextCorrectionServiceBase`, aber Audio-Dateien/MaxRecording ohne Limit |
| F2: API Key im Speicher | MEDIUM | Offen | Unveraendert |
| F8: Wrong Window | LOW | Offen | Unveraendert |
| F6: CUDA DLL Pfad | LOW | **HOCHGESTUFT → HIGH** | Neubewertung: Env-Vars sind user-settable, kein Admin noetig |
| F11: Error Messages | LOW | Offen | Unveraendert |
| F12: Rate Limiting | LOW | Offen | Unveraendert, State Machine ist effektiver Guard |
| F13: Custom Endpoint | INFO | Akzeptiert | Beabsichtigtes Feature |
| F14: Registry Auto-Start | INFO | Akzeptiert | Korrekte Implementierung |

---

## Offene Findings -- HIGH Severity

### F6-rev -- CUDA PATH Environment Variable Injection (hochgestuft von LOW)

**Dateien:** `App.xaml.cs:246-291`

`AddCudaLibraryPaths()` liest `CUDA_PATH_V13_*` Environment-Variablen und fuegt Verzeichnisse dem PATH voran. Keine Pfad-Kanonisierung (`Path.GetFullPath`), keine Validierung gegen erlaubte Basisverzeichnisse. **Kritisch:** Environment-Variablen sind per-User setzbar, kein Admin-Zugriff erforderlich.

**Angriffsvektor:** Nutzer-Prozess setzt `CUDA_PATH_V13_1=C:\tmp\fake_cuda`, erstellt `bin\x64\` mit manipulierter DLL → wird von Whisper.net/sherpa-onnx geladen.

| | |
|---|---|
| **Risiko** | DLL-Injection via PATH-Manipulation; beliebige Code-Ausfuehrung im App-Kontext |
| **Aufwand Fix** | Low (< 1h) |
| **Nutzen Fix** | Hoch -- schliesst lokale Privilege-Escalation/Code-Execution |

**Fix:** Pfade mit `Path.GetFullPath()` kanonisieren, nur Pfade unter `C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA` akzeptieren.

---

### F3-rev -- SHA-256 Hashes bei Model-Downloads nicht genutzt

**Dateien:** `ModelManager.cs:75`, `CorrectionModelManager.cs:91`, `ParakeetModelManager.cs:109`

Infrastruktur in `ModelDownloadHelper` existiert (SHA-256 Parameter), wird aber von keinem der 3 Model-Manager aufgerufen. Alle Downloads laufen ohne Hash-Verifikation.

| | |
|---|---|
| **Risiko** | MITM (Corporate TLS-Inspection) liefert manipulierte Model-Datei → potenzielle Code Execution |
| **Aufwand Fix** | Medium (1-2h, Hashes muessen ermittelt werden) |
| **Nutzen Fix** | Hoch -- Model-Dateien werden als Native Code geladen |

**Fix:** SHA-256 Hashes in `KnownModels`-Arrays hinterlegen, an `DownloadToFileAsync` uebergeben.

---

## Offene Findings -- MEDIUM Severity

### F2-rev -- API Key als Plaintext Cache-Dictionary-Key

**Dateien:** `OpenAiClientFactory.cs`

Cache-Key ist `$"{apiKey}|{endpoint ?? ""}"` -- API-Key direkt als Dictionary-Key im Speicher. Memory-Dumps zeigen den Key im Klartext.

| | |
|---|---|
| **Risiko** | API-Key-Extraktion aus Memory-Dumps; setzt kompromittierte Maschine voraus |
| **Aufwand Fix** | Low (< 30min) |
| **Nutzen Fix** | Mittel -- Defense-in-Depth |

**Fix:** SHA-256-Hash als Cache-Key verwenden statt Raw-Key.

---

### F10-rev -- Fehlende Groessenlimits (Residual)

**Dateien:** `AudioFileReader.cs`, `WriteSpeechOptionsValidator`

Text-Truncation (50K) ist implementiert. Aber: (a) Audio-Dateien werden ohne Size-Check gelesen → OOM moeglich, (b) OGG-Decoder schreibt unbegrenzt in MemoryStream, (c) `MaxRecordingSeconds` hat kein oberes Limit im Validator.

| | |
|---|---|
| **Risiko** | OOM bei sehr grossen Audio-Dateien, lokale DoS |
| **Aufwand Fix** | Low (< 30min) |
| **Nutzen Fix** | Mittel -- verhindert Ressourcen-Erschoepfung |

**Fix:** Max 500 MB in AudioFileReader, MaxRecordingSeconds oberes Limit (7200) im Validator.

---

### F5-rev -- SensitiveIdentifiers Deny-List unvollstaendig (Residual)

**Dateien:** `SourceFileParser.cs:161-175`

Gute Basis-Liste vorhanden. Fehlend: JWT-bezogene (`jwt_secret`, `jwt_token`), OAuth-bezogene (`oauth_token`, `oauth_secret`), Cloud-Provider-spezifische (`aws_access_key`, `github_token`), weitere (`database_password`, `smtp_password`, `certificate_key`).

| | |
|---|---|
| **Risiko** | Sensitive Identifier koennten an Cloud-APIs gesendet werden |
| **Aufwand Fix** | Low (< 15min) |
| **Nutzen Fix** | Mittel -- erweitert Schutz fuer Enterprise-Umgebungen |

**Fix:** Deny-List um ~16 zusaetzliche Eintraege erweitern.

---

### NEW-F15 -- Transcription History unverschluesselt

**Dateien:** `TranscriptionHistoryService.cs`

Transkriptions-History in Plaintext-JSON unter `%APPDATA%/WriteSpeech/transcription-history.json`. Koennte Passwoerter, persoenliche oder medizinische Daten enthalten, die der Nutzer diktiert hat.

| | |
|---|---|
| **Risiko** | Andere Prozesse unter gleichem User-Konto koennen History lesen |
| **Aufwand Fix** | Medium (1-2h, DPAPI Integration + Migration) |
| **Nutzen Fix** | Mittel -- schuetzt sensitive diktierte Inhalte |

**Fix:** DPAPI-Verschluesselung (`ProtectedData.Protect/Unprotect` mit `DataProtectionScope.CurrentUser`).

---

## Offene Findings -- LOW Severity

### NEW-F16 -- Vorhersagbarer Single-Instance Mutex Name

**Dateien:** `App.xaml.cs`

Mutex-Name `"WriteSpeech-SingleInstance"` ist statisch. Jeder Prozess unter gleichem User kann diesen Mutex vorab erstellen und den App-Start blockieren.

| | |
|---|---|
| **Risiko** | Lokale DoS -- App kann nicht gestartet werden |
| **Aufwand Fix** | Low (< 10min) |
| **Nutzen Fix** | Low -- setzt boesartigen lokalen Prozess voraus |

**Fix:** `Local\` Prefix + Username: `$@"Local\WriteSpeech-{Environment.UserName}"`

---

### F7-rev -- Clipboard Exposure Window (Residual)

**Dateien:** `TextInsertionService.cs:73`

`try/finally` ist korrekt implementiert. 200ms Delay vor Wiederherstellung bleibt als Exposure-Window. Inherent bei Clipboard-basiertem Input.

| | |
|---|---|
| **Risiko** | Clipboard-Monitoring kann Text abfangen (200ms Fenster) |
| **Aufwand Fix** | Low (< 5min) |
| **Nutzen Fix** | Low -- reduziert Fenster, eliminiert Problem nicht |

**Fix:** Delay auf 100ms reduzieren, als akzeptiertes Restrisiko dokumentieren.

---

### F11 -- Error Messages leaken Details (**FIXED**)

**Dateien:** `OverlayViewModel.cs`, `FileTranscriptionViewModel.cs`

Exception-Messages wurden direkt in UI angezeigt.

| | |
|---|---|
| **Risiko** | Informations-Disclosure, nur gegenueber lokalem User |
| **Aufwand Fix** | Low (< 30min) |
| **Nutzen Fix** | Low |

**Fix:** Exception-Typen auf user-freundliche Messages mappen.

---

### F12 -- Kein Hotkey-Cooldown (**AKZEPTIERT**)

**Dateien:** `OverlayViewModel.cs`

State Machine verhindert konkurrente Recordings effektiv. `_isTransitioning` Flag blockiert Concurrent-State-Transitions.

| | |
|---|---|
| **Risiko** | Rapid Toggling koennte theoretisch UI-Flicker verursachen |
| **Aufwand Fix** | Low, aber Interaktion mit Push-to-Talk/Error-Recovery komplex |
| **Nutzen Fix** | Sehr Low -- State Machine + `_isTransitioning` sind ausreichender Guard |

**Status:** Akzeptiertes Restrisiko. State Machine ist ausreichender Guard.

---

## INFORMATIONAL (akzeptiert)

### F13 -- Custom Endpoint sendet API Key an beliebige URIs
Beabsichtigtes Feature. Kein Fix noetig.

### F14 -- Registry Auto-Start Entry
Korrekte Implementierung. Kein Fix noetig.

---

## Positive Security Findings

- Kein hardcodierter API-Key oder Debug-Backdoor im gesamten Codebase
- Credentials ausschliesslich aus IOptionsMonitor (DI-Pattern)
- `appsettings.json` korrekt gitignored, Template-Datei tracked
- User-Text in `<transcription>`-Tags isoliert (Prompt Injection Mitigation)
- `MaxInputLength = 50_000` fuer Cloud-Correction implementiert
- `SensitiveIdentifiers` Deny-List filtert Credential-Namen aus IDE-Kontext
- Atomare Settings-Writes via Temp-File + `File.Move`
- Clipboard-Restore in `try/finally` garantiert
- SHA-256 Infrastruktur in `ModelDownloadHelper` vorhanden (muss nur aktiviert werden)
- API-Keys werden nie geloggt (nur Laengen)
- Transkribierter Text wird nicht geloggt (nur Zeichenanzahl)
- Alle Audio-Daten in-memory (keine Temp-Dateien)
- Settings-Persistenz thread-safe via `Lock` + `DebouncedSaveHelper`
- HTTPS fuer alle Model-Downloads
- 7-Tage Log-Rotation in privatem AppData-Verzeichnis
- Sichere JSON-Deserialisierung (`System.Text.Json`, kein `BinaryFormatter`)
- AutoStart korrekt: quoted Path, HKCU-Scope

---

## Priorisierte Umsetzungsreihenfolge

| # | Finding | Severity | Aufwand | Nutzen |
|---|---------|----------|---------|--------|
| 1 | F6-rev: CUDA Path Validation | HIGH | Low | Hoch |
| 2 | F3-rev: SHA-256 Model Hashes | HIGH | Medium | Hoch |
| 3 | F2-rev: Hashed Cache Key | MEDIUM | Low | Mittel |
| 4 | F10-rev: Audio Size + MaxRecording Limits | MEDIUM | Low | Mittel |
| 5 | F5-rev: SensitiveIdentifiers erweitern | MEDIUM | Low | Mittel |
| 6 | F16: Mutex Name | LOW | Low | Low-Mittel |
| 7 | F7-rev: Clipboard Delay reduzieren | LOW | Low | Low |
| 8 | F11: Error Messages | LOW | Low | Low |
| 9 | F12: Hotkey Cooldown | LOW | Low | Low |
| 10 | F15: History Verschluesselung | MEDIUM | Medium | Mittel |

## Implementierungsplan

**Phase 1 -- Quick Wins:** F10-rev, F5-rev, F2-rev, F16, F7-rev
**Phase 2 -- High Impact:** F6-rev, F3-rev
**Phase 3 -- Defense-in-Depth:** F11, F12, F15
