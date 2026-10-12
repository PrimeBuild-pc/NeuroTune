import { Bot, ChevronRight, RotateCcw, ScanLine } from 'lucide-react';
import type { DiagnosisStage } from '../diagnosisFlow';
import { t } from '../i18n';
import type { OperationManifest, ScanResult } from '../types';
import { Metric } from './Metric';
import { NeuralVisualizer } from './NeuralVisualizer';
import './Overview.css';

export function Overview({ hasProvider, scan, history, coreState, onProvider, onScan, onPrivacy, nextAction, disabled = false }: {
  hasProvider: boolean; scan?: ScanResult; history: OperationManifest[];
  coreState: DiagnosisStage | 'idle'; onProvider: () => void; onScan: () => void; onPrivacy?: () => void;
  nextAction?: { label: string; onClick: () => void }; disabled?: boolean;
}) {
  return <div className="stack-xl overview-page">
    <section className="hero-panel">
      <div className="hero-copy">
        <span className="kicker">{t('Controlled system tuning')}</span>
        <h2>{t('Understand the machine.')}<br/>{t('Change only what is safe.')}</h2>
        <p>{t('Your AI investigates evidence and measurements. You review the plan; NeuroTune backs up, applies and verifies approved changes.')}</p>
        <div className="button-row">
          <button className="primary" disabled={disabled && !nextAction} onClick={nextAction?.onClick ?? (hasProvider ? onScan : onProvider)}>{nextAction ? t(nextAction.label) : hasProvider ? t('Complete diagnosis') : t('Connect a provider')}<ChevronRight size={17} aria-hidden="true"/></button>
          {hasProvider && <button className="ghost" disabled={disabled} onClick={onProvider}>{t('Provider settings')}</button>}
          {onPrivacy && <button className="ghost" disabled={disabled} onClick={onPrivacy}>{t('Privacy & security')}</button>}
        </div>
      </div>
      <NeuralVisualizer state={coreState}/>
    </section>
    <div className="metric-grid three overview-metrics">
      <Metric icon={Bot} label={t('AI provider')} value={hasProvider ? t('Configured') : t('Not configured')} tone={hasProvider ? 'good' : 'warn'}/>
      <Metric icon={ScanLine} label={t('System profile')} value={scan ? t('Ready') : t('Not scanned')}/>
      <Metric icon={RotateCcw} label={t('Recoverable operations')} value={String(history.filter(item => item.actions.some(action => action.applied && !action.rolledBack)).length)}/>
    </div>
  </div>;
}

