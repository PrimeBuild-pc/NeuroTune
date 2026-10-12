import { Check, ChevronRight } from 'lucide-react';
import { t } from '../i18n';
import type { OptimizationRun } from '../types';
import { audit, journeyStep, measured } from '../journey';

export function JourneyBar({ run, next, disabled }: { run?: OptimizationRun; next?: { label: string; onClick: () => void }; disabled?: boolean }) {
  const steps = run?.mode === 'auditOnly' ? audit : measured;
  const current = journeyStep(run);
  return <section className="journey-bar" aria-label={t('Guided path')}>
    <ol>{steps.map((step, index) => <li key={step} className={index < current ? 'done' : index === current ? 'current' : undefined} aria-current={index === current ? 'step' : undefined}>
      <span aria-hidden="true">{index < current ? <Check size={13}/> : index + 1}</span>{t(step)}
    </li>)}</ol>
    {next && <button className="primary" disabled={disabled} onClick={next.onClick}>{t('Next step')}: {t(next.label)}<ChevronRight size={16} aria-hidden="true"/></button>}
  </section>;
}
