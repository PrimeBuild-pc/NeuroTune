import { useEffect, useRef, useState } from 'react';
import { listen } from '@tauri-apps/api/event';
import { agent, newRequestId } from './agent';

interface ToolFile { name: string; sha256: string; bytes: number; }
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
  return <section className="scewin-report"><h4>SCEWIN setup observations · {report.settings.length} questions</h4>
    <p className="muted-copy">{report.source}<br/>{new Date(report.readAtUtc).toLocaleString()} · {report.sensitiveQuestionsOmitted} sensitive questions omitted. No setting can be edited or applied here.</p>
    <label><span>Filter setup questions</span><input value={search} onChange={event => { setSearch(event.target.value); setPage(0); }} placeholder="Example: PBO, C-state, memory"/></label>
    <dl className="firmware-facts">{matching.slice(visiblePage * 50, (visiblePage + 1) * 50).map((item, index) => <div key={index}>
      <dt>{item.question}<small>Token: {item.token || 'Unavailable'}</small></dt>
      <dd>{item.currentValue ?? 'Unknown'}<small>{item.status}</small></dd>
    </div>)}</dl>
    {!matching.length && <p>No matching setup questions.</p>}
    <div className="button-row"><button className="secondary" disabled={visiblePage === 0} onClick={() => setPage(visiblePage - 1)}>Previous settings</button><span>Page {visiblePage + 1} / {pages} · {matching.length} matching</span><button className="secondary" disabled={visiblePage + 1 >= pages} onClick={() => setPage(visiblePage + 1)}>Next settings</button></div>
    <details><summary>Export provenance and limitations</summary><code className="export-hash">SHA-256: {report.exportSha256}</code><ul className="muted-copy">{report.notes.map(note => <li key={note}>{note}</li>)}</ul></details>
  </section>;
}

export function ScewinPanel({ readConsent, onBusyChange }: { readConsent: boolean; onBusyChange: (busy: boolean) => void }) {
  const [directory, setDirectory] = useState('');
  const [files, setFiles] = useState<ToolFile[]>();
  const [riskAccepted, setRiskAccepted] = useState(false);
  const [report, setReport] = useState<ScewinReport>();
  const [busy, setBusy] = useState('');
  const [error, setError] = useState('');
  const request = useRef<string | undefined>(undefined);
  useEffect(() => {
    const unlisten = listen<{ requestId: string; message: string }>('agent-progress', event => {
      if (event.payload.requestId === request.current) setBusy(event.payload.message);
    });
    return () => { void unlisten.then(stop => stop()); };
  }, []);
  async function run(command: string, payload: unknown) {
    if (request.current) return;
    const id = newRequestId(); request.current = id; onBusyChange(true); setBusy('Processing locally…'); setError('');
    try {
      if (command === 'scewin-inspect') { setFiles(undefined); setRiskAccepted(false); setFiles(await agent<ToolFile[]>(command, payload, id)); }
      else setReport(await agent<ScewinReport>(command, payload, id));
    } catch (reason) { setError(String(reason)); }
    finally { request.current = undefined; setBusy(''); onBusyChange(false); }
  }
  async function importFile(file: File) {
    if (!readConsent || file.size > 16 * 1024 * 1024) { setError('Enable firmware reading and select an export smaller than 16 MiB.'); return; }
    try {
      const bytes = new Uint8Array(await file.arrayBuffer());
      const encoding = bytes[0] === 0xff && bytes[1] === 0xfe ? 'utf-16le' : bytes[0] === 0xfe && bytes[1] === 0xff ? 'utf-16be' : 'utf-8';
      const text = new TextDecoder(encoding, { fatal: true }).decode(bytes);
      await run('scewin-import', { text, readConsent });
    } catch (reason) { setError(String(reason)); }
  }
  return <section className="scewin-panel"><h4>Optional SCEWIN export · privileged external tool</h4>
    <p className="muted-copy">For compatible AMI/HII firmware, SCEWIN can export actual setup values that Windows does not expose. Supply your own legally obtained local package; NeuroTune does not bundle or download AMI binaries. Only <code>/o /s</code> export is invoked, never import/write.</p>
    <p className="error-text">The tool can load kernel drivers, including unsigned or unverified drivers. Export arguments do not sandbox its internals. Privileged external code can access local data and credentials. It can crash Windows or affect firmware; a driver may remain loaded until restart. Windows may block it. NeuroTune will not disable Secure Boot, Memory Integrity or driver-signature enforcement.</p>
    <fieldset disabled={!readConsent || Boolean(busy)}>
      <label><span>Local SCEWIN folder</span><input value={directory} spellCheck={false} placeholder="C:\Tools\SCEWIN" onChange={event => { setDirectory(event.target.value); setFiles(undefined); setRiskAccepted(false); }}/></label>
      <button className="secondary" disabled={!directory.trim()} onClick={() => void run('scewin-inspect', { directory })}>Inspect package hashes</button>
      {files && <><dl className="firmware-facts">{files.map(file => <div key={file.name}><dt>{file.name}<small>{file.bytes.toLocaleString()} bytes · signature not verified</small></dt><dd><code>{file.sha256}</code></dd></div>)}</dl>
        <label className="consent-toggle"><input type="checkbox" checked={riskAccepted} onChange={event => setRiskAccepted(event.target.checked)}/><span><strong>I approve this privileged package, including unsigned/unverified driver risk</strong><small>The displayed SHA-256 values identify the files I am authorizing; they do not establish vendor authenticity.</small></span></label>
        <button className="secondary" disabled={!riskAccepted} onClick={() => {
          if (window.confirm('Run these exact SCEWIN files as administrator to request an export? Kernel drivers may load and external code can access local data/credentials. No BIOS import, force flag or security bypass will be invoked.'))
            void run('scewin-export', { directory, readConsent, riskAccepted, expectedHashes: Object.fromEntries(files.map(file => [file.name, file.sha256])) });
        }}>Export current setup with SCEWIN</button>
      </>}
      <label><span>Or import an existing nvram.txt without running drivers</span><input type="file" accept=".txt,text/plain" onChange={event => { const file = event.currentTarget.files?.[0]; if (file) void importFile(file); event.currentTarget.value = ''; }}/></label>
    </fieldset>
    {busy && <p role="status">{busy}</p>}{error && <p role="alert" className="error-text">{error}</p>}
    {!readConsent && <p className="muted-copy">Enable firmware reading above first.</p>}
    {report && <ScewinSettings key={report.exportSha256} report={report}/>}
  </section>;
}
