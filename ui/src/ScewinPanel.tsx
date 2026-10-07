import { useEffect, useRef, useState } from 'react';
import { listen } from '@tauri-apps/api/event';
import { agent, newRequestId } from './agent';
import { formatDate, t } from './i18n';

function settingStatus(value: string) {
  return ['Reported by export', 'Unknown — no marked current value', 'Unknown — conflicting current-value markers'].includes(value) ? t(value) : value;
}

interface ToolFile { name: string; sha256: string; bytes: number; }
type DisplayMessage = { source: string } | { raw: string };
export interface ScewinReport {
  source: string; readAtUtc: string; exportSha256: string; sensitiveQuestionsOmitted: number;
  toolFiles: ToolFile[]; notes: string[];
  settings: { question: string; token: string; currentValue: string | null; status: string }[];
}

export function ScewinSettings({ report }: { report: ScewinReport }) {
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(0);
  const matching = report.settings.filter(item => `${item.question} ${item.currentValue ?? ''}`.toLowerCase().includes(search.toLowerCase()));
  const pages = Math.max(1, Math.ceil(matching.length / 50));
  const visiblePage = Math.min(page, pages - 1);
  return <section className="scewin-report"><h4>{t('SCEWIN setup observations · {count} questions', { count: report.settings.length })}</h4>
    <p className="muted-copy">{report.source}<br/>{t('{date} · {count} sensitive questions omitted. No setting can be edited or applied here.', { date: formatDate(report.readAtUtc), count: report.sensitiveQuestionsOmitted })}</p>
    <p role="status">{t('Reported current values: {known}/{total}. Missing or conflicting values remain unknown.', { known: report.settings.filter(item => item.currentValue != null).length, total: report.settings.length })}</p>
    <label><span>{t('Filter setup questions')}</span><input value={search} onChange={event => { setSearch(event.target.value); setPage(0); }} placeholder={t('Example: PBO, C-state, memory')}/></label>
    <dl className="firmware-facts">{matching.slice(visiblePage * 50, (visiblePage + 1) * 50).map((item, index) => <div key={index}>
      <dt>{item.question}<small>{t('Token: {token}', { token: item.token || t('Unavailable') })}</small></dt>
      <dd>{item.currentValue ?? t('Unknown')}<small>{settingStatus(item.status)}</small></dd>
    </div>)}</dl>
    {!matching.length && <p>{t('No matching setup questions.')}</p>}
    <div className="button-row"><button className="secondary" disabled={visiblePage === 0} onClick={() => setPage(visiblePage - 1)}>{t('Previous settings')}</button><span>{t('Page {page} / {pages} · {count} matching', { page: visiblePage + 1, pages, count: matching.length })}</span><button className="secondary" disabled={visiblePage + 1 >= pages} onClick={() => setPage(visiblePage + 1)}>{t('Next settings')}</button></div>
    <details><summary>{t('Export provenance and limitations')}</summary><code className="export-hash">SHA-256: {report.exportSha256}</code><ul className="muted-copy">{report.notes.map(note => <li key={note}>{note}</li>)}</ul></details>
  </section>;
}

export function ScewinPanel({ readConsent, onBusyChange }: { readConsent: boolean; onBusyChange: (busy: boolean) => void }) {
  const [directory, setDirectory] = useState('');
  const [files, setFiles] = useState<ToolFile[]>();
  const [riskAccepted, setRiskAccepted] = useState(false);
  const [report, setReport] = useState<ScewinReport>();
  const [importInfo, setImportInfo] = useState<{ name: string; bytes: number; encoding: string }>();
  const [busy, setBusy] = useState<DisplayMessage>();
  const [error, setError] = useState<DisplayMessage>();
  const request = useRef<string | undefined>(undefined);
  useEffect(() => {
    const unlisten = listen<{ requestId: string; message: string }>('agent-progress', event => {
      if (event.payload.requestId === request.current) setBusy({ raw: event.payload.message });
    });
    return () => { void unlisten.then(stop => stop()); };
  }, []);
  async function run(command: string, payload: unknown, prepare?: () => Promise<unknown>) {
    if (request.current) return;
    const id = newRequestId(); request.current = id; onBusyChange(true); setBusy({ source: 'Processing locally…' }); setError(undefined);
    if (command !== 'scewin-inspect') { setReport(undefined); setImportInfo(undefined); }
    try {
      if (prepare) payload = await prepare();
      if (command === 'scewin-inspect') { setFiles(undefined); setRiskAccepted(false); setFiles(await agent<ToolFile[]>(command, payload, id)); }
      else setReport(await agent<ScewinReport>(command, payload, id));
    } catch (reason) { setError({ raw: String(reason) }); }
    finally { request.current = undefined; setBusy(undefined); onBusyChange(false); }
  }
  async function importFile(file: File) {
    if (!readConsent || file.size === 0 || file.size > 16 * 1024 * 1024) { setError({ source: 'Enable firmware reading and select a nonempty export of at most 16 MiB.' }); return; }
    await run('scewin-import', undefined, async () => {
      const bytes = new Uint8Array(await file.arrayBuffer());
      const encoding = bytes[0] === 0xff && bytes[1] === 0xfe ? 'utf-16le' : bytes[0] === 0xfe && bytes[1] === 0xff ? 'utf-16be' : 'utf-8';
      let text: string;
      try { text = new TextDecoder(encoding, { fatal: true }).decode(bytes); }
      catch { throw new Error(t('The export is not valid UTF-8 or BOM-marked UTF-16. Convert it to one of those encodings; no legacy encoding is guessed.')); }
      setImportInfo({ name: file.name, bytes: file.size, encoding });
      return { text, readConsent };
    });
  }
  return <section className="scewin-panel" aria-busy={Boolean(busy)}><h4>{t('Read BIOS setup from an export')}</h4>
    <p className="muted-copy">{t('Start with an existing export: select the text file, review its encoding and reported values, and verify the motherboard and freshness yourself. No tool, driver or BIOS write is needed for import; values are not sent to AI.')}</p>
    <fieldset disabled={!readConsent || Boolean(busy)}><label><span>{t('Or import an existing nvram.txt without running drivers')}</span><input type="file" accept=".txt,text/plain" onChange={event => { const file = event.currentTarget.files?.[0]; if (file) void importFile(file); event.currentTarget.value = ''; }}/></label></fieldset>
    <details><summary>{t('No export yet? Review the privileged SCEWIN workflow')}</summary><h4>{t('Optional SCEWIN export · privileged external tool')}</h4>
    <p className="muted-copy">{t('For compatible AMI/HII firmware, SCEWIN can export actual setup values that Windows does not expose. Supply your own legally obtained local package; NeuroTune does not bundle or download AMI binaries. Only {arguments} export is invoked, never import/write.', { arguments: '/o /s' })}</p>
    <p className="error-text">{t('The tool can load kernel drivers, including unsigned or unverified drivers. Export arguments do not sandbox its internals. Privileged external code can access local data and credentials. It can crash Windows or affect firmware; a driver may remain loaded until restart. Windows may block it. NeuroTune will not disable Secure Boot, Memory Integrity or driver-signature enforcement.')}</p>
    <fieldset disabled={!readConsent || Boolean(busy)}>
      <label><span>{t('Local SCEWIN folder')}</span><input value={directory} spellCheck={false} placeholder="C:\Tools\SCEWIN" onChange={event => { setDirectory(event.target.value); setFiles(undefined); setRiskAccepted(false); }}/></label>
      <button className="secondary" disabled={!directory.trim()} onClick={() => void run('scewin-inspect', { directory })}>{t('Inspect package hashes')}</button>
      {files && <><dl className="firmware-facts">{files.map(file => <div key={file.name}><dt>{file.name}<small>{t('{bytes} bytes · signature not verified', { bytes: file.bytes.toLocaleString() })}</small></dt><dd><code>{file.sha256}</code></dd></div>)}</dl>
        <label className="consent-toggle"><input type="checkbox" checked={riskAccepted} onChange={event => setRiskAccepted(event.target.checked)}/><span><strong>{t('I approve this privileged package, including unsigned/unverified driver risk')}</strong><small>{t('The displayed SHA-256 values identify the files I am authorizing; they do not establish vendor authenticity.')}</small></span></label>
        <button className="secondary" disabled={!riskAccepted} onClick={() => {
          if (window.confirm(t('Run these exact SCEWIN files as administrator to request an export? Kernel drivers may load and external code can access local data/credentials. No BIOS import, force flag or security bypass will be invoked.')))
            void run('scewin-export', { directory, readConsent, riskAccepted, expectedHashes: Object.fromEntries(files.map(file => [file.name, file.sha256])) });
        }}>{t('Export current setup with SCEWIN')}</button>
      </>}
    </fieldset></details>
    {busy && <p role="status">{'source' in busy ? t(busy.source) : busy.raw}</p>}{error && <p role="alert" className="error-text">{'source' in error ? t(error.source) : error.raw}</p>}
    {!readConsent && <p className="muted-copy">{t('Enable firmware reading above first.')}</p>}
    {report && <>{importInfo && <p className="muted-copy">{t('Imported file: {name} · {bytes} bytes · {encoding}', { name: importInfo.name, bytes: importInfo.bytes, encoding: importInfo.encoding })}</p>}<ScewinSettings key={report.exportSha256} report={report}/></>}
  </section>;
}
