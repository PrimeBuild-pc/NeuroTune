import { useEffect, useId, useState } from 'react';
import { agent } from './agent';
import { formatDate, t } from './i18n';
import type { DefenderReport, DefenderScanOperation, DefenderScanResult } from './types';

export const defenderScanDisclosure = 'The scan may quarantine or remove threats and use network/cloud/file sample submission according to existing Defender settings. Closing NeuroTune does not guarantee the scan stops. Manage progress and remediation in Windows Security; no rollback of malware removal is guaranteed.';

export function WindowsSecurityPanel({ blocked, onBusy, children }: { blocked: boolean; onBusy: (value: boolean) => void; children?: React.ReactNode }) {
  const scanId = useId();
  const [section, setSection] = useState(children ? 'privacy' : 'status');
  const [report, setReport] = useState<DefenderReport>();
  const [loadingCurrent, setLoadingCurrent] = useState(true);
  const [operation, setOperation] = useState<DefenderScanOperation>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [scanType, setScanType] = useState<'quick' | 'full'>('quick');
  const [scanConsent, setScanConsent] = useState(false);
  const [remediationConsent, setRemediationConsent] = useState(false);
  const [networkConsent, setNetworkConsent] = useState(false);
  useEffect(() => { void agent<DefenderScanOperation | null>('defender-scan-current').then(item => setOperation(item ?? undefined)).catch(reason => setError(String(reason))).finally(() => setLoadingCurrent(false)); }, []);
  async function perform(work: () => Promise<void>) {
    if (busy || blocked || loadingCurrent) return;
    setBusy(true); onBusy(true); setError('');
    try { await work(); }
    catch (reason) { setError(String(reason)); }
    finally { setBusy(false); onBusy(false); }
  }
  const unresolved = operation?.state === 'Running' || operation?.state === 'InterruptedOrFailed';
  const available = report?.status === 'observed' && report.protection?.antivirusEnabled === true && report.protection.runningMode === 'Normal' && report.scanState === 'idle';
  async function start() {
    if (!scanConsent || !remediationConsent || !networkConsent || !available || unresolved) return;
    if (!window.confirm(t('Start the {type} scan? {disclosure}', { type: scanType === 'quick' ? t('quick') : t('full'), disclosure: t(defenderScanDisclosure) }))) return;
    await perform(async () => {
      try {
        const result = await agent<DefenderScanResult>('defender-scan', { scanType, scanConsent, remediationConsent, networkConsent });
        setOperation(result.operation); setReport(result.report);
      } finally {
        setScanConsent(false); setRemediationConsent(false); setNetworkConsent(false);
        const current = await agent<DefenderScanOperation | null>('defender-scan-current'); setOperation(current ?? undefined);
      }
    });
  }
  return <div className="security-panel">
    <div className="section-switcher" role="group" aria-label={t('Windows security tools')}>
      {children && <button aria-pressed={section === 'privacy'} onClick={() => setSection('privacy')}>{t('Privacy & AI')}</button>}
      <button aria-pressed={section === 'status'} onClick={() => setSection('status')}>{t('Defender status')}</button>
      <button aria-pressed={section === 'scan'} onClick={() => setSection('scan')}>{t('User-approved Defender scan')}</button>
      <button aria-pressed={section === 'manual'} onClick={() => setSection('manual')}>{t('Manual tools')}</button>
    </div>
    {children && <div hidden={section !== 'privacy'}>{children}</div>}
    <fieldset disabled={blocked || busy || loadingCurrent}>
      <section className="section-card" hidden={section !== 'status'}>
        <h2>{t('Defender · local tools separate from AI')}</h2>
        <p>{t('Reading status and detection summaries does not start a scan. Scanning is a separate operation with possible effects on files; it is not a preset tweak or a model read request.')}</p>
        <p>{t('No output is automatically sent to the provider. After scanning, dismiss any previous report and start a new audit to collect fresh evidence.')}</p>
        <button className="secondary" onClick={() => void perform(async () => { setReport(await agent<DefenderReport>('defender-status')); setOperation((await agent<DefenderScanOperation | null>('defender-scan-current')) ?? undefined); })}>{t('Read status and detections · no scan')}</button>
        <DefenderReportView report={report}/>
      </section>
      <section className="section-card" hidden={section !== 'scan'}>
        <h2>{t('User-approved Defender scan')}</h2>
        <p>{t(defenderScanDisclosure)}</p>
        <p className="muted-copy">{t('A quick scan may take many minutes; a full scan hours. The command has a one-hour/24-hour limit: a timeout does not prove the antivirus service has stopped. Do not start it during benchmarks or performance runs. Exclusions, protections and cloud policies are not changed.')}</p>
        <fieldset className="form-grid"><legend>{t('Scan type')}</legend><div className="choice-grid wide">{(['quick', 'full'] as const).map(type => <label className="choice-option" key={type}><input type="radio" name={scanId} checked={scanType === type} onChange={() => { setScanType(type); setScanConsent(false); setRemediationConsent(false); setNetworkConsent(false); }}/><span>{t(type === 'quick' ? 'Quick' : 'Full')}</span></label>)}</div></fieldset>
        <div className="scan-consents">
          <label className="consent-toggle"><input type="checkbox" checked={scanConsent} onChange={event => setScanConsent(event.target.checked)}/><span>{t('I authorize this scan and its resource usage.')}</span></label>
          <label className="consent-toggle"><input type="checkbox" checked={remediationConsent} onChange={event => setRemediationConsent(event.target.checked)}/><span>{t('I accept possible quarantine/removal according to Defender; this is not a guaranteed remediation-free scan.')}</span></label>
          <label className="consent-toggle"><input type="checkbox" checked={networkConsent} onChange={event => setNetworkConsent(event.target.checked)}/><span>{t('I accept network/cloud/file sample submission according to current antivirus settings, independently of the AI provider.')}</span></label>
        </div>
        {!available && <p>{t('Read the status first. Defender must be active in Normal mode with idle scan status. An alternative antivirus, passive mode, busy or Unknown require manual verification.')}</p>}
        <button className="secondary" disabled={!scanConsent || !remediationConsent || !networkConsent || !available || unresolved} onClick={() => void start()}>{t('Start Defender scan')}</button>
      </section>
      {unresolved && <div className="notice danger" role="alert"><div><p>{t('The previous request did not finish reliably. Check progress and Protection history in Windows Security. No new scan is started and the existing one is not cancelled.')}</p><button className="secondary" onClick={() => {
        if (window.confirm(t('Have you verified in Windows Security that the scan is no longer running and reviewed Protection history? The report does not prove a clean PC.')))
          void perform(async () => setOperation(await agent<DefenderScanOperation>('defender-scan-review', { reviewedInWindowsSecurity: true })));
      }}>{t('I verified the interrupted request in Windows Security')}</button></div></div>}
    </fieldset>
    <section className="section-card" hidden={section !== 'manual'}>
      <h2>{t('Optional follow-ups · Autoruns and Sigcheck')}</h2>
      <p>{t('Autoruns/Autorunsc inventories persistence; Sigcheck checks signatures and metadata. They are not antivirus tools and an unsigned file is not automatically malware. In this release they are manual tools: NeuroTune does not download, install or run them.')}</p>
      <p>{t('Verify source, signature, version and EULA. VirusTotal may send hashes or files: do not enable it implicitly. Certificate checks may use the network even without VirusTotal. Review and import only redacted text in Supporting files; it remains unverified evidence, not a certified local result.')}</p>
      <div className="source-list"><a href="https://learn.microsoft.com/en-us/sysinternals/downloads/autoruns" target="_blank" rel="noreferrer">{t('Autoruns · Microsoft source')}</a><a href="https://learn.microsoft.com/en-us/sysinternals/downloads/sigcheck" target="_blank" rel="noreferrer">{t('Sigcheck · Microsoft source')}</a></div>
    </section>
    {busy && <p role="status">{t('Defender operation in progress. No AI inference; do not close the app to try to cancel the antivirus service.')}</p>}
    {error && <p className="error-text" role="alert">{error}</p>}
    {operation && <div className="section-card security-operation"><strong>{t('Last request: {type} · {state}', { type: operation.scanType, state: operation.state })}</strong><p>{operation.detail}</p></div>}
    <p className="muted-copy"><strong>{t('Remediation:')}</strong> {t('Open Windows Security → Virus & threat protection → Protection history. Verify the detection and antivirus options. NeuroTune does not delete AI-suggested files/services or expose generic removals.')}</p>
  </div>;
}

export function DefenderReportView({ report }: { report?: DefenderReport }) {
  if (!report) return <p>{t('No summary read. No scan started.')}</p>;
  const display = (value: unknown) => value === undefined || value === null ? t('Unknown') : String(value);
  return <section aria-label={t('Summary observed by Defender')}>
    <p><strong>{report.status === 'observed' ? t('Metadata reported by Defender') : t('Defender unavailable')}</strong> · {formatDate(report.readAtUtc)}</p>
    <dl className="firmware-facts">
      <div><dt>{t('Mode / scan status')}</dt><dd>{report.protection?.runningMode ?? t('Unknown')} / {report.scanState}</dd></div>
      <div><dt>{t('Antivirus / real-time protection / tamper')}</dt><dd>{display(report.protection?.antivirusEnabled)} / {display(report.protection?.realTimeProtectionEnabled)} / {display(report.protection?.tamperProtected)}</dd></div>
      <div><dt>{t('Signature age (days)')}</dt><dd>{display(report.protection?.signatureAgeDays)}</dd></div>
      <div><dt>{t('Cloud / sample submission (configured values)')}</dt><dd>{display(report.preferences?.cloudReporting)} / {display(report.preferences?.sampleSubmission)}</dd></div>
    </dl>
    <h4>{t('Scanner-reported threat summaries · not AI verdicts')}</h4>
    {report.threats.length ? <ul>{report.threats.map((threat, index) => <li key={index}>{t('{name} · ID {id} · active: {active} · reported severity: {severity}', { name: threat.name, id: display(threat.threatId), active: display(threat.isActive), severity: display(threat.severity) })}</li>)}</ul> : <p>{t('No items reported in the summary: this does not certify a clean PC.')}</p>}
    {report.truncated && <p role="alert">{t('Summary limited to the first 40 items; other detections may be present. Review Protection history.')}</p>}
    <p>{report.limitation}</p>
  </section>;
}
