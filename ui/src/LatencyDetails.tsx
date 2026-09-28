import { useEffect, useState } from 'react';
import { agent } from './agent';
import type { TraceReport } from './types';

interface ProcessorTimeSample {
  processorGroup: number; logicalProcessor: number; windowMilliseconds: number;
  busyMilliseconds: number; idleMilliseconds: number; dpcMilliseconds: number; isrMilliseconds: number;
}

export function LiveProcessorTimes({ recording }: { recording: boolean }) {
  const [samples, setSamples] = useState<ProcessorTimeSample[]>([]);
  const [error, setError] = useState('');
  useEffect(() => {
    if (!recording) return;
    let disposed = false;
    let timer: number | undefined;
    async function sample() {
      try {
        const result = await agent<ProcessorTimeSample[]>('measurement-live');
        if (!disposed) { setSamples(result); setError(''); }
      } catch (reason) { if (!disposed) setError(String(reason)); }
      if (!disposed) timer = window.setTimeout(() => void sample(), 3000);
    }
    void sample();
    return () => { disposed = true; window.clearTimeout(timer); };
  }, [recording]);
  if (!recording && !samples.length) return null;
  return <section className="section-card"><h3>{recording ? 'Live processor times' : 'Last processor sample'}</h3>
    <p className="muted-copy">Windows counters sampled over approximately one second. Times are per logical processor; DPC/ISR counters do not measure individual spikes or interrupt-to-process latency.</p>
    {error && <p role="alert">{error}</p>}
    <div className="measurement-table">{samples.map(item => <article key={`${item.processorGroup}:${item.logicalProcessor}`}>
      <strong>Group {item.processorGroup} · LP {item.logicalProcessor}</strong>
      <span>Busy {item.busyMilliseconds.toFixed(1)} ms · idle {item.idleMilliseconds.toFixed(1)} ms</span>
      <code>DPC {item.dpcMilliseconds.toFixed(2)} ms · ISR {item.isrMilliseconds.toFixed(2)} ms · window {item.windowMilliseconds.toFixed(0)} ms</code>
    </article>)}</div>
  </section>;
}

export function LatencyDetails({ report, hardFaultsEnabled }: { report: TraceReport; hardFaultsEnabled?: boolean }) {
  const [page, setPage] = useState(0);
  const [driver, setDriver] = useState<DriverResult>();
  const [driverBusy, setDriverBusy] = useState(false);
  const [driverError, setDriverError] = useState('');
  async function inspectDriver(module: string) {
    setDriverBusy(true); setDriver(undefined); setDriverError('');
    try { setDriver(await agent<DriverResult>('measurement-driver-details', { module })); }
    catch (reason) { setDriverError(String(reason)); }
    finally { setDriverBusy(false); }
  }
  const pageCount = Math.max(1, Math.ceil(report.interrupts.length / 20));
  const visiblePage = Math.min(page, pageCount - 1);
  return <>
    <h4>Every logical processor</h4><div className="measurement-table">{report.processors.map(item => <article key={item.logicalProcessor}>
      <strong>LP {item.logicalProcessor}</strong>
      <span>Scheduled busy {item.scheduledBusyMilliseconds?.toFixed(1) ?? 'Unavailable'} ms · idle {item.scheduledIdleMilliseconds?.toFixed(1) ?? 'Unavailable'} ms</span>
      <code>DPC {item.dpc ? (item.dpc.totalMicroseconds / 1000).toFixed(2) : 'Unavailable'} ms · ISR {item.isr ? (item.isr.totalMicroseconds / 1000).toFixed(2) : 'Unavailable'} ms</code>
      {item.unobservedMilliseconds != null && <small>Unobserved {item.unobservedMilliseconds.toFixed(2)} ms · DPC {item.dpc?.count ?? 0} events, max {item.dpc?.maxMicroseconds.toFixed(2) ?? 'Unavailable'} µs · ISR {item.isr?.count ?? 0} events, max {item.isr?.maxMicroseconds.toFixed(2) ?? 'Unavailable'} µs</small>}
    </article>)}</div>
    <h4>Driver / processor distributions · {report.interrupts.length} rows</h4>
    <div className="measurement-table">{report.interrupts.slice(visiblePage * 20, (visiblePage + 1) * 20).map((item, index) => <article key={index}>
      <strong>{item.kind.toUpperCase()} · {item.module} · LP {item.logicalProcessor}</strong>
      <span>{item.distribution.count} events · {(item.distribution.totalMicroseconds / 1000).toFixed(2)} ms total</span>
      <code>Max {item.distribution.maxMicroseconds.toFixed(2)} µs · P99 {item.distribution.p99Microseconds.toFixed(2)} µs</code>
      {item.module.toLowerCase().endsWith('.sys') && <button className="secondary" disabled={driverBusy} onClick={() => void inspectDriver(item.module)}>Inspect {item.module} in Windows</button>}
    </article>)}</div>
    <div className="button-row"><button className="secondary" disabled={visiblePage === 0} onClick={() => setPage(visiblePage - 1)}>Previous drivers</button><span>Page {visiblePage + 1} / {pageCount}</span><button className="secondary" disabled={visiblePage + 1 >= pageCount} onClick={() => setPage(visiblePage + 1)}>Next drivers</button></div>
    {driverBusy && <p role="status">Reading current driver inventory…</p>}
    {driverError && <p role="alert">{driverError}</p>}
    {driver && <DriverDetails result={driver}/>}
    <h4>Hard pagefaults by process</h4>
    {!hardFaultsEnabled ? <p className="muted-copy">Unavailable in this legacy capture.</p> : !report.hardFaults?.length ? <p className="muted-copy">No hard pagefaults observed during this capture.</p> : <div className="measurement-table">{report.hardFaults.map(item => <article key={item.processKey}>
      <strong>{item.processName}</strong><span>{item.resolution.count} faults · {item.resolution.eventsPerSecond.toFixed(2)}/s</span>
      <code>Max {item.resolution.maxMicroseconds.toFixed(2)} µs · P99 {item.resolution.p99Microseconds.toFixed(2)} µs</code>
    </article>)}</div>}
    <details><summary>Timeline of the 50 longest interrupt events</summary><div className="measurement-table">{report.longestSpikes?.map((item, index) => <article key={index}>
      <strong>{item.kind.toUpperCase()} · {item.source}</strong><span>LP {item.logicalProcessor} · at {item.startMilliseconds.toFixed(2)} ms</span><code>{item.durationMicroseconds.toFixed(2)} µs</code>
    </article>)}</div></details>
    {report.limitations && <details><summary>Measurement coverage and limitations</summary><ul className="muted-copy">{report.limitations.map(item => <li key={item}>{item}</li>)}</ul></details>}
  </>;
}

interface DriverResult {
  module: string; inspectedAtUtc: string; limitation: string;
  services: { name: string; displayName: string; state: string; startMode: string; fileVersion: string; company: string; devices: string[] }[];
}

export function DriverDetails({ result }: { result: DriverResult }) {
  return <section className="section-card"><h4>{result.module} · current Windows inventory</h4>
    <p className="muted-copy">Read at {new Date(result.inspectedAtUtc).toLocaleString()}. {result.limitation}</p>
    {!result.services.length && <p>No readable direct driver-service association found.</p>}
    <div className="measurement-table">{result.services.map(item => <article key={item.name}>
      <strong>{item.displayName} · {item.name}</strong><span>{item.company} · {item.fileVersion}</span>
      <code>{item.state} · start mode {item.startMode}</code>
      <span>Directly associated devices: {item.devices.length ? item.devices.join(', ') : 'None found; shared/framework users may be unlisted'}</span>
    </article>)}</div>
  </section>;
}

interface FirmwareResult {
  readEnabled: boolean; writeSupported: boolean; facts: Record<string, string>;
  interfaces: string[]; settingsStatus: string; guidanceUrl: string;
}

export function FirmwarePanel() {
  const [consent, setConsent] = useState(() => localStorage.getItem('neurotune.firmwareReadConsent') === 'true');
  const [result, setResult] = useState<FirmwareResult>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  async function read() {
    setBusy(true); setError('');
    try { setResult(await agent<FirmwareResult>('firmware-read', { readConsent: consent })); }
    catch (reason) { setError(String(reason)); }
    finally { setBusy(false); }
  }
  return <section className="section-card"><h3>BIOS / UEFI inspection</h3>
    <label className="consent-toggle"><input type="checkbox" checked={consent} disabled={busy} onChange={event => {
      setConsent(event.target.checked); localStorage.setItem('neurotune.firmwareReadConsent', String(event.target.checked)); setResult(undefined);
    }}/><span><strong>Allow BIOS reading</strong><small>Enables firmware identity and memory details in subsequent scans and this local inspection. Existing reports remain stored.</small></span></label>
    <p className="muted-copy">BIOS writing is unavailable. Detected interfaces and Windows-observed state do not prove access to setup values such as PBO, C-states or memory timings.</p>
    <button className="secondary" disabled={!consent || busy} onClick={() => void read()}>{busy ? 'Reading firmware…' : 'Read firmware now'}</button>
    {error && <p role="alert">{error}</p>}
    {result && <><p role="status">{result.settingsStatus}</p><div className="measurement-table">{Object.entries(result.facts).map(([key, value]) => <article key={key}><strong>{key}</strong><span>{value}</span></article>)}</div>
      {result.guidanceUrl && <a href={result.guidanceUrl} target="_blank" rel="noreferrer">Official BIOS guide · use the manual for the exact detected board and firmware</a>}
    </>}
  </section>;
}
