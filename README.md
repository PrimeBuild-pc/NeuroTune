# NeuroTune

NeuroTune è un'applicazione desktop Windows che raccoglie un profilo tecnico del PC, chiede a un LLM una diagnosi contestuale e applica soltanto ottimizzazioni locali predefinite, verificabili e reversibili.

## Sicurezza prima delle prestazioni

- L'AI **non può eseguire comandi**: può scegliere esclusivamente gli `ActionId` del catalogo interno.
- Prima di ogni modifica sono obbligatori un punto di ripristino e il backup delle chiavi di registro coinvolte.
- Ogni operazione salva lo stato precedente e offre rollback dalla cronologia.
- Le API key sono cifrate con Windows DPAPI per l'utente corrente.
- Il profilo mostrato nell'app è lo stesso inviato al provider; username e nome del PC vengono redatti.
- NeuroTune non include telemetria proprietaria.

> Le ottimizzazioni di sistema comportano sempre un rischio. Usare prima su una macchina virtuale o su un PC con backup aggiornato.

## Requisiti

- Windows 10 o Windows 11 x64
- Privilegi amministrativi
- Protezione sistema abilitata sull'unità Windows
- API key OpenRouter, OpenAI o Anthropic
- .NET 8 SDK solo per compilare da sorgente

## Sviluppo

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

Avvio in sviluppo, da un terminale amministrativo:

```powershell
dotnet run --project src/NeuroTune
```

Pubblicazione self-contained:

```powershell
dotnet publish src/NeuroTune -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Flusso utente

1. Seleziona provider e modello, quindi salva la API key.
2. Premi **Analizza sistema** e controlla il profilo inviato.
3. Scegli un preset o seleziona manualmente le azioni.
4. Conferma l'applicazione; NeuroTune crea i backup prima di procedere.
5. Usa **Cronologia** per ripristinare un'operazione.

## Dati locali

Configurazione, chiavi cifrate, log e manifest di rollback sono salvati in `%LocalAppData%\NeuroTune`. Nessuna API key viene salvata nella repository.

## Stato MVP

Il catalogo iniziale privilegia poche modifiche documentate e ripristinabili: piano energetico, Game Mode, HAGS, Game DVR ed effetti visivi. Tweak di rete generici, script LLM e pulizie distruttive sono deliberatamente esclusi.

Vedi [piano implementativo](docs/IMPLEMENTATION_PLAN.md), [specifica](.claude/PROJECT_SPECIFICATION.md) e [policy di sicurezza](SECURITY.md).
