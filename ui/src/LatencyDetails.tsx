import { useEffect, useState } from 'react';
import { LoaderCircle } from 'lucide-react';
import { agent } from './agent';
import { ScewinPanel } from './ScewinPanel';
import type { TraceReport } from './types';
import { formatDate, t } from './i18n';

function driverStatus(value: string) {
  return ['Running', 'Stopped', 'Paused', 'Start Pending', 'Stop Pending', 'Continue Pending', 'Pause Pending', 'Unknown', 'Boot', 'System', 'Auto', 'Manual', 'Disabled'].includes(value) ? t(value) : value;
}

export function MeasurementFeedback({ message, startedAt, log, children }: {
  message: string; startedAt: number; log: string[]; children?: React.ReactNode;
}) {
  const [now, setNow] = useState(Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);
  return <section className="measurement-feedback" aria-label={t('Measurement operation')}>
    <div className="section-heading"><strong role="status"><LoaderCircle size={18} className="spin" aria-hidden="true"/>{message}</strong>
      <span className="status-pill" role="timer" aria-live="off">{t('{seconds} s elapsed', { seconds: Math.max(0, Math.floor((now - startedAt) / 1000)) })}</span></div>
    <p>{t('Running locally, without AI. Windows recorder startup, trace saving and analysis can take several seconds. The capture countdown begins only after WPR is ready.')}</p>
    {log.length > 0 && <details open><summary>{t('Operation log')}</summary><ol>{log.map((entry, index) => <li key={index}>{entry}</li>)}</ol></details>}
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
  return <span role="timer" aria-live="off">{remaining == null ? t('Waiting for recorder') : remaining > 0 ? t('{seconds}s remaining', { seconds: remaining }) : t('Finalizing capture…')}</span>;
}

export function MeasurementBars({ title, unit, rows, note }: {
  title: string; unit: string; rows: { label: string; value: number }[]; note: string;
}) {
  const available = rows.filter(row => Number.isFinite(row.value) && row.value >= 0);
  const maximum = Math.max(0, ...available.map(row => row.value));
  return <figure className="measurement-chart"><figcaption><h4>{title}</h4><p className="muted-copy">{note}</p></figcaption>
    {!available.length ? <p className="muted-copy">{t('No recorded data available for this chart.')}</p> : <div className="chart-bars" tabIndex={0} role="region" aria-label={t('{title} values', { title })}>{available.map((row, index) => <div className="chart-row" key={index}>
      <div><span>{row.label}</span><strong>{row.value.toFixed(unit === 'events' ? 0 : 2)} {unit === 'events' ? t('events') : unit}</strong></div>
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
    <MeasurementBars title={t('Interrupt time by logical processor')} unit="ms" rows={report.processors.filter(item => item.dpc && item.isr).map(item => ({
      label: `LP ${item.logicalProcessor}`, value: (item.dpc!.totalMicroseconds + item.isr!.totalMicroseconds) / 1000,
    }))} note={t('Total observed DPC + ISR duration. This is already included in scheduled busy time, not additional CPU load. Each bar shares the same scale.')}/>
    <MeasurementBars title={t('Longest driver interrupt events')} unit="µs" rows={[...drivers].map(([label, value]) => ({ label, value })).sort((a, b) => b.value - a.value).slice(0, 10)}
      note={t('Top 10 driver / interrupt-kind maxima across all logical processors. These are individual event durations, not interrupt-to-process latency or proof of a faulty driver.')}/>
    {hardFaultsEnabled && <MeasurementBars title={t('Hard pagefaults by process')} unit="events" rows={(report.hardFaults ?? []).map(item => ({ label: `${item.processName} · ${item.processKey}`, value: item.resolution.count })).sort((a, b) => b.value - a.value).slice(0, 10)}
      note={t('Top 10 captured process lifetimes by fault count. A hard pagefault requires disk access; it is not necessarily an application error. Complete details are below.')}/>}
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
  return <section className="section-card"><details open={recording}><summary><strong>{t(recording ? 'Live processor times' : 'Last processor sample (not the trace report)')}</strong></summary>
    <p className="muted-copy">{t('Windows counters sampled over approximately one second. Times are per logical processor; DPC/ISR counters do not measure individual spikes or interrupt-to-process latency.')}</p>
    {error && <p role="alert">{error}</p>}
    {recording && !samples.length && !error && <p role="status">{t('Sampling the first processor counter window…')}</p>}
    <MeasurementBars title={t('Live CPU busy time')} unit="%" rows={samples.filter(item => item.windowMilliseconds > 0).map(item => ({ label: t('Group {group} · LP {processor}', { group: item.processorGroup, processor: item.logicalProcessor }), value: item.busyMilliseconds / item.windowMilliseconds * 100 }))} note={t('Busy time as a percentage of each actual sampling window. Bars are relative to the busiest processor in this sample; exact percentages are labelled.')}/>
    <details><summary>{t('Exact live processor counters')}</summary><div className="measurement-table">{samples.map(item => <article key={`${item.processorGroup}:${item.logicalProcessor}`}>
      <strong>{t('Group {group} · LP {processor}', { group: item.processorGroup, processor: item.logicalProcessor })}</strong>
      <span>{t('Busy {busy} ms · idle {idle} ms', { busy: item.busyMilliseconds.toFixed(1), idle: item.idleMilliseconds.toFixed(1) })}</span>
      <code>{t('DPC {dpc} ms · ISR {isr} ms · window {window} ms', { dpc: item.dpcMilliseconds.toFixed(2), isr: item.isrMilliseconds.toFixed(2), window: item.windowMilliseconds.toFixed(0) })}</code>
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
    <details><summary>{t('Exact counters for every logical processor')}</summary>
    <h4>{t('Every logical processor')}</h4><div className="measurement-table">{report.processors.map(item => <article key={item.logicalProcessor}>
      <strong>LP {item.logicalProcessor}</strong>
      <span>{t('Scheduled busy {busy} ms · idle {idle} ms', { busy: item.scheduledBusyMilliseconds?.toFixed(1) ?? t('Unavailable'), idle: item.scheduledIdleMilliseconds?.toFixed(1) ?? t('Unavailable') })}</span>
      <code>{t('DPC {dpc} ms · ISR {isr} ms', { dpc: item.dpc ? (item.dpc.totalMicroseconds / 1000).toFixed(2) : t('Unavailable'), isr: item.isr ? (item.isr.totalMicroseconds / 1000).toFixed(2) : t('Unavailable') })}</code>
      {item.unobservedMilliseconds != null && <small>{t('Unobserved {unobserved} ms · DPC {dpcCount} events, max {dpcMax} µs · ISR {isrCount} events, max {isrMax} µs', { unobserved: item.unobservedMilliseconds.toFixed(2), dpcCount: item.dpc?.count ?? 0, dpcMax: item.dpc?.maxMicroseconds.toFixed(2) ?? t('Unavailable'), isrCount: item.isr?.count ?? 0, isrMax: item.isr?.maxMicroseconds.toFixed(2) ?? t('Unavailable') })}</small>}
    </article>)}</div></details>
    <h4>{t('Driver / processor distributions · {count} rows', { count: report.interrupts.length })}</h4>
    <div className="measurement-table">{report.interrupts.slice(visiblePage * 20, (visiblePage + 1) * 20).map((item, index) => <article key={index}>
      <strong>{item.kind.toUpperCase()} · {item.module} · LP {item.logicalProcessor}</strong>
      <span>{t('{count} events · {total} ms total', { count: item.distribution.count, total: (item.distribution.totalMicroseconds / 1000).toFixed(2) })}</span>
      <code>{t('Max {max} µs · P99 {p99} µs', { max: item.distribution.maxMicroseconds.toFixed(2), p99: item.distribution.p99Microseconds.toFixed(2) })}</code>
      {item.module.toLowerCase().endsWith('.sys') && <button className="secondary" disabled={driverBusy} onClick={() => void inspectDriver(item.module)}>{t('Inspect {module} in Windows', { module: item.module })}</button>}
    </article>)}</div>
    <div className="button-row"><button className="secondary" disabled={visiblePage === 0} onClick={() => setPage(visiblePage - 1)}>{t('Previous drivers')}</button><span>{t('Page {page} / {pages}', { page: visiblePage + 1, pages: pageCount })}</span><button className="secondary" disabled={visiblePage + 1 >= pageCount} onClick={() => setPage(visiblePage + 1)}>{t('Next drivers')}</button></div>
    {driverBusy && <p role="status">{t('Reading current driver inventory…')}</p>}
    {driverError && <p role="alert">{driverError}</p>}
    {driver && <DriverDetails result={driver}/>}
    <h4>{t('Hard pagefaults by process')}</h4>
    {!hardFaultsEnabled ? <p className="muted-copy">{t('Unavailable in this legacy capture.')}</p> : !report.hardFaults?.length ? <p className="muted-copy">{t('No hard pagefaults observed during this capture.')}</p> : <div className="measurement-table">{report.hardFaults.map(item => <article key={item.processKey}>
      <strong>{item.processName}</strong><span>{t('{count} faults · {rate}/s', { count: item.resolution.count, rate: item.resolution.eventsPerSecond.toFixed(2) })}</span>
      <code>{t('Max {max} µs · P99 {p99} µs', { max: item.resolution.maxMicroseconds.toFixed(2), p99: item.resolution.p99Microseconds.toFixed(2) })}</code>
    </article>)}</div>}
    <details><summary>{t('Timeline of the 50 longest interrupt events')}</summary><div className="measurement-table">{report.longestSpikes?.map((item, index) => <article key={index}>
      <strong>{item.kind.toUpperCase()} · {item.source}</strong><span>{t('LP {processor} · at {time} ms', { processor: item.logicalProcessor, time: item.startMilliseconds.toFixed(2) })}</span><code>{item.durationMicroseconds.toFixed(2)} µs</code>
    </article>)}</div></details>
    {report.limitations && <details><summary>{t('Measurement coverage and limitations')}</summary><ul className="muted-copy">{report.limitations.map(item => <li key={item}>{item}</li>)}</ul></details>}
  </>;
}

interface DriverResult {
  module: string; inspectedAtUtc: string; limitation: string;
  services: { name: string; displayName: string; state: string; startMode: string; fileVersion: string; company: string; devices: string[] }[];
}

export function DriverDetails({ result }: { result: DriverResult }) {
  return <section className="section-card"><h4>{t('{module} · current Windows inventory', { module: result.module })}</h4>
    <p className="muted-copy">{t('Read at {date}.', { date: formatDate(result.inspectedAtUtc) })} {result.limitation}</p>
    {!result.services.length && <p>{t('No readable direct driver-service association found.')}</p>}
    <div className="measurement-table">{result.services.map(item => <article key={item.name}>
      <strong>{item.displayName} · {item.name}</strong><span>{item.company} · {item.fileVersion}</span>
      <code>{t('{state} · start mode {mode}', { state: driverStatus(item.state), mode: driverStatus(item.startMode) })}</code>
      <span>{t('Directly associated devices: {devices}', { devices: item.devices.length ? item.devices.join(', ') : t('None found; shared/framework users may be unlisted') })}</span>
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
    <div className="notice info"><div><strong>{t('Exact BIOS setup values are not available to the Windows reader')}</strong><p>{result.settingsStatus}</p>
      <p>{t('PBO, C-states, XMP/EXPO enablement and memory timings cannot be confirmed by this reader. Detected interfaces are not proof of readable settings.')}</p></div></div>
    <p className="muted-copy">{t('Detected setup interfaces: {interfaces}. No firmware write or driver installation is performed.', { interfaces: result.interfaces.length ? result.interfaces.join(', ') : t('None') })}</p>
    {groups.map(group => <section key={group.title}><h4>{t(group.title)}</h4><dl className="firmware-facts">
      {Object.entries(result.facts).filter(([key]) => group.matches(key)).map(([key, value]) => <div key={key}><dt>{key}</dt><dd>{value}</dd></div>)}
    </dl></section>)}
    {result.guidanceUrl && <><a href={result.guidanceUrl} target="_blank" rel="noreferrer">{t('Vendor documentation (reference only)')}</a><p className="muted-copy">{t('Documentation may explain some menus, but does not supply your current setup values or guarantee coverage of every option in this BIOS version.')}</p></>}
  </div>;
}

export function FirmwarePanel() {
  const [section, setSection] = useState('reader');
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
  return <section className="section-card firmware-panel"><h2>{t('BIOS / UEFI inspection')}</h2>
    <div className="section-switcher" role="group" aria-label={t('Firmware inspection sections')}><button disabled={busy || scewinBusy} aria-pressed={section === 'reader'} onClick={() => setSection('reader')}>{t('Windows firmware reader')}</button><button disabled={busy || scewinBusy} aria-pressed={section === 'export'} onClick={() => setSection('export')}>{t('SCEWIN export')}</button></div>
    <div hidden={section !== 'reader'}>
    <label className="consent-toggle"><input type="checkbox" checked={consent} disabled={busy || scewinBusy} onChange={event => {
      setConsent(event.target.checked); localStorage.setItem('neurotune.firmwareReadConsent', String(event.target.checked)); setResult(undefined);
    }}/><span><strong>{t('Allow BIOS reading')}</strong><small>{t('Enables firmware identity and memory details in subsequent scans and this local inspection. Existing reports remain stored.')}</small></span></label>
    <p className="muted-copy">{t('Read board/BIOS identity, Secure Boot, DIMM information and Windows-observed virtualization/TPM state. Exact setup settings are not exposed by this Windows reader. Optional SCEWIN export has a separate section; BIOS writing is not supported.')}</p>
    <button className="secondary" disabled={!consent || busy || scewinBusy} onClick={() => void read()}>{t(busy ? 'Reading firmware…' : 'Read firmware now')}</button>
    {busy && <p role="status">{t('Reading SMBIOS and Windows WMI providers locally. Some providers take several seconds; no AI request is being made.')}</p>}
    {error && <p role="alert">{error}</p>}
    {result && <><p className="sr-only" role="status">{t('Windows firmware inspection completed. Exact setup settings remain unavailable to this reader.')}</p><FirmwareFacts result={result}/></>}
    </div>
    <div hidden={section !== 'export'}><ScewinPanel readConsent={consent} onBusyChange={setScewinBusy}/></div>
  </section>;
}
