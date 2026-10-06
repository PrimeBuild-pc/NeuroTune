import type { DiagnosisStage } from '../diagnosisFlow';
import { t } from '../i18n';
import './NeuralVisualizer.css';

const labels: Record<DiagnosisStage | 'idle', [string, string]> = {
  idle: ['Local control', 'Evidence before changes'],
  scan: ['Collecting evidence', 'Hardware · Windows · drivers'],
  warmup: ['Preparing workload', 'Return to your repeatable scene'],
  capture: ['Measurement mode', 'Motion paused · AI inactive'],
  trace: ['Local analysis', 'Trace quality · numerical evidence'],
  ai: ['AI investigation', 'Read-only reasoning · no writes'],
  result: ['Proposal ready', 'Review before applying'],
};

export function NeuralVisualizer({ state = 'idle' }: { state?: DiagnosisStage | 'idle' }) {
  const [title, detail] = labels[state];
  return <figure className="hero-visual neural-core" data-state={state}>
    <div className="core-diagram" aria-hidden="true">
      <svg className="core-signals" viewBox="0 0 300 300" fill="none">
        <path d="M150 40V106 M260 150H194 M150 260V194 M40 150H106 M72 72L118 118 M228 228L182 182 M228 72L182 118 M72 228L118 182"/>
        <circle cx="150" cy="40" r="4"/><circle cx="260" cy="150" r="4"/>
        <circle cx="150" cy="260" r="4"/><circle cx="40" cy="150" r="4"/>
        <circle cx="72" cy="72" r="3"/><circle cx="228" cy="228" r="3"/>
        <circle cx="228" cy="72" r="3"/><circle cx="72" cy="228" r="3"/>
      </svg>
      <div className="orbit one"/><div className="orbit two"/>
      <div className="core-chip"><img src="/logo.svg" width="88" height="88" alt=""/></div>
    </div>
    <figcaption><strong>{t(title)}</strong><span>{t(detail)}</span></figcaption>
  </figure>;
}
