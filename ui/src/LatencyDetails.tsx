import { useEffect, useState } from 'react';
import { LoaderCircle } from 'lucide-react';
import { agent } from './agent';
import { ScewinPanel } from './ScewinPanel';
import type { TraceReport } from './types';

export function MeasurementFeedback({ message, startedAt, log, children }: {
  message: string; startedAt: number; log: string[]; children?: React.ReactNode;
}) {
  const [now, setNow] = useState(Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);
  return <section className="measurement-feedback" aria-label="Measurement operation">
    <div className="section-heading"><strong role="status"><LoaderCircle size={18} className="spin" aria-hidden="true"/>{message}</strong>
      <span className="status-pill" role="timer" aria-live="off">{Math.max(0, Math.floor((now - startedAt) / 1000))} s elapsed</span></div>
    <p>Running locally, without AI. Windows recorder startup, trace saving and analysis can take several seconds. The capture countdown begins only after WPR is ready.</p>
    {log.length > 0 && <details open><summary>Operation log</summary><ol>{log.map((entry, index) => <li key={index}>{entry}</li>)}</ol></details>}
    {children}
  </section>;
}

export function CaptureCountdown({ startedAt, durationSeconds }: { startedAt?: string; durationSeconds: number }) {
  const [now, setNow] = useState(Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);
  const remaining = startedAt ? Math.max(0, durationSeconds - Math.floor((now - new Date(startedAt).getTime()) / 1000)) : null;
  return <span role="timer" aria-live="off">{remaining == null ? 'Waiting for recorder' : remaining > 0 ? `${remaining}s remaining` : 'Finalizing capture…'}</span>;
}

export function MeasurementBars({ title, unit, rows, note }: {
  title: string; unit: string; rows: { label: string; value: number }[]; note: string;
}) {
  const available = rows.filter(row => Number.isFinite(row.value) && row.value >= 0);
  const maximum = Math.max(0, ...available.map(row => row.value));
  return <figure className="measurement-chart"><figcaption><h4>{title}</h4><p className="muted-copy">{note}</p></figcaption>
    {!available.length ? <p className="muted-copy">No recorded data available for this chart.</p> : <div className="chart-bars" tabIndex={0} role="region" aria-label={`${title} values`}>{available.map((row, index) => <div className="chart-row" key={index}>
      <div><span>{row.label}</span><strong>{row.value.toFixed(unit === 'events' ? 0 : 2)} {unit}</strong></div>
      <div className="chart-track" aria-hidden="true"><span style={{ transform: `scaleX(${maximum > 0 ? row.value / maximum : 0})` }}/></div>
    </div>)}</div>}
  </figure>;
}

export function LatencyCharts({ report, hardFaultsEnabled }: { report: TraceReport; hardFaultsEnabled?: boolean }) {
  const drivers = new Map<string, number>();
  for (const item of report.interrupts) {
    const label = `${item.module} · ${item.kind.toUpperCase()}`;
    drivers.set(label, Math.max(drivers.get(label) ?? 0, item.distribution.maxMicroseconds));
  }
  return <div className="latency-charts">
    <MeasurementBars title="Interrupt time by logical processor" unit="ms" rows={report.processors.filter(item => item.dpc && item.isr).map(item => ({
      label: `LP ${item.logicalProcessor}`, value: (item.dpc!.totalMicroseconds + item.isr!.totalMicroseconds) / 1000,
    }))} note="Total observed DPC + ISR duration. This is already included in scheduled busy time, not additional CPU load. Each bar shares the same scale."/>
    <MeasurementBars title="Longest driver interrupt events" unit="µs" rows={[...drivers].map(([label, value]) => ({ label, value })).sort((a, b) => b.value - a.value).slice(0, 10)}
      note="Top 10 driver / interrupt-kind maxima across all logical processors. These are individual event durations, not interrupt-to-process latency or proof of a faulty driver."/>
    {hardFaultsEnabled && <MeasurementBars title="Hard pagefaults by process" unit="events" rows={(report.hardFaults ?? []).map(item => ({ label: `${item.processName} · ${item.processKey}`, value: item.resolution.count })).sort((a, b) => b.value - a.value).slice(0, 10)}
      note="Top 10 captured process lifetimes by fault count. A hard pagefault requires disk access; it is not necessarily an application error. Complete details are below."/>}
  </div>;
}

interface ProcessorTimeSample {
  processorGroup: number; logicalProcessor: number; windowMilliseconds: number;
  busyMilliseconds: number; idleMilliseconds: number; dpcMilliseconds: number; isrMilliseconds: number;
}

export function LiveProcessorTimes({ recording }: { recording: boolean }) {
  const [samples, setSamples] = useState<ProcessorTimeSample[]>([]);
  const [error, setError] = useState('');
  useEffect(() => {
    if (!recording) return;
    setSamples([]); setError('');
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
  return <section className="section-card"><details open={recording}><summary><strong>{recording ? 'Live processor times' : 'Last processor sample (not the trace report)'}</strong></summary>
    <p className="muted-copy">Windows counters sampled over approximately one second. Times are per logical processor; DPC/ISR counters do not measure individual spikes or interrupt-to-process latency.</p>
    {error && <p role="alert">{error}</p>}
    {recording && !samples.length && !error && <p role="status">Sampling the first processor counter window…</p>}
    <MeasurementBars title="Live CPU busy time" unit="%" rows={samples.filter(item => item.windowMilliseconds > 0).map(item => ({ label: `Group ${item.processorGroup} · LP ${item.logicalProcessor}`, value: item.busyMilliseconds / item.windowMilliseconds * 100 }))} note="Busy time as a percentage of each actual sampling window. Bars are relative to the busiest processor in this sample; exact percentages are labelled."/>
    <details><summary>Exact live processor counters</summary><div className="measurement-table">{samples.map(item => <article key={`${item.processorGroup}:${item.logicalProcessor}`}>
      <strong>Group {item.processorGroup} · LP {item.logicalProcessor}</strong>
      <span>Busy {item.busyMilliseconds.toFixed(1)} ms · idle {item.idleMilliseconds.toFixed(1)} ms</span>
      <code>DPC {item.dpcMilliseconds.toFixed(2)} ms · ISR {item.isrMilliseconds.toFixed(2)} ms · window {item.windowMilliseconds.toFixed(0)} ms</code>
    </article>)}</div></details></details>
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
    <LatencyCharts report={report} hardFaultsEnabled={hardFaultsEnabled}/>
    <details><summary>Exact counters for every logical processor</summary>
    <h4>Every logical processor</h4><div className="measurement-table">{report.processors.map(item => <article key={item.logicalProcessor}>
      <strong>LP {item.logicalProcessor}</strong>
      <span>Scheduled busy {item.scheduledBusyMilliseconds?.toFixed(1) ?? 'Unavailable'} ms · idle {item.scheduledIdleMilliseconds?.toFixed(1) ?? 'Unavailable'} ms</span>
      <code>DPC {item.dpc ? (item.dpc.totalMicroseconds / 1000).toFixed(2) : 'Unavailable'} ms · ISR {item.isr ? (item.isr.totalMicroseconds / 1000).toFixed(2) : 'Unavailable'} ms</code>
      {item.unobservedMilliseconds != null && <small>Unobserved {item.unobservedMilliseconds.toFixed(2)} ms · DPC {item.dpc?.count ?? 0} events, max {item.dpc?.maxMicroseconds.toFixed(2) ?? 'Unavailable'} µs · ISR {item.isr?.count ?? 0} events, max {item.isr?.maxMicroseconds.toFixed(2) ?? 'Unavailable'} µs</small>}
    </article>)}</div></details>
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

export interface FirmwareResult {
  readEnabled: boolean; writeSupported: boolean; facts: Record<string, string>;
  interfaces: string[]; settingsStatus: string; guidanceUrl: string;
}

export function FirmwareFacts({ result }: { result: FirmwareResult }) {
  const groups = [
    { title: 'Board and firmware identity', matches: (key: string) => ['Motherboard', 'BIOS', 'Firmware mode', 'Secure Boot'].includes(key) },
    { title: 'Memory reported by SMBIOS', matches: (key: string) => key.startsWith('DIMM ') || key === 'Memory profile assessment' },
    { title: 'Processor and Windows-observed capabilities', matches: (key: string) => !['Motherboard', 'BIOS', 'Firmware mode', 'Secure Boot', 'Memory profile assessment'].includes(key) && !key.startsWith('DIMM ') },
  ];
  return <div className="firmware-results">
    <div className="notice info"><div><strong>Exact BIOS setup values are not available to the Windows reader</strong><p>{result.settingsStatus}</p>
      <p>PBO, C-states, XMP/EXPO enablement and memory timings cannot be confirmed by this reader. Detected interfaces are not proof of readable settings.</p></div></div>
    <p className="muted-copy">Detected setup interfaces: {result.interfaces.length ? result.interfaces.join(', ') : 'None'}. No firmware write or driver installation is performed.</p>
    {groups.map(group => <section key={group.title}><h4>{group.title}</h4><dl className="firmware-facts">
      {Object.entries(result.facts).filter(([key]) => group.matches(key)).map(([key, value]) => <div key={key}><dt>{key}</dt><dd>{value}</dd></div>)}
    </dl></section>)}
    {result.guidanceUrl && <><a href={result.guidanceUrl} target="_blank" rel="noreferrer">Vendor documentation (reference only)</a><p className="muted-copy">Documentation may explain some menus, but does not supply your current setup values or guarantee coverage of every option in this BIOS version.</p></>}
  </div>;
}

export function FirmwarePanel() {
  const [consent, setConsent] = useState(() => localStorage.getItem('neurotune.firmwareReadConsent') === 'true');
  const [result, setResult] = useState<FirmwareResult>();
  const [busy, setBusy] = useState(false);
  const [scewinBusy, setScewinBusy] = useState(false);
  const [error, setError] = useState('');
  async function read() {
    setBusy(true); setError('');
    try { setResult(await agent<FirmwareResult>('firmware-read', { readConsent: consent })); }
    catch (reason) { setError(String(reason)); }
    finally { setBusy(false); }
  }
  return <section className="section-card firmware-panel"><h3>BIOS / UEFI inspection</h3>
    <label className="consent-toggle"><input type="checkbox" checked={consent} disabled={busy || scewinBusy} onChange={event => {
      setConsent(event.target.checked); localStorage.setItem('neurotune.firmwareReadConsent', String(event.target.checked)); setResult(undefined);
    }}/><span><strong>Allow BIOS reading</strong><small>Enables firmware identity and memory details in subsequent scans and this local inspection. Existing reports remain stored.</small></span></label>
    <p className="muted-copy">Read board/BIOS identity, Secure Boot, DIMM information and Windows-observed virtualization/TPM state. Exact setup settings are not exposed by this Windows reader. Optional SCEWIN export is separate below; BIOS writing is not supported.</p>
    <button className="secondary" disabled={!consent || busy || scewinBusy} onClick={() => void read()}>{busy ? 'Reading firmware…' : 'Read firmware now'}</button>
    {busy && <p role="status">Reading SMBIOS and Windows WMI providers locally. Some providers take several seconds; no AI request is being made.</p>}
    {error && <p role="alert">{error}</p>}
    {result && <><p className="sr-only" role="status">Windows firmware inspection completed. Exact setup settings remain unavailable to this reader.</p><FirmwareFacts result={result}/></>}
    <ScewinPanel readConsent={consent} onBusyChange={setScewinBusy}/>
  </section>;
}
