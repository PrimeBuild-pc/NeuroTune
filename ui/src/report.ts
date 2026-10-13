import { formatDate, getLanguage, t } from './i18n';
import type { BiosChecklistItem, MeasurementComparison, MeasurementSession } from './types';

export function metricName(id: string) {
  const key = id.replace(/^comparison:[^:]+:/, '').replace(/:median_delta_percent$/, '');
  const labels: Record<string, string> = {
    'interrupt:system:worst_module_p99_us': 'Worst driver interrupt P99',
    'fault:system:count': 'Hard pagefault count',
    'fault:system:worst_process_p99_us': 'Worst hard pagefault P99',
    'target:worst_thread_ready_p99_us': 'Worst target Ready Time P99',
    'target:migrations': 'Target thread migrations',
    'frames:average_fps': 'Average FPS', 'frames:one_percent_low_fps': '1% low',
    'frames:p99_ms': 'Frame-time P99', 'frames:stutter_count': 'Stutters',
  };
  return { name: labels[key] ? t(labels[key]) : key.replace(/^interrupt:/, '').replace(/:worst_core_p99_us$/, ' · P99'),
    unit: key.endsWith('_us') ? 'µs' : key.endsWith('_ms') ? 'ms' : key.endsWith('_fps') ? 'FPS' : '' };
}

const escape = (value: unknown) => String(value).replace(/[&<>"']/g, character => `&#${character.charCodeAt(0)};`);

// A self-contained, script-free HTML page: every value is escaped and nothing is loaded from the network.
export function comparisonReportHtml(comparison: MeasurementComparison, sessions: MeasurementSession[], changes: string[] = []) {
  const side = (ids: string[]) => ids.map(id => sessions.find(item => item.id === id)).filter(item => item !== undefined)
    .map(item => `<li>${escape(formatDate(item.createdAtUtc))} · ${escape(item.conditions || t('Conditions not recorded'))} · ${escape(item.systemWide ? t('System-wide diagnostic') : item.processName)} · ${escape(item.durationSeconds)} s</li>`).join('');
  const rows = comparison.metrics.map(metric => {
    const { name, unit } = metricName(metric.evidenceId);
    const outcome = comparison.diagnosticOnly ? '' : t(metric.outcome === 'improvement' ? 'Improvement' : metric.outcome === 'regression' ? 'Regression' : 'Inconclusive');
    return `<tr><th>${escape(name)}</th><td>${escape(metric.baselineMedian.toFixed(2))} ${escape(unit)}</td><td>${escape(metric.candidateMedian.toFixed(2))} ${escape(unit)}</td><td>${escape(metric.deltaPercent.toFixed(1))}%</td><td>${escape(outcome)}</td></tr>`;
  }).join('');
  const verdict = comparison.rejectionReasons.length ? t('Comparison rejected') : comparison.diagnosticOnly ? t('Diagnostic differences · not a benchmark verdict')
    : comparison.level === 'repeated' ? t('Repeated comparison') : t('Exploratory comparison');
  return `<!doctype html><html lang="${escape(getLanguage())}"><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
<title>${escape(t('NeuroTune before/after report'))}</title><style>body{font:14px/1.5 system-ui,sans-serif;max-width:900px;margin:32px auto;padding:0 16px;color:#1b1d26}table{border-collapse:collapse;width:100%}th,td{border-bottom:1px solid #ddd;padding:6px 8px;text-align:left}small{color:#666}</style></head><body>
<h1>${escape(t('NeuroTune before/after report'))}</h1><p><small>${escape(formatDate(new Date().toISOString()))} · ${escape(t('Results are local observations, not guaranteed performance gains.'))}</small></p>
<h2>${escape(verdict)}</h2><p>${escape(comparison.recommendationReason)}</p>
${comparison.rejectionReasons.length ? `<ul>${comparison.rejectionReasons.map(reason => `<li>${escape(reason)}</li>`).join('')}</ul>` : ''}
${changes.length ? `<h2>${escape(t('Changes applied'))}</h2><ul>${changes.map(change => `<li>${escape(change)}</li>`).join('')}</ul>` : ''}
<h2>${escape(t('Before measurement'))}</h2><ul>${side(comparison.baselineSessionIds)}</ul>
<h2>${escape(t('After measurement'))}</h2><ul>${side(comparison.candidateSessionIds)}</ul>
${rows ? `<h2>${escape(t('Measured differences'))}</h2><table><thead><tr><th>${escape(t('Metric'))}</th><th>${escape(t('Before'))}</th><th>${escape(t('After'))}</th><th>${escape(t('Change'))}</th><th>${escape(t('Interpretation'))}</th></tr></thead><tbody>${rows}</tbody></table>` : ''}
</body></html>`;
}

// Same self-contained format, so the checklist can be opened on a phone while the PC is in the firmware setup.
export function biosChecklistHtml(items: BiosChecklistItem[]) {
  const rows = items.map(item => `<tr><th>${escape(item.setting)}</th><td>${escape(item.value)}</td><td>${escape(item.menuPath)}</td><td>${escape(item.reason)}</td><td>${escape(t(item.source === 'general' ? 'Unverified (general knowledge)' : item.source === 'scewin' ? 'From the BIOS dump' : 'From the manual'))}</td></tr>`).join('');
  return `<!doctype html><html lang="${escape(getLanguage())}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
<title>${escape(t('BIOS checklist'))}</title><style>body{font:15px/1.5 system-ui,sans-serif;margin:16px;color:#1b1d26}table{border-collapse:collapse;width:100%}th,td{border-bottom:1px solid #ddd;padding:6px;text-align:left;vertical-align:top}</style></head><body>
<h1>${escape(t('BIOS checklist'))}</h1><p>${escape(t('NeuroTune changes nothing in the BIOS. After EXPO/PBO/Curve Optimizer, run a stability test (for example OCCT or TM5) before measuring.'))}</p>
<table><tbody>${rows}</tbody></table></body></html>`;
}

export function downloadFile(content: string, type: string, filename: string) {
  const url = URL.createObjectURL(new Blob([content], { type }));
  const link = document.createElement('a');
  link.href = url; link.download = filename; link.click();
  URL.revokeObjectURL(url);
}
