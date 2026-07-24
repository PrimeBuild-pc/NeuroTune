# 🚀 Specifica di Progetto: AI-Powered System Optimizer

## 📄 Descrizione Generale

**AI-Powered System Optimizer** è un'applicazione desktop intelligente progettata per analizzare, diagnosticare e ottimizzare automaticamente le prestazioni del sistema operativo, con focus primario su Windows, in base all'hardware specifico, al software installato e alle esigenze reali dell'utente, come gaming ad alte prestazioni, produttività e riduzione della latenza di rete.

A differenza dei tradizionali software di pulizia o ottimizzazione basati su regole rigide e generiche, l'applicazione sfrutta modelli di linguaggio avanzati via API (per esempio OpenRouter, OpenAI, Qwen e Moonshot) per effettuare un'analisi contestualizzata. L'applicazione raccoglie in autonomia la configurazione di sistema e formula strategie di ottimizzazione personalizzate, eliminando la necessità di inserimento manuale dei dati.

---

## 🎯 Scopo del Progetto

Lo scopo è **democratizzare l'amministrazione avanzata e l'ottimizzazione del sistema operativo**, rendendola accessibile agli utenti non tecnici e garantendo sicurezza e trasparenza.

Il progetto mira a risolvere:

- **Complessità tecnica:** evitare modifiche manuali al Registro o l'esecuzione manuale di script.
- **Soluzioni generiche:** personalizzare le modifiche in base a CPU, GPU, RAM, disco e versione del sistema operativo.
- **Perdita di tempo:** automatizzare diagnostica, decisione ed esecuzione in un'esperienza lineare.

---

## 🔑 Funzionalità Principali

### 1. Autenticazione e BYOK (Bring Your Own Key)

- Salvataggio sicuro di API key per provider multipli, tra cui OpenRouter, OpenAI e Anthropic.
- Selezione del modello da utilizzare per la diagnostica.

### 2. Ispezione Automatica del Sistema

Rilevamento automatico senza inserimento manuale di:

- **Hardware:** CPU, GPU e driver, quantità e velocità RAM, NVMe/SSD/HDD.
- **Sistema operativo:** build Windows, piano energetico, sicurezza e telemetria.
- **Rete:** TCP/IP, latenza, DNS e algoritmo di Nagle.
- **Gaming:** Game Mode, Hardware-Accelerated GPU Scheduling (HAGS) e Variable Refresh Rate.
- **Processi e servizi:** software di avvio e carico di sistema.

### 3. Motore di Diagnosi e Strategia LLM

- Diagnosi del profilo per individuare colli di bottiglia, impostazioni non ottimali e servizi superflui.
- Interventi classificati per categoria (Gaming, Rete, Sistema, Privacy) e rischio.

### 4. Preset di Ottimizzazione

- 🟢 **Sicuro / Bilanciato:** esclusivamente ottimizzazioni a basso rischio.
- 🎮 **Extreme Gaming:** riduzione della latenza e delle attività in background non essenziali.
- 🛠️ **Personalizzato:** selezione manuale delle singole azioni proposte.

### 5. Sicurezza e Ripristino

- Creazione obbligatoria di un punto di ripristino e backup delle chiavi di registro coinvolte prima di ogni modifica.
- Rollback dedicato per riportare il sistema allo stato precedente.

---

## 👥 User Journey

1. **Configurazione iniziale:** avvio con privilegi amministrativi e inserimento della API key.
2. **Scansione e analisi:** raccolta automatica del profilo e interrogazione del modello AI.
3. **Selezione e applicazione:** scelta del preset o delle singole azioni.
4. **Esecuzione sicura:** punto di ripristino, applicazione delle modifiche e conferma finale.

---

## 💡 Valore Aggiunto

- **Adattabilità:** raccomandazioni legate alla configurazione reale, incluse architetture CPU ibride e GPU recenti.
- **Indipendenza dai costi di abbonamento:** modello BYOK con pagamento diretto del consumo al provider scelto.
- **Trasparenza:** spiegazione chiara di cosa viene cambiato e del motivo della raccomandazione.
