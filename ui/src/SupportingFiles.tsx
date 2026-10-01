import { useEffect, useState } from 'react';
import { agent } from './agent';
import { prepareSupportingFile, supportAccept, supportLimits, validateSupportList } from './supportFiles';
import type { SupportingAttachment } from './types';

export function SupportingFiles({ files, onFiles, onReady, onVision, disabled }: {
  files: SupportingAttachment[]; onFiles: (files: SupportingAttachment[]) => void; onReady: (value: boolean) => void; onVision: (value: boolean) => void; disabled: boolean;
}) {
  const [busy, setBusy] = useState(false);
  const [reviewed, setReviewed] = useState(false);
  const [vision, setVision] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [error, setError] = useState('');
  const images = files.filter(item => item.kind === 'image').length;
  useEffect(() => { onReady(!busy && !dirty && (!files.length || reviewed && (!images || vision))); onVision(Boolean(images && vision)); }, [busy, dirty, files.length, reviewed, images, vision, onReady, onVision]);
  async function preview(candidates: SupportingAttachment[]) {
    validateSupportList(candidates);
    // Local validation only. Vision consent is separately required by run-create and diagnose, not by this preview.
    const normalized = await agent<SupportingAttachment[]>('support-preview', { attachments: candidates });
    onFiles(normalized); setDirty(false); setReviewed(false); setError('');
  }
  async function add(selected: File[]) {
    setBusy(true); setError(''); setReviewed(false); setVision(false);
    try {
      if (files.length + selected.length > supportLimits.files) throw new Error('Maximum 8 optional files.');
      if (images + selected.filter(file => /\.(png|jpe?g)$/i.test(file.name)).length > supportLimits.images) throw new Error('Maximum 4 screenshots.');
      const candidates = [...files];
      for (const file of selected) { candidates.push(await prepareSupportingFile(file)); validateSupportList(candidates); }
      await preview(candidates);
    } catch (reason) { setError(String(reason)); }
    finally { setBusy(false); }
  }
  async function refreshPreview() {
    setBusy(true);
    try { await preview(files); } catch (reason) { setError(String(reason)); } finally { setBusy(false); }
  }
  function remove(id: string) { onFiles(files.filter(item => item.id !== id)); if (files.length === 1) setDirty(false); setReviewed(false); setError(''); }
  return <details className="supporting-files" open={Boolean(files.length)}>
    <summary>Report e screenshot di supporto · opzionali ({files.length}/8)</summary>
    <p>Puoi aggiungere export di CPU-Z, GPU-Z, HWiNFO e altre app, senza aprirle o eseguirle da NeuroTune. Contenuti forniti dall’utente, non misure verificate né prova che provengano da questo PC.</p>
    <label className="support-picker"><span>Aggiungi report o screenshot</span><input type="file" accept={supportAccept} multiple disabled={disabled || busy} onChange={event => { const selected = Array.from(event.target.files ?? []); event.target.value = ''; if (selected.length) void add(selected); }}/></label>
    <p className="muted-copy">Massimo 8 file, di cui 4 immagini. Report TXT/LOG/CSV/JSON/XML/HTML: sorgente ≤512 KiB, 40.000 caratteri per file, 80.000 complessivi; nessun taglio silenzioso. PNG/JPEG: sorgente ≤20 MiB/20 megapixel, preparati in PNG senza metadati aggiuntivi, ≤1600 px per lato e ≤768 KiB. Controlla che il testo rimanga leggibile dopo il ridimensionamento. PDF, archivi e programmi non sono accettati.</p>
    {busy && <p role="status">Preparazione e controllo locale degli allegati… Nessun invio all’AI.</p>}
    {error && <p role="alert" className="error-text">{error}</p>}
    <div className="support-list">{files.map(item => <article className="support-item" key={item.id}>
      <div className="section-heading"><strong>{item.name}</strong><button className="ghost" disabled={disabled || busy} aria-label={`Rimuovi ${item.name}`} onClick={() => remove(item.id)}>Rimuovi</button></div>
      <small>Fornito dall’utente · {item.kind === 'image' ? 'immagine non verificata' : 'testo non verificato'} · SHA-256 del contenuto preparato: <code>{dirty ? 'da ricalcolare' : item.sha256}</code></small>
      {item.kind === 'image' ? <img src={`data:image/png;base64,${item.content}`} alt={`Anteprima dello screenshot ${item.name}`}/> : <label><span>Anteprima testuale modificabile · {item.name} (HTML mai eseguito)</span><textarea value={item.content} maxLength={supportLimits.reportCharacters} disabled={disabled || busy} rows={10} spellCheck={false} onChange={event => { onFiles(files.map(file => file.id === item.id ? { ...file, content: event.target.value } : file)); setDirty(true); setReviewed(false); }}/></label>}
    </article>)}</div>
    {dirty && <button className="secondary" disabled={disabled || busy} onClick={() => void refreshPreview()}>Aggiorna anteprima locale</button>}
    {files.length > 0 && <>
      <p className="muted-copy">La rimozione delle identità/righe sensibili nei report è solo best-effort. Le immagini NON sono anonimizzate: elimina dati personali o ritaglia lo screenshot prima di caricarlo. Nessuna acquisizione automatica del desktop. I testi preparati vengono conservati nelle evidenze del run; i pixel degli screenshot non vengono salvati nello storico locale.</p>
      <label className="consent-toggle"><input type="checkbox" checked={reviewed} disabled={disabled || busy || dirty} onChange={event => setReviewed(event.target.checked)}/><span>Ho controllato le anteprime e autorizzo l’invio di questi contenuti al provider principale quando avvio la diagnosi.</span></label>
      {images > 0 && <label className="consent-toggle"><input type="checkbox" checked={vision} disabled={disabled || busy} onChange={event => setVision(event.target.checked)}/><span>Il modello selezionato supporta immagini. Accetto il costo/consumo aggiuntivo: gli screenshot possono essere inviati a ogni turno dell’indagine. Nessun cambio automatico di modello/provider, OCR separato o scarto silenzioso.</span></label>}
    </>}
  </details>;
}
