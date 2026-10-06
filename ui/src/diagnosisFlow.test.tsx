import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { CompleteDiagnosis } from './CompleteDiagnosis';
import { DiagnosisFlow } from './diagnosisFlow';
import type { MeasurementSession, MeasurementWorkload, ScanResult, TuningGoals } from './types';

const goals: TuningGoals = { priority: 'systemLatency', riskProfile: 'balanced', games: [], notes: '', gameContext: { game: '', version: '', launcher: '', graphicsApi: '', displayMode: '', vrr: '', vSync: '', symptoms: [], preserve: '' }, performanceInput: { userProvided: true, notes: '' } };
const workload: MeasurementWorkload = { processId: 42, name: 'game', startTimeUtc: '2026-01-01T00:00:00Z', description: 'game' };
function harness(options: { cancelDuringStart?: boolean; invalidQuality?: boolean; failedProvider?: boolean } = {}) {
  const calls: string[] = []; const requests: Array<{ command: string; payload: unknown }> = []; const sessions: MeasurementSession[] = []; let id = 0;
  const flow = new DiagnosisFlow(() => {}, {
    cancel: async () => true, wait: async () => {},
    invoke: async <T,>(command: string, payload?: unknown): Promise<T> => {
      calls.push(command); requests.push({ command, payload }); let result: unknown;
      switch (command) {
        case 'support-preview': result = (payload as { attachments: unknown[] }).attachments; break;
        case 'scan': result = { profile: {}, actions: [] }; break;
        case 'measurement-start': {
          const request = payload as { systemWide: boolean; durationSeconds: number };
          const session = { id: String(++id), state: 'recording', systemWide: request.systemWide, label: 'baseline', recordingStartedAtUtc: new Date(Date.now() - request.durationSeconds * 1000).toISOString(), durationSeconds: request.durationSeconds, processName: 'game', hardwareFingerprint: 'hw', configurationFingerprint: 'cfg', createdAtUtc: String(id) } as MeasurementSession;
          sessions.push(session); result = session;
          if (options.cancelDuringStart) await flow.cancel();
          break;
        }
        case 'measurement-list': result = sessions.map(session => options.cancelDuringStart ? session : ({ ...session, state: session.state === 'recording' ? 'captured' : session.state })); break;
        case 'measurement-analyze': {
          const session = sessions.find(item => item.id === (payload as { sessionId: string }).sessionId)!;
          session.state = 'completed'; session.report = { schemaVersion: 1, quality: { isValid: !options.invalidQuality } } as MeasurementSession['report'];
          result = session; break;
        }
        case 'run-create': result = { id: 'run', state: 'scanned' }; break;
        case 'diagnose': result = { summary: options.failedProvider ? 'Provider failed; not an AI diagnosis' : 'AI result', recommendations: [] }; break;
        case 'run-get': result = { id: 'run', state: options.failedProvider ? 'hypothesizing' : !sessions.length ? 'proposalReady' : sessions[0].systemWide ? 'baselinePending' : 'baselineReady' }; break;
        case 'measurement-cancel': sessions[0].state = 'cancelled'; break;
        case 'run-dismiss': break;
        default: throw new Error(`Unexpected command ${command}`);
      }
      return result as T;
    },
  });
  return { flow, calls, requests };
}
const input = { goals, workload, durationSeconds: 30, optionalTelemetryConsent: false, firmwareReadConsent: false };

describe('complete diagnosis lifecycle', () => {
  it('collects three matching traces before one model request, never invoking an apply command', async () => {
    const { flow, calls } = harness(); const result = await flow.execute(input);
    expect(result.sessions).toHaveLength(3);
    expect(calls.filter(command => command === 'measurement-analyze')).toHaveLength(3);
    expect(calls.indexOf('diagnose')).toBeGreaterThan(calls.lastIndexOf('measurement-analyze'));
    expect(calls.filter(command => command === 'diagnose')).toHaveLength(1);
    expect(calls).not.toContain('apply'); expect(calls).not.toContain('run-approve');
  });
  it('keeps system-wide evidence diagnostic, not a workload baseline', async () => {
    const { flow } = harness(); const result = await flow.execute({ ...input, workload: undefined });
    expect(result.sessions).toHaveLength(1); expect(result.run.state).toBe('baselinePending');
  });
  it('cancellation during startup waits for the owned session, then releases only its recorder', async () => {
    const { flow, calls } = harness({ cancelDuringStart: true });
    await expect(flow.execute(input)).rejects.toThrow('cancelled');
    expect(calls).toContain('measurement-cancel'); expect(calls).not.toContain('diagnose');
  });
  it('fails quality checks before transmission and never presents provider fallback as final AI output', async () => {
    const invalid = harness({ invalidQuality: true });
    await expect(invalid.flow.execute(input)).rejects.toThrow('quality failed'); expect(invalid.calls).not.toContain('diagnose');
    const failed = harness({ failedProvider: true });
    await expect(failed.flow.execute(input)).rejects.toThrow('Provider failed'); expect(failed.calls).toContain('run-dismiss');
  });
  it('prepares optional text locally and rejects unconfirmed image support before measurements/provider requests', async () => {
    const report = { id: 'report', name: 'cpu-z.txt', kind: 'report' as const, contentType: 'text/plain' as const, content: 'Clock: 4200 MHz', sha256: '' };
    const text = harness(); await text.flow.execute({ ...input, attachments: [report] });
    expect(text.calls.indexOf('support-preview')).toBeLessThan(text.calls.indexOf('scan'));
    const images = harness();
    await expect(images.flow.execute({ ...input, attachments: [{ ...report, kind: 'image', contentType: 'image/png' }], imagesConfirmed: false })).rejects.toThrow('vision support');
    expect(images.calls).not.toContain('measurement-start'); expect(images.calls).not.toContain('diagnose');
  });
  it('forwards each objective unchanged with independent risk/context and the same measurement/approval lifecycle', async () => {
    for (const priority of ['balanced', 'systemLatency', 'networkLatency', 'stability'] as const) {
      const { flow, calls, requests } = harness();
      const selected = { ...goals, priority, riskProfile: 'safe' as const, notes: 'Preserve security', games: ['workload'] };
      await flow.execute({ ...input, goals: selected });
      for (const command of ['run-create', 'diagnose']) expect((requests.find(item => item.command === command)?.payload as { goals: TuningGoals } | undefined)?.goals).toEqual(selected);
      expect(calls.filter(command => command === 'measurement-start')).toHaveLength(3);
      expect(calls).not.toContain('run-approve'); expect(calls).not.toContain('apply');
    }
  });
  it('offers five prompt specializations and preserves a restored legacy selection without silently converting it', () => {
    const render = (priority: TuningGoals['priority']) => renderToStaticMarkup(<CompleteDiagnosis goals={{ ...goals, priority }} onGoals={() => {}} onStart={() => {}} onCancel={() => {}} blocked={false}/>);
    for (const priority of ['balanced', 'systemLatency', 'networkLatency', 'stability'] as const) {
      const html = render(priority);
      expect(html).toContain('Overall performance'); expect(html).toContain('System latency'); expect(html).toContain('Network optimization'); expect(html).toContain('System stability');
      expect(html).toContain('Windows privacy &amp; security');
      expect(html).toContain(`value="${priority}" selected=""`); expect(html).toContain('does not apply tweaks');
      expect(html).not.toContain('value="fps"'); expect(html).not.toContain('value="efficiency"');
    }
    expect(render('fps')).toContain('value="fps" selected=""'); expect(render('efficiency')).toContain('value="efficiency" selected=""');
  });
  it('audit mode skips every WPR/workload stage and persists independently of the selected focus', async () => {
    for (const priority of ['privacySecurity', 'balanced'] as const) {
      const { flow, calls, requests } = harness();
      const result = await flow.execute({ ...input, goals: { ...goals, priority }, mode: 'auditOnly', durationSeconds: 0 });
      expect(result.sessions).toEqual([]); expect(result.run.state).toBe('proposalReady');
      for (const command of ['measurement-start', 'measurement-analyze', 'measurement-cancel', 'apply', 'defender-scan']) expect(calls).not.toContain(command);
      expect((requests.find(item => item.command === 'run-create')?.payload as { mode: string } | undefined)?.mode).toBe('auditOnly');
      expect((requests.find(item => item.command === 'scan')?.payload as { privacySecurityReadConsent: boolean } | undefined)?.privacySecurityReadConsent).toBe(true);
      const html = renderToStaticMarkup(<CompleteDiagnosis mode="auditOnly" goals={{ ...goals, priority }} onGoals={() => {}} onStart={() => {}} onCancel={() => {}} blocked={false}/>);
      expect(html).toContain('Start AI audit'); expect(html).not.toContain('id="diagnosis-workload"'); expect(html).not.toContain('Seconds per trace');
    }
    const measured = harness();
    await measured.flow.execute({ ...input, goals: { ...goals, priority: 'privacySecurity' } });
    expect(measured.calls.filter(command => command === 'measurement-start')).toHaveLength(3);
    const failed = harness({ failedProvider: true });
    await expect(failed.flow.execute({ ...input, mode: 'auditOnly' })).rejects.toThrow('Provider failed');
    expect(failed.calls).toContain('run-dismiss'); expect(failed.calls).not.toContain('apply');
    const progress = renderToStaticMarkup(<CompleteDiagnosis mode="auditOnly" goals={goals} onGoals={() => {}} onStart={() => {}} onCancel={() => {}} blocked={false} progress={{ mode: 'auditOnly', stage: 'ai', message: 'Audit', startedAt: Date.now(), log: [] }}/>);
    expect(progress).not.toContain('Prepare workload'); expect(progress).not.toContain('Trace analysis'); expect(progress).toContain('AI investigation');
  });
  it('audit preview waits for explicit approval and cancellation sends nothing to the provider', async () => {
    const { flow, calls } = harness(); let approve: (() => void) | undefined; let ready: (() => void) | undefined;
    const previewReady = new Promise<void>(resolve => { ready = resolve; });
    const execution = flow.execute({ ...input, mode: 'auditOnly', reviewAudit: async () => { ready!(); await new Promise<void>(resolve => { approve = resolve; }); } });
    await previewReady;
    expect(calls).toContain('scan'); expect(calls).not.toContain('run-create'); expect(calls).not.toContain('diagnose');
    approve!(); await execution; expect(calls).toContain('diagnose');
    const cancelled = harness();
    await expect(cancelled.flow.execute({ ...input, mode: 'auditOnly', reviewAudit: async () => { throw new Error('Preview cancelled'); } })).rejects.toThrow('Preview cancelled');
    expect(cancelled.calls).not.toContain('diagnose'); expect(cancelled.calls).not.toContain('run-create');
    const html = renderToStaticMarkup(<CompleteDiagnosis mode="auditOnly" goals={goals} onGoals={() => {}} onStart={() => {}} onCancel={() => {}} blocked auditPreview={{ sanitizedProfile: '<script>private</script>' } as ScanResult} progress={{ mode: 'auditOnly', stage: 'scan', message: 'Local only', startedAt: Date.now(), log: [] }}/>);
    expect(html).toContain('no AI transmission'); expect(html).toContain('I authorize evidence transmission'); expect(html).toContain('Cancel without sending');
    expect(html).toContain('&lt;script&gt;'); expect(html).not.toContain('<script>');
  });
  it('renders actual phases, a timer and cancel control, without manufactured percentages', () => {
    const html = renderToStaticMarkup(<CompleteDiagnosis goals={goals} onGoals={() => {}} onStart={() => {}} onCancel={() => {}} blocked={false} progress={{ stage: 'ai', message: 'AI read-only follow-up · memory-pressure', startedAt: Date.now(), log: ['WPR recording', 'ETL quality checked'] }}/>);
    expect(html).toContain('Actual operation log'); expect(html).toContain('Cancel diagnosis'); expect(html).toContain('role="timer"'); expect(html).not.toContain('role="progressbar"');
  });
});
