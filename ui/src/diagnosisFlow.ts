import { agent, cancelAgent, newRequestId } from './agent';
import { preparatoryBaselines } from './plan';
import { getLanguage, t } from './i18n';
import type { Diagnosis, InvestigationMode, MeasurementSession, MeasurementWorkload, OptimizationRun, ScanResult, SupportingAttachment, TuningGoals } from './types';

export type DiagnosisStage = 'scan' | 'warmup' | 'capture' | 'trace' | 'ai' | 'result';
export interface DiagnosisProgress {
  stage: DiagnosisStage; message: string; startedAt: number; log: string[]; mode?: InvestigationMode;
  repeat?: number; repeats?: number; recordingStartedAt?: string; durationSeconds?: number; warmupEndsAt?: number;
}
interface Input { mode?: InvestigationMode; goals: TuningGoals; workload?: MeasurementWorkload; durationSeconds: number; optionalTelemetryConsent: boolean; firmwareReadConsent: boolean; attachments?: SupportingAttachment[]; imagesConfirmed?: boolean; reviewAudit?: (scan: ScanResult) => Promise<void>; }
interface Dependencies { invoke: typeof agent; cancel: typeof cancelAgent; wait: (milliseconds: number) => Promise<void>; }

// One lifecycle owner: cancellation never kills a capture-start/stop call before its named session is known.
export class DiagnosisFlow {
  private aborted = false;
  private request?: { id: string; cancellable: boolean };
  private ownedSession?: MeasurementSession;
  private runId?: string;
  private progress: DiagnosisProgress = { stage: 'scan', message: t('Starting complete diagnosis…'), startedAt: Date.now(), log: [] };
  private publish: (value: DiagnosisProgress) => void;
  private deps: Dependencies;
  constructor(publish: (value: DiagnosisProgress) => void, deps: Dependencies = {
    invoke: agent, cancel: cancelAgent, wait: milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds)),
  }) { this.publish = publish; this.deps = deps; this.publish(this.progress); }
  note(requestId: string, message: string) {
    if (this.request?.id !== requestId) return;
    this.progress = { ...this.progress, message, log: [...this.progress.log, message].slice(-40) }; this.publish(this.progress);
  }
  async cancel() {
    this.aborted = true;
    this.update(this.progress.stage, t('Cancelling diagnosis; waiting for owned recorder operations to finish safely…'));
    if (this.request?.cancellable) await this.deps.cancel(this.request.id);
  }
  private check() { if (this.aborted) throw new Error(t('Complete diagnosis cancelled. Completed reports remain local; no changes were applied.')); }
  private update(stage: DiagnosisStage, message: string, fields: Partial<DiagnosisProgress> = {}) {
    this.progress = { stage, message, mode: this.progress.mode, startedAt: this.progress.startedAt, log: [...this.progress.log, message].slice(-40), ...fields };
    this.publish(this.progress);
  }
  private async call<T>(command: string, payload?: unknown, cancellable = false): Promise<T> {
    this.check();
    const id = newRequestId(); this.request = { id, cancellable };
    try { return await this.deps.invoke<T>(command, payload, id); }
    finally { this.request = undefined; }
  }
  async execute(input: Input): Promise<{ scan: ScanResult; diagnosis: Diagnosis; run: OptimizationRun; sessions: MeasurementSession[] }> {
    const language = getLanguage();
    const mode = input.mode ?? 'measuredOptimization';
    if (!['measuredOptimization', 'auditOnly'].includes(mode)) throw new Error(t('Unknown investigation mode.'));
    this.progress.mode = mode;
    if (mode !== 'auditOnly' && (input.durationSeconds < 30 || input.durationSeconds > 600 || !Number.isInteger(input.durationSeconds))) throw new Error(t('Capture duration must be 30–600 seconds.'));
    try {
      this.update('scan', t('Checking for an existing NeuroTune capture before local collection…'));
      const existing = await this.call<MeasurementSession[]>('measurement-list');
      if (existing.some(session => session.state === 'recording')) throw new Error(t('Finish the existing capture before starting complete diagnosis. It was not cancelled.'));
      if (input.attachments?.length) {
        this.update('scan', t('Validating optional supporting files locally; no provider request…'));
        input.attachments = await this.call<SupportingAttachment[]>('support-preview', { attachments: input.attachments });
        if (input.attachments.some(item => item.kind === 'image') && !input.imagesConfirmed) throw new Error(t('Confirm screenshot review and selected-model vision support before diagnosis.'));
      }
      this.update('scan', t('Collecting hardware, Windows, driver and software evidence locally…'));
      const scan = await this.call<ScanResult>('scan', { optionalTelemetryConsent: input.optionalTelemetryConsent, firmwareReadConsent: input.firmwareReadConsent, privacySecurityReadConsent: mode === 'auditOnly' || input.goals.priority === 'privacySecurity' }, true);
      this.check();
      if (mode === 'auditOnly' && input.reviewAudit) {
        this.update('scan', t('Audit evidence is ready locally. Review the prepared profile before consenting to provider transmission.'));
        await input.reviewAudit(scan); this.check();
      }
      const repeats = input.workload ? 3 : 1;
      const sessions: MeasurementSession[] = [];
      if (mode !== 'auditOnly') {
        this.update('warmup', input.workload ? t('Return to the selected workload and keep a repeatable scene running. Capture begins in 10 seconds.') : t('Preparing a system-wide diagnostic snapshot; this is not an FPS benchmark.'), { warmupEndsAt: Date.now() + 10_000 });
        for (let second = 0; second < 10; second++) { this.check(); await this.deps.wait(1000); }
        for (let repeat = 1; repeat <= repeats; repeat++) {
          this.check();
          this.update('capture', t('Preparing trace {repeat}/{repeats}; countdown starts only after WPR is ready.', { repeat, repeats }), { repeat, repeats });
          const session = await this.call<MeasurementSession>('measurement-start', {
            processId: input.workload?.processId ?? 0, processStartTimeUtc: input.workload?.startTimeUtc ?? '0001-01-01T00:00:00Z',
            systemWide: !input.workload, label: 'baseline', durationSeconds: input.durationSeconds, keepRawTrace: false,
          });
          this.ownedSession = session; this.check();
          if (!session.recordingStartedAtUtc || session.state !== 'recording') throw new Error(t('The recorder did not confirm capture readiness.'));
          this.update('capture', t('Recording trace {repeat}/{repeats}. Neither AI is running.', { repeat, repeats }), { repeat, repeats, recordingStartedAt: session.recordingStartedAtUtc, durationSeconds: session.durationSeconds });
          // The existing independent watchdog owns the deadline, including app closure. Polling never starts/stops another session.
          const deadline = new Date(session.recordingStartedAtUtc).getTime() + session.durationSeconds * 1000;
          const finishBy = deadline + 120_000;
          // Do not repeatedly spawn native agents during the measured interval. Only UI timers and the recorder watchdog run.
          while (Date.now() < deadline) { this.check(); await this.deps.wait(Math.min(1000, deadline - Date.now())); }
          let captured = session;
          while (captured.state === 'recording') {
            this.check();
            const history = await this.call<MeasurementSession[]>('measurement-list');
            captured = history.find(item => item.id === session.id) ?? (() => { throw new Error(t('The owned capture disappeared.')); })();
            if (Date.now() > finishBy) throw new Error(t('Recorder finalization exceeded its deadline; diagnosis stopped.'));
            if (captured.state === 'recording') await this.deps.wait(1000);
          }
          if (captured.state !== 'captured') throw new Error(captured.error ?? t('Capture did not complete successfully.'));
          this.update('trace', t('Reading ETL and checking trace quality {repeat}/{repeats} locally…', { repeat, repeats }), { repeat, repeats });
          const analyzed = await this.call<MeasurementSession>('measurement-analyze', { sessionId: session.id }, true);
          this.ownedSession = undefined; this.check();
          if (analyzed.state !== 'completed' || !analyzed.report?.quality.isValid) throw new Error(t('Trace quality failed. No AI result or system change was manufactured. Inspect the retained report in advanced measurements.'));
          sessions.push(analyzed);
        }
        if (input.workload && preparatoryBaselines(sessions, new Set(sessions.map(item => item.id))).length !== 3)
          throw new Error(t('Baseline repeats do not match; diagnosis stopped before provider transmission.'));
      }
      this.update('ai', mode === 'auditOnly' ? t('Audit evidence collected without ETW. The selected AI may request read-only observations; no scanner or change is authorized.') : t('Mandatory evidence collected. The selected AI can now investigate additional read-only aspects and propose a plan.'));
      const run = await this.call<OptimizationRun>('run-create', { profile: scan.profile, goals: input.goals, mode, measurementSessionIds: sessions.map(item => item.id), attachments: input.attachments ?? [], imagesConfirmed: input.imagesConfirmed ?? false });
      this.runId = run.id; this.check();
      const diagnosis = await this.call<Diagnosis>('diagnose', { profile: scan.profile, goals: input.goals, language, runId: run.id, attachments: input.attachments ?? [], imagesConfirmed: input.imagesConfirmed ?? false }, true);
      this.check();
      const finalRun = await this.call<OptimizationRun>('run-get', { runId: run.id });
      if (mode === 'auditOnly' ? finalRun.state !== 'proposalReady' : finalRun.state !== 'baselineReady' && finalRun.state !== 'baselinePending') throw new Error(diagnosis.summary || t('The AI did not complete a validated diagnosis. No deterministic plan was substituted.'));
      this.update('result', t('AI analysis completed. No changes applied; review and choose your interventions.'));
      return { scan, diagnosis, run: finalRun, sessions };
    } catch (error) {
      const cleanup: string[] = [];
      if (this.ownedSession) {
        try {
          const sessions = await this.deps.invoke<MeasurementSession[]>('measurement-list');
          const current = sessions.find(item => item.id === this.ownedSession!.id);
          if (current?.state === 'recording') await this.deps.invoke('measurement-cancel', { sessionId: current.id });
        } catch (reason) { cleanup.push(t('Recorder cleanup needs attention: {error}', { error: String(reason) })); }
      }
      if (this.runId) {
        try { await this.deps.invoke('run-dismiss', { runId: this.runId }); }
        catch (reason) { cleanup.push(t('Run recovery needs attention: {error}', { error: String(reason) })); }
      }
      throw new Error([this.aborted ? t('Complete diagnosis cancelled. Completed reports remain local; no changes were applied.') : String(error), ...cleanup].join(' '));
    }
  }
}
