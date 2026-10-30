import { MAX_REPORT_BYTES, exactKeys, requireInput, seal, text, verifySeal } from './io.mjs';

const states = ['running', 'stopped', 'start-pending', 'stop-pending', 'continue-pending', 'pause-pending', 'paused', 'unknown'];
const limitations = ['Registered desktop apps and observed Win32 services only; Store apps and usage are not inventoried.',
  'Names, publishers and dependency strings are untrusted data, not commands or removal recommendations.',
  'Registry/SCM source and coverage claims are unattested; missing/group dependencies remain unresolved.',
  'No service/app change, installation, verified backup or execution approval is provided.'];
const optional = value => value === null || text(value, 256);

export function normalizeAppsServices(raw) {
  exactKeys(raw, ['schemaVersion', 'kind', 'generatedAtUtc', 'services', 'apps', 'unavailable']);
  requireInput(raw.schemaVersion === 1 && raw.kind === 'apps-services-snapshot' && text(raw.generatedAtUtc) && Number.isFinite(Date.parse(raw.generatedAtUtc)), 'Unsupported app/service snapshot.');
  requireInput(Array.isArray(raw.services) && raw.services.length <= 512 && Array.isArray(raw.apps) && raw.apps.length <= 1000, 'App/service snapshot exceeds its row limits.');
  exactKeys(raw.unavailable, ['services', 'appLocations']);
  requireInput(Object.values(raw.unavailable).every(value => Number.isSafeInteger(value) && value >= 0 && value <= 10000), 'Invalid app/service coverage counts.');
  const names = new Set(), ids = new Set();
  const services = raw.services.map(service => {
    exactKeys(service, ['name', 'displayName', 'startType', 'delayedAutomatic', 'state', 'dependencies']);
    requireInput(text(service.name, 256) && !names.has(service.name.toLowerCase()) && optional(service.displayName), 'Invalid or duplicate service name.');
    names.add(service.name.toLowerCase());
    requireInput(Number.isInteger(service.startType) && service.startType >= 0 && service.startType <= 4 && typeof service.delayedAutomatic === 'boolean' && states.includes(service.state), 'Invalid observed service state.');
    requireInput(Array.isArray(service.dependencies) && service.dependencies.length <= 128 && service.dependencies.every(value => text(value, 256)) &&
      new Set(service.dependencies.map(value => value.toLowerCase())).size === service.dependencies.length, 'Invalid service dependencies.');
    return structuredClone(service);
  }).sort((a, b) => a.name.localeCompare(b.name, 'en'));
  const apps = raw.apps.map(app => {
    exactKeys(app, ['id', 'scope', 'name', 'version', 'publisher', 'systemComponent']);
    requireInput(typeof app.id === 'string' && /^[a-f0-9]{64}$/u.test(app.id) && !ids.has(app.id), 'Invalid or duplicate app reference.'); ids.add(app.id);
    requireInput(['machine64', 'machine32', 'user'].includes(app.scope) && text(app.name, 256) && optional(app.version) && optional(app.publisher) && typeof app.systemComponent === 'boolean', 'Invalid registered app metadata.');
    return structuredClone(app);
  }).sort((a, b) => a.id.localeCompare(b.id, 'en'));
  const unresolvedDependencies = services.flatMap(service => service.dependencies.filter(name => name.startsWith('+') || !names.has(name.toLowerCase())).map(dependency => ({ service: service.name, dependency })));
  const report = seal({ schemaVersion: 1, kind: 'apps-services', provenance: 'local-registry-scm-unattested',
    collectedAt: new Date(raw.generatedAtUtc).toISOString(), facts: { services, apps, unresolvedDependencies }, unavailable: structuredClone(raw.unavailable), limitations: [...limitations] });
  requireInput(Buffer.byteLength(JSON.stringify(report)) <= MAX_REPORT_BYTES, 'Normalized app/service report exceeds 512 KiB.');
  return report;
}
export function validateAppsServices(report) {
  verifySeal(report);
  exactKeys(report, ['schemaVersion', 'kind', 'provenance', 'collectedAt', 'facts', 'unavailable', 'limitations', 'id']);
  exactKeys(report.facts, ['services', 'apps', 'unresolvedDependencies']);
  const expected = normalizeAppsServices({ schemaVersion: report.schemaVersion, kind: 'apps-services-snapshot', generatedAtUtc: report.collectedAt,
    services: report.facts.services, apps: report.facts.apps, unavailable: report.unavailable });
  requireInput(report.id === expected.id, 'App/service report does not match its sources or derived dependencies.');
  return report;
}
