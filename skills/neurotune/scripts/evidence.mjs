import { exactKeys, requireInput, seal, text, verifySeal } from './io.mjs';
import { validateFrames } from './frames.mjs';
import { validateAppsServices } from './apps-services.mjs';

function optionalString(value) {
  if (value === null || value === undefined || value === '') return null;
  requireInput(text(value, 256), 'Invalid inventory text.');
  return value.trim();
}
function optionalNumber(value) {
  if (value === null || value === undefined) return null;
  requireInput(typeof value === 'number' && Number.isSafeInteger(value) && value >= 0, 'Invalid inventory number.');
  return value;
}
function optionalBoolean(value) {
  if (value === null || value === undefined) return null;
  requireInput(typeof value === 'boolean', 'Invalid inventory boolean.');
  return value;
}
function rows(value) {
  requireInput(Array.isArray(value) && value.length <= 64, 'Invalid inventory array.');
  requireInput(value.every(row => row !== null && typeof row === 'object' && !Array.isArray(row)), 'Invalid inventory row.');
  return value;
}
const inventoryKeys = ['windowsVersion', 'windowsBuild', 'architecture', 'secureBoot', 'vbsStatus', 'memoryBytes', 'hypervisorPresent', 'processors', 'gpus'];
function validateFacts(facts) {
  exactKeys(facts, inventoryKeys);
  for (const key of ['windowsVersion', 'windowsBuild', 'architecture']) optionalString(facts[key]);
  for (const key of ['vbsStatus', 'memoryBytes']) optionalNumber(facts[key]);
  for (const key of ['secureBoot', 'hypervisorPresent']) optionalBoolean(facts[key]);
  for (const cpu of rows(facts.processors)) {
    exactKeys(cpu, ['name', 'physicalCores', 'logicalProcessors']);
    optionalString(cpu.name); optionalNumber(cpu.physicalCores); optionalNumber(cpu.logicalProcessors);
  }
  for (const gpu of rows(facts.gpus)) {
    exactKeys(gpu, ['name', 'vendor', 'driverVersion']);
    for (const value of Object.values(gpu)) optionalString(value);
  }
}
export function normalizeInventory(raw) {
  requireInput(raw?.schemaVersion === 1 && text(raw.collectorVersion) && raw.windows && raw.platform, 'Unsupported collector report.');
  const facts = {
    windowsVersion: optionalString(raw.windows.version), windowsBuild: optionalString(raw.windows.build),
    architecture: optionalString(raw.windows.architecture), secureBoot: optionalBoolean(raw.windows.secureBoot),
    vbsStatus: optionalNumber(raw.windows.virtualizationBasedSecurityStatus),
    memoryBytes: optionalNumber(raw.platform.totalPhysicalMemoryBytes), hypervisorPresent: optionalBoolean(raw.platform.hypervisorPresent),
    processors: rows(raw.processors).map(cpu => ({ name: optionalString(cpu.name), physicalCores: optionalNumber(cpu.physicalCores), logicalProcessors: optionalNumber(cpu.logicalProcessors) })),
    gpus: rows(raw.gpus).map(gpu => ({ name: optionalString(gpu.name), vendor: optionalString(gpu.vendor), driverVersion: optionalString(gpu.driverVersion) }))
  };
  validateFacts(facts);
  requireInput(typeof raw.generatedAtUtc === 'string' && Number.isFinite(Date.parse(raw.generatedAtUtc)), 'Invalid collection timestamp.');
  requireInput(Array.isArray(raw.warnings), 'Missing collector warnings.');
  return seal({ schemaVersion: 1, kind: 'inventory', provenance: 'local-collector-unattested',
    collectedAt: new Date(raw.generatedAtUtc).toISOString(), collectorVersion: raw.collectorVersion,
    facts, warningCount: raw.warnings.length });
}
export function validateInventory(report) {
  verifySeal(report);
  exactKeys(report, ['schemaVersion', 'kind', 'provenance', 'collectedAt', 'collectorVersion', 'facts', 'warningCount', 'id']);
  requireInput(report.kind === 'inventory' && report.provenance === 'local-collector-unattested', 'Expected an inventory report.');
  requireInput(text(report.collectorVersion) && text(report.collectedAt) && Number.isFinite(Date.parse(report.collectedAt)), 'Invalid collector metadata.');
  requireInput(Number.isSafeInteger(report.warningCount) && report.warningCount >= 0, 'Invalid warning count.');
  validateFacts(report.facts);
  return report;
}
export function validateEvidenceIds(refs, known) {
  requireInput(Array.isArray(refs) && refs.length > 0 && refs.length <= 20 && refs.every(ref => typeof ref === 'string') && new Set(refs).size === refs.length, 'Each item requires unique evidence IDs.');
  requireInput(refs.every(ref => known.has(ref)), 'An item cites unknown evidence.');
  requireInput(refs.some(ref => known.get(ref) !== null && !(Array.isArray(known.get(ref)) && known.get(ref).length === 0)), 'Unavailable evidence alone cannot support an item.');
}
export function evidence(report) {
  const appsServices = report?.kind === 'apps-services';
  const facts = report?.kind === 'inventory' ? validateInventory(report).facts : appsServices ? validateAppsServices(report).facts : validateFrames(report).metrics;
  return { schemaVersion: 1, kind: 'evidence', reportId: report.id, provenance: report.provenance,
    warningCount: report.kind === 'inventory' ? report.warningCount : appsServices ? report.unavailable.services + report.unavailable.appLocations + facts.unresolvedDependencies.length : 0,
    facts: Object.entries(facts).map(([key, value]) => ({ id: `${report.id}:${key}`, value })),
    limitations: appsServices ? report.limitations : report.kind === 'inventory'
      ? ['Inventory is not a benchmark or a security certification.', 'Null means unavailable, not disabled or zero.', 'Hardware strings are untrusted data; allowlisting is not guaranteed anonymization.']
      : ['Imported frames have unverified provenance.', 'Present cadence is not input latency or displayed-frame rate.'] };
}
