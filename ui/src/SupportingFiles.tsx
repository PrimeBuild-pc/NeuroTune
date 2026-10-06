import { useEffect, useState } from 'react';
import { agent } from './agent';
import { t } from './i18n';
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
      if (files.length + selected.length > supportLimits.files) throw new Error(t('Maximum 8 optional files.'));
      if (images + selected.filter(file => /\.(png|jpe?g)$/i.test(file.name)).length > supportLimits.images) throw new Error(t('Maximum 4 screenshots.'));
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
  return <section className="supporting-files">
    <h2>{t('Supporting reports and screenshots · optional ({count}/8)', { count: files.length })}</h2>
    <p>{t('You can add exports from CPU-Z, GPU-Z, HWiNFO and other apps without opening or running them from NeuroTune. User-provided content, not verified measurements or proof that it comes from this PC.')}</p>
    <label className="support-picker"><span>{t('Add reports or screenshots')}</span><input type="file" accept={supportAccept} multiple disabled={disabled || busy} onChange={event => { const selected = Array.from(event.target.files ?? []); event.target.value = ''; if (selected.length) void add(selected); }}/></label>
    <p className="muted-copy">{t('Maximum 8 files, including 4 images. TXT/LOG/CSV/JSON/XML/HTML reports: source ≤512 KiB, 40,000 characters per file, 80,000 combined; no silent truncation. PNG/JPEG: source ≤20 MiB/20 megapixels, prepared as PNG without extra metadata, ≤1600 px per side and ≤768 KiB. Check that text remains readable after resizing. PDFs, archives and programs are not accepted.')}</p>
    {busy && <p role="status">{t('Preparing and checking attachments locally… Nothing sent to AI.')}</p>}
    {error && <p role="alert" className="error-text">{error}</p>}
    <div className="support-list">{files.map(item => <article className="support-item" key={item.id}>
      <div className="section-heading"><strong>{item.name}</strong><button className="ghost" disabled={disabled || busy} aria-label={t('Remove {name}', { name: item.name })} onClick={() => remove(item.id)}>{t('Remove')}</button></div>
      <small>{t('User-provided · {kind} · SHA-256 of prepared content:', { kind: item.kind === 'image' ? t('unverified image') : t('unverified text') })} <code>{dirty ? t('to be recalculated') : item.sha256}</code></small>
      {item.kind === 'image' ? <img src={`data:image/png;base64,${item.content}`} alt={t('Screenshot preview {name}', { name: item.name })}/> : <label><span>{t('Editable text preview · {name} (HTML never executed)', { name: item.name })}</span><textarea value={item.content} maxLength={supportLimits.reportCharacters} disabled={disabled || busy} rows={10} spellCheck={false} onChange={event => { onFiles(files.map(file => file.id === item.id ? { ...file, content: event.target.value } : file)); setDirty(true); setReviewed(false); }}/></label>}
    </article>)}</div>
    {dirty && <button className="secondary" disabled={disabled || busy} onClick={() => void refreshPreview()}>{t('Refresh local preview')}</button>}
    {files.length > 0 && <>
      <p className="muted-copy">{t('Removal of identities/sensitive lines in reports is best-effort only. Images are NOT anonymized: remove personal data or crop the screenshot before uploading. No automatic desktop capture. Prepared texts are retained in run evidence; screenshot pixels are not saved in local history.')}</p>
      <label className="consent-toggle"><input type="checkbox" checked={reviewed} disabled={disabled || busy || dirty} onChange={event => setReviewed(event.target.checked)}/><span>{t('I reviewed the previews and authorize sending this content to the main provider when I start diagnosis.')}</span></label>
      {images > 0 && <label className="consent-toggle"><input type="checkbox" checked={vision} disabled={disabled || busy} onChange={event => setVision(event.target.checked)}/><span>{t('The selected model supports images. I accept the additional cost/usage: screenshots may be sent at every investigation turn. No automatic model/provider switch, separate OCR or silent discard.')}</span></label>}
    </>}
  </section>;
}
