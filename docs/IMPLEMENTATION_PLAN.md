# Piano implementativo

## Decisioni MVP

- Windows 10/11 x64, .NET 8 e WPF.
- Singola applicazione amministrativa, nessun backend e nessun database.
- Provider BYOK: OpenRouter, OpenAI e Anthropic.
- Profilo tramite API Windows/.NET; configurazione JSON e segreti DPAPI.
- Catalogo locale chiuso: l'LLM diagnostica e raccomanda, ma non genera modifiche eseguibili.
- Punto di ripristino, backup del registro, journal per azione e rollback inverso.

## Fasi

1. Inizializzare soluzione, repository e documentazione.
2. Definire profilo, diagnosi, azioni, preset e manifest.
3. Implementare configurazione e API key cifrate.
4. Raccogliere hardware, Windows, rete, processi, avvio e servizi.
5. Implementare il catalogo di azioni reversibili.
6. Integrare i tre provider e validare rigorosamente il JSON.
7. Filtrare le raccomandazioni per preset e rischio.
8. Rendere obbligatori punto di ripristino e backup del registro.
9. Applicare le azioni in sequenza con verifica e rollback automatico.
10. Esporre configurazione, analisi, applicazione e cronologia nella UI WPF.
11. Redigere identità Windows, loggare localmente e gestire timeout/errori.
12. Testare i confini critici e compilare su GitHub Actions.
13. Pubblicare un artefatto self-contained `win-x64` con checksum.

## Criteri di completamento MVP

- Build e test passano su Windows.
- Nessuna chiave compare nei file versionati o nei log.
- Una risposta LLM con `ActionId` sconosciuto viene rifiutata.
- Nessuna modifica viene applicata quando il backup obbligatorio fallisce.
- Ogni azione applicata conserva lo stato necessario al rollback.
- La pipeline produce un eseguibile self-contained e relativo SHA-256.

## Rinviato intenzionalmente

Installer, firma digitale, aggiornamenti automatici, plugin e cataloghi remoti. Andranno aggiunti solo quando esistono certificato, canale di distribuzione e requisiti verificabili.
