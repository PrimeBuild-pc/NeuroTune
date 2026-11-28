# F8 fase 2 — scrittura impostazioni BIOS con SCEWIN (bozza da approvare)

Stato: bozza del 2026-10-13. Il piano v2 richiede questo piano separato **dopo** aver provato la fase 1 su schede reali.
Nessuna riga di codice finché Lorenzo non approva e non sceglie la scheda di prova.

## Prerequisiti

- Fase 1 provata su almeno una scheda reale: export SCEWIN, checklist, "Riavvia nel BIOS", "Controlla dopo il riavvio".
  Candidata: questo PC (MSI MPG X570 GAMING EDGE WIFI, AMI 1.S1 dell'11/09/2025, BitLocker spento, Secure Boot attivo).
- SCEWIN fornito dall'utente, con gli stessi controlli SHA-256 dell'export (`ScewinService.RequiredFiles`).

## Primitiva `bios.scewin`

| Parametro | Regola |
|---|---|
| `question` | Testo esatto di una "Setup Question" presente nell'ultimo export della stessa scheda e versione BIOS. |
| `value` | Una delle opzioni elencate per quella domanda (`[NN]Testo`), mai un valore libero. |

- **Ammesse solo** le domande della checklist fase 1: EXPO/XMP (solo il profilo, mai i timing), ReBAR/Above 4G,
  C-state globali, ASPM, ErP, SVM/IOMMU, controller integrati non usati (audio, Wi-Fi/BT, seconda LAN, RGB).
  **Mai**: Secure Boot, TPM/fTPM, password, voltaggi, frequenze, timing, curve, chiavi, boot order.
- Capture: export completo `/o /s` appena prima, salvato col journal insieme a SMBIOS (`Win32_BIOS`, `Win32_BaseBoard`).
- Apply: file di import con le sole domande cambiate, `SCEWIN_64.exe /i /s <file>`; poi riavvio richiesto.
- Verify (dopo il riavvio): nuovo export e confronto del valore; se scheda o versione BIOS sono cambiate, la voce è
  "non verificabile" e il rollback automatico è bloccato.
- Restore: import dei valori catturati, solo se la versione BIOS è la stessa; altrimenti istruzioni manuali.

## Sicurezza

- BitLocker attivo: sospendere per un riavvio (`manage-bde -protectors -disable C: -RebootCount 1`) con conferma,
  altrimenti al riavvio può chiedere la chiave di ripristino.
- Prima dell'Apply: checklist HTML scaricabile con valori originali e percorso dei menu, per il ripristino a mano.
- Se il PC non si avvia: Clear CMOS dal manuale della scheda; lo si dice prima della conferma.
- Rischio High, conferma separata, solo Expert. Mai più di 5 domande per operazione.

## Test

- Unit: validazione domanda/valore contro un export di esempio, file di import generato, regole vietate.
- Reale (solo con il consenso di Lorenzo): su questo PC una sola impostazione innocua e reversibile (es. ErP),
  Apply → riavvio → Verify → Restore → riavvio → Verify.

## Domande per Lorenzo

1. Va bene provare prima la fase 1 e poi una scrittura innocua su questo PC?
2. La lista delle domande ammesse va bene, o includere anche PBO base (solo Enabled/Disabled)?
