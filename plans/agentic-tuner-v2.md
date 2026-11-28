# NeuroTune v2 — tuner agentico completo

Stato: approvato in discussione il 2026-10-12. Piano di direzione, una PR per fase.

## Obiettivo

Sostituire i tool a pagamento dei "tweaker". NeuroTune raccoglie dati reali su PC e gioco (inventario, ETW/DPC,
PresentMon, topologia, BIOS), poi guida un agente Pi con il modello scelto dall'utente attraverso passi strutturati.
L'agente può toccare qualsiasi parametro modificabile: driver GPU, NIC, IRQ, servizi, app, giochi, Windows, Defender
(con conferma) e BIOS (prima come checklist). Ogni scrittura deve essere annullabile.

## Principi v2 (sostituiscono "Evidence before claims")

1. **I preset sono prompt.** Il preset contiene la checklist minima e i passi da seguire. Il catalogo non decide quali
   tweak applicare.
2. **Le misure sono contesto, non un cancello.** Servono all'agente per intervenire in modo mirato e a chiudere il ciclo
   misura → intervento → misura. L'Apply non richiede più 3 Baseline valide.
3. **Libertà di lettura quasi totale.** La checklist è il minimo: l'agente deve indagare oltre e dichiarare cosa ha
   controllato fuori lista.
4. **Libertà di scrittura con rollback garantito dal programma, non dal modello.** Le scritture sono operazioni base
   tipizzate con parametri liberi. Lo script libero è ammesso solo con i target dichiarati e il rollback verificato.
5. **Rischio anti-cheat e Defender accettato** sotto conferma esplicita dell'utente.

## Cosa resta, cosa cambia

| Componente | Destino |
|---|---|
| `IReversibleAction`, `OptimizationEngine`, `BackupService`, `JournalStorage` | **Resta**: è il nucleo del rollback. Le operazioni base sono nuove `IReversibleAction`. |
| ID parametrici esistenti (`power.custom.<hash>`, `gaming.gpu-<hash>.*` + `RecoveryTarget`) | **Resta**: è già il pattern giusto, va generalizzato. |
| `PiAgentBridge`, `OptionalWorkerProcess`, `PlannerProtocol` | **Resta**, con nuovi tipi di turno. |
| `InvestigationService` (lettori fissi) | **Resta** come strumenti curati, affiancato dalla lettura libera. |
| `MeasurementService`, `TraceAnalyzer`, `PresentMonImporter`, `LiveFrameRate` | **Resta**: alimenta il ciclo chiuso. |
| `DeviceControl` | **Resta**: diventa l'operazione `device.disable` con lo stesso allowlist e le stesse guardie. |
| `ScewinService`, `FirmwareInspection`, manuali PDF, allegati | **Resta**: base della checklist BIOS. |
| `ConflictAnalyzer` | **Evolve** in pre-analisi deterministica ("linter") dei risultati. |
| `ScriptReviewService` | **Evolve** in un controllo AST bloccante per gli script dichiarati. |
| `GamingContextDetector` | **Resta**: collega l'exe in esecuzione al pacchetto gioco. |
| `OptimizationRun` (macchina a stati) | **Cambia**: si rimuove il cancello `BaselinePending → BaselineReady → Approved`. |
| `ActionPolicy.ForbiddenPerformanceTargets` | **Cambia**: `defender` resta vietato tranne `defender.exclusion`. |
| `LlmClient.AnalysisFocus` + prompt inline | **Cambia**: diventano file di preset e passaggi per dominio. |
| Avvertenze nel prompt che frenano il modello ("No change is a valid outcome…") | **Si riduce** a regole di sicurezza reali. |

Niente da buttare: si tolgono i vincoli, non il codice.

---

## F1 — Operazioni base parametriche (fondazione, blocca tutto il resto)

Una `IReversibleAction` per tipo, con parametri validati. L'ID resta autoesplicativo: tipo + hash dei parametri,
come `power.custom`. I parametri completi vanno nel journal.

| Tipo | Parametri | Note |
|---|---|---|
| `registry.set` / `registry.delete` | hive, percorso, nome, tipo, valore | Capture distingue "assente" (al restore va cancellata) da "presente". |
| `service.start-type` | nome, StartType | + stato in esecuzione; dipendenze lette prima. |
| `netadapter.property` | adapter (GUID da PnP), RegistryKeyword, valore | `Get/Set-NetAdapterAdvancedProperty`. |
| `qos.policy` | exe, DSCP | `New-NetQosPolicy`. |
| `powercfg.setting` | GUID subgroup/setting, AC/DC | Sul piano attivo o personalizzato. |
| `bcd.option` | elemento, valore | Lista ristretta di elementi. |
| `startup.entry` | Run/StartupApproved, nome | Disattivazione nativa reversibile. |
| `scheduled-task.state` | percorso task | Enable/Disable. |
| `appcompat.layer` / `ifeo.perf` | exe, flag | Fullscreen optimizations, priorità per exe. |
| `file.json` / `file.ini` | percorso (sotto radici consentite), chiave, valore | Config giochi e app; backup del file intero. |
| `device.disable` | slug esistente | Migrazione di `DeviceControl`. |
| `defender.exclusion` | percorso/processo | Vedi F7. |

- `ActionRecord` riceve `Kind` + `Parameters` (JSON). `OptimizationCatalog.ResolveRollback` ricostruisce l'azione da
  lì. Lo schema journal va versionato e **il lettore accetta v1 e v2**, per non ripetere il blocco dei journal legacy.
- Lista di cose vietate per tipo, non per singolo tweak: `HKLM\SAM`, `SECURITY`, `Lsa`, `CI`/`DeviceGuard` (tranne lettura),
  servizi di boot critici (RpcSs, DcomLaunch, LSM, BFE…), percorsi di sistema per `file.*`.
- `ActionPolicy` resta: il rischio deriva dal tipo e dal target (servizio critico = High), con conferma separata.
- **Test:** per ogni tipo, Capture/Apply/Verify/Restore su target di prova (`HKCU\Software\NeuroTuneTest`, servizio
  fittizio, file temporaneo), più i casi "valore assente" e "chiave assente".
- **Fatto quando:** il modello può proporre `registry.set` su una chiave qualsiasi consentita e il rollback ripristina
  esattamente lo stato precedente, compresa la cancellazione dei valori che prima non esistevano.

**Stato F1 (2026-10-12): fatto, PR #31.** `ParametricActions.cs`, journal v4 (legge v3), `Parametric` nella richiesta
`apply`. Differenze: `device.disable` mantiene gli ID `device.disable.<slug>`; `defender.exclusion` c'è come primitiva,
il flusso F7 no. VBS consentito su richiesta di Lorenzo (DeviceGuard + `hypervisorlaunchtype`), servizi protetti ridotti a quelli di boot, WMI e Defender. Test NIC/Defender solo in VM (`NEUROTUNE_TEST_NETADAPTER`, `NEUROTUNE_TEST_DEFENDER`).

## F2 — Lettura libera per l'agente

- Nuovo strumento `powershell-read` in `InvestigationService`. Il comando viene analizzato dal parser AST di PowerShell,
  invocando `powershell.exe` (5.1, sempre presente) per estrarre i nomi dei comandi. Così non serve aggiungere
  `Microsoft.PowerShell.SDK`.
- Lista consentita: `Get-*` selezionati, `reg query`, `powercfg /q /l /a`, `pnputil /enum-*`, `netsh … show`,
  `bcdedit /enum`, `driverquery`, `wevtutil qe`, `nvidia-smi -q`. Rifiutati pipe verso comandi non consentiti,
  `Invoke-*`, `iex`, `Start-Process`, `Set-*`, `New-*`, `Remove-*` e i `Get-*` con effetti collaterali (da verificare uno per uno).
- Processo non admin per default, admin solo per i comandi che lo richiedono (lista). Limiti di tempo e output;
  si riusa la redazione esistente.
- Output con il solito ID `investigation:` per le citazioni.
- **Fatto quando:** l'agente può leggere chiavi/valori, servizi, proprietà NIC e configurazioni non previsti dai lettori
  fissi, e un comando di scrittura viene rifiutato prima di essere eseguito.

## F3 — Preset come prompt e pipeline per dominio

- Cartella `presets/` con un file Markdown per preset: `competitive-fps`, `system-latency`, `network`,
  `stability`, `streaming`, `hardware-layout`. Ogni file contiene: obiettivo, checklist obbligatoria per dominio,
  segnali da cercare, formato di output, sezione "oltre la checklist". `LlmClient` li carica al posto di `AnalysisFocus`.
- Pipeline in `LlmClient` / Pi:
  1. **Raccolta deterministica:** profilo, tracce, PresentMon, topologia, pacchetto gioco.
  2. **Pre-analisi:** `ConflictAnalyzer` produce risultati tipizzati (gravità, evidenza, operazione suggerita).
  3. **Passaggi per dominio:** GPU/driver, rete, IRQ/input/USB, CPU/scheduler/power, memoria, audio, app/servizi,
     gioco, BIOS. Ogni passaggio ha il proprio prompt, un budget di turni, accesso ai lettori e produce proposte tipizzate.
  4. **Critico:** conflitti tra domini, coerenza con gioco e anti-cheat, duplicati.
  5. **Piano finale** a livelli Sicuro / Consigliato / Aggressivo.
- `PlanRecommendation`: aggiungere `Parameters` per le operazioni base. La validazione esistente (`ParseDiagnosis`)
  controlla tipo, parametri e lista vietata.
- Opzione "modello piccolo": 3 campionamenti della fase 3 con voto per proposta. Si aggiunge solo se l'eval (F11)
  mostra che serve.
- **Fatto quando:** una diagnosi completa produce proposte con parametri per almeno 5 domini, con un modello locale da
  circa 14B che non fallisce il parsing.

**Stato F3 (2026-10-13): in parte.** Preset in `presets/*.md`, proposte parametriche validate dalla diagnosi fino
all'Apply, obiettivi Competitive FPS e Streaming. I passaggi per dominio, il critico e i 3 livelli sono solo istruzioni
del preset dentro un unico ciclo Pi, non chiamate separate (da rivalutare con F11).

## F4 — Ciclo chiuso e rimozione del cancello Baseline

- `OptimizationRun`: l'Apply è consentito da `ProposalReady`. Le Baseline sono evidenza allegata, non un requisito.
  Vanno aggiornati i test della macchina a stati e i documenti.
- Misura rapida predefinita: 60 s con `LiveFrameRate`/PresentMon (FPS medi, 1% low, frame-time P99). Il pacchetto ETW
  3×180 s resta per la diagnosi approfondita di latenza.
- Nuovo turno `Refine`: dopo Apply + misura, l'agente riceve il diff prima/dopo e propone aggiustamenti o il rollback di
  singole voci. Massimo 3 iterazioni, poi Keep/Rollback dell'utente come oggi.
- Benchmark ripetibili dove il gioco lo consente (replay Fortnite, mappa benchmark CS2): istruzioni nel pacchetto gioco (F9).
- **Fatto quando:** il flusso scansione → piano → apply → misura → refine si completa senza Baseline obbligatorie e il
  rollback per singola voce funziona.

## F5 — Domini ad alto impatto per il gioco competitivo

Quasi tutto passa dalle operazioni base di F1; qui servono solo validazioni specifiche e conoscenza nei prompt.

- **MSI mode e affinity IRQ:** `registry.set` su `Enum\…\Device Parameters\Interrupt Management` (già letto in
  `HardwareTopologyService`). Validazione: il dispositivo supporta MSI, riavvio richiesto. Prima/dopo con la
  distribuzione DPC/ISR di `TraceAnalyzer`.
- **NIC:** `netadapter.property` (interrupt moderation, EEE/Green, flow control, RSS, offload, buffer) + `qos.policy`.
- **NVIDIA:** profili driver (DRS). Opzioni da valutare:
  - (a) P/Invoke su `nvapi64.dll` del driver;
  - (b) Profile Inspector in CLI con import/export `.nip`.

  Capture = esportazione del profilo globale e del profilo del gioco. Verificare licenze e stabilità prima di scegliere.
- **AMD:** ADLX SDK tramite un piccolo helper nativo. Dopo NVIDIA, salvo diversa priorità.
- **Audio:** miglioramenti degli endpoint (`MMDevices\Audio\Render\…\FxProperties`). **Da verificare:** i permessi su
  quelle chiavi (proprietario Audiosrv/TrustedInstaller).
- **App:** accelerazione hardware e overlay (Discord `settings.json`, policy Chrome/Edge, Steam), avvio automatico,
  Game Bar/NVIDIA overlay.
- **Per exe:** `appcompat.layer` (FSO), GPU preference (esiste già), `ifeo.perf`.
- **Fatto quando:** su un PC NVIDIA con Fortnite il piano tocca profilo driver, NIC, MSI, overlay e config del gioco, e
  ogni voce si annulla singolarmente.

## F6 — Script libero con target dichiarati

- Nuovo `PlanRecommendationKind.DeclaredScript`: script PowerShell + target dichiarati (stessi tipi di F1) + script di
  rollback scritto dal modello.
- Esecuzione:
  1. controllo AST bloccante (`ScriptReviewService` evoluto): niente download+esecuzione, niente cancellazioni fuori dai
     target, niente `bcdedit /delete`, formattazioni o modifiche ad account e credenziali;
  2. snapshot dei target con il `Capture` delle operazioni base;
  3. esecuzione elevata;
  4. per i target senza riavvio: rollback del modello → confronto con lo snapshot → riapplicazione. Se il confronto
     fallisce, vale il rollback di NeuroTune;
  5. durante l'esecuzione, traccia ETW `Microsoft-Windows-Kernel-Registry` per segnalare le scritture non dichiarate
     (segnala soltanto: l'evento non contiene il valore precedente).
- Interfaccia: solo modalità Expert, script + riassunto in italiano + conferma separata, etichetta "rollback non garantito"
  se ci sono scritture fuori dichiarazione.
- Uno script che funziona su più PC diventa candidato a una nuova riga di catalogo o a un nuovo tipo di F1.

## F7 — Defender

- `defender.exclusion` (percorso o processo), con journal e rimozione al termine dell'operazione che la richiede.
  È la strada preferita per WinRing0 e i tool bloccati.
- Preferire PawnIO dove esiste già il percorso (vedi `PAWNIO_TRUST_REVIEW.md`).
- Disattivazione temporanea guidata, come ultima scelta: apri `windowsdefender://threatsettings`, l'utente spegne
  Tamper Protection e la protezione in tempo reale, NeuroTune controlla con `Get-MpComputerStatus`, esegue, poi mostra un
  banner persistente finché la protezione non risulta riattivata.
- **Da verificare sulla build attuale:** che Tamper Protection blocchi davvero `Set-MpPreference` e che
  `Add-MpPreference -ExclusionPath` funzioni da admin senza gestione Intune.

## F8 — BIOS, fase 1 (nessuna scrittura)

- Nuovo tipo di output `BiosChecklist`: impostazione, valore, percorso dei menu, motivo, fonte (dump SCEWIN, pagina del
  manuale PDF, conoscenza generale = non verificata).
- Contenuto tipico: EXPO/XMP, PBO (base), ReBAR/Above 4G, C-state/ASPM/ErP, controller e dispositivi inutilizzati,
  SVM/VT-d se non usati. **Mai** Secure Boot/TPM (anti-cheat), voltaggi o timing manuali.
- Pulsante "Riavvia nel BIOS": `shutdown /r /fw /t 0` dopo conferma, con la checklist salvata e consultabile da telefono
  (esportazione o QR del report HTML).
- Verifica al riavvio successivo, solo lettura:
  - RAM: `Win32_PhysicalMemory.ConfiguredClockSpeed` rispetto al profilo XMP/EXPO;
  - ReBAR;
  - virtualizzazione: `VirtualizationFirmwareEnabled`;
  - C-state: per quanto osservabile.

  Le voci non osservabili vengono segnate come tali.
- Dopo EXPO/PBO/Curve Optimizer: test di stabilità guidato (OCCT/TM5 manuale) prima del ciclo di misura.
- **Fase 2 (scrittura SCEWIN su schede AMI):** piano separato dopo che la fase 1 è stata provata su schede reali.

## F9 — Pacchetti gioco e ricerca web

- `games/<id>.json`: exe, launcher, percorsi e formato della config, impostazioni competitive, anti-cheat e relativi
  vincoli, sincronizzazione cloud, metodo di benchmark ripetibile, CPU-bound/GPU-bound tipico.
- Primi 5 pacchetti: Fortnite, CS2, Valorant, Apex, Call of Duty/Warzone. `GamingContextDetector` li collega.
- Ricerca web come strumento Pi (Brave/Tavily con chiave dell'utente, oppure ricerca nativa del provider se disponibile)
  per giochi senza pacchetto o per verificare aggiornamenti. Il contenuto web è un dato: può produrre solo proposte
  tipizzate, mai comandi.

## F10 — Persistenza

- All'avvio dell'app, e opzionalmente con un task al logon, si esegue `Verify()` sulle voci dell'ultima operazione
  mantenuta. Se un aggiornamento di Windows o del driver ha annullato qualcosa, NeuroTune avvisa e propone di riapplicare.
  Lo snapshot del journal fornisce già lo "stato atteso".

## F11 — Suite di valutazione (scheletro subito dopo F3)

- `tests/eval/`: profili di sistema (reali anonimizzati + sintetici) con voci attese (devono esserci / non devono
  esserci).
- Runner manuale, fuori dalla CI per i costi: esegue la pipeline con il provider scelto e misura recall delle voci
  attese, violazioni, errori di parsing e turni usati.
- Serve a non regredire quando si cambiano i prompt: una modifica che migliora GPT può peggiorare Qwen.

---

## Ordine

F1 → F2 + F3 (in parallelo) → F11 scheletro → F4 → F5 → F7 → F6 → F8 → F9 → F10.

F7 viene prima di F6 perché è piccolo e sblocca i tool bloccati da Defender. F8 è indipendente e può essere anticipato
se serve una funzione visibile presto.

## Documentazione da aggiornare a ogni fase

README (sezioni principi e trust boundaries), `docs/DETERMINISTIC_CONTROLS.md`, `docs/DEVICE_CONTROLS.md`, `SECURITY.md`,
`RELEASE_NOTES.md`. Le note che diventano obsolete vanno cancellate, non lasciate.

## Rischi aperti

- **NVIDIA DRS:** API e licenza da verificare prima di F5.
- **Permessi su `MMDevices`:** potrebbero richiedere di prendere ownership delle chiavi (rischio più alto, va dichiarato).
- **Journal v2:** la compatibilità con i v1 va testata in VM con journal reali.
- **Defender e anti-cheat:** comportamento da confermare sulla build di Windows corrente.
- **Lettura libera:** alcuni `Get-*` hanno effetti collaterali; la lista consentita va costruita a mano e testata.
- **Test di scrittura:** le prove con permessi elevati restano da fare in VM, non sul PC principale.
