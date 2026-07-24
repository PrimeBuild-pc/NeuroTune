# Sicurezza

## Segnalare una vulnerabilità

Non aprire issue pubbliche contenenti API key, dati personali o dettagli immediatamente sfruttabili. Invia una segnalazione privata tramite la funzione **Security advisories** della repository GitHub.

Indica versione, passaggi di riproduzione, impatto previsto e un contatto. Non includere dump completi del profilo Windows.

## Modello di sicurezza

- La risposta LLM è trattata come input non attendibile.
- Sono accettati soltanto identificativi presenti nel catalogo compilato nell'app.
- Parametri, percorsi e comandi arbitrari provenienti dal modello non vengono eseguiti.
- Il motore interrompe l'operazione se non riesce a creare il punto di ripristino o i backup richiesti.
- Le API key sono protette con DPAPI `CurrentUser` e redatte dai log.

## Limiti noti

NeuroTune richiede privilegi amministrativi perché modifica impostazioni di sistema. Un account Windows già compromesso o un eseguibile manomesso può aggirare le protezioni applicative. Le build non firmate devono essere verificate con il checksum pubblicato dalla pipeline.
