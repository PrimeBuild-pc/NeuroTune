import { SystemOneNotes } from '../SystemOnePanel';
import type { Diagnosis } from '../types';
import { t } from '../i18n';

export function DiagnosisView({ diagnosis }: { diagnosis: Diagnosis }) {
  return <div className="diagnosis"><p>{diagnosis.summary}</p><SystemOneNotes items={diagnosis.systemOneAdvisories}/>
    {diagnosis.findings.length > 0 && <><h4>{t('Findings · citations establish provenance, not truth')}</h4><div className="finding-list">{diagnosis.findings.map(item => <article key={item.evidenceId}>
      <strong>{item.title}</strong>{item.evidenceId.startsWith('support:') && <small>{t('User-supplied support · unverified')}</small>}
      <code>{item.evidenceId}: {item.currentValue}</code><p>{item.assessment}</p>
    </article>)}</div></>}
  </div>;
}
