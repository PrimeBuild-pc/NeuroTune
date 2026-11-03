import { createReadStream } from 'node:fs';
import { stat } from 'node:fs/promises';
import { createInterface } from 'node:readline';
import { exactKeys, readBytes, requireInput, seal, sha256, text, verifySeal } from './io.mjs';

// A busy gaming PC can produce hundreds of MB of tracerpt CSV per minute.
export const MAX_TRACE_BYTES = 4 * 1024 ** 3;
export const MAX_LATENCYMON_BYTES = 2 * 1024 * 1024;
const MAX_LINE = 16384, TOP_MODULES = 15, MAX_CPUS = 256;
const factKeys = ['source', 'traceSeconds', 'logicalProcessors', 'eventsLost', 'dpcCount', 'isrCount', 'highestDpcUs', 'highestDpcModule',
  'highestIsrUs', 'highestIsrModule', 'highestTotalDpcModule', 'highestTotalIsrModule', 'dpcTimePercent', 'isrTimePercent',
  'dpcOver500UsCount', 'dpcOver1000UsCount', 'highestInterruptToProcessUs', 'hardPagefaults', 'perCpu', 'topModules'];
const cpuKeys = ['cpu', 'dpcPercent', 'isrPercent', 'dpcMaxUs', 'isrMaxUs', 'dpcCount', 'isrCount'];
const moduleKeys = ['module', 'dpcCount', 'dpcMaxUs', 'dpcTotalMs', 'isrCount', 'isrMaxUs', 'isrTotalMs'];
const round = (value, digits = 3) => value === null ? null : Number(value.toFixed(digits));

// tracerpt quotes file names that may contain commas.
export function splitCsvLine(line) {
  const out = []; let field = '', quoted = false;
  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (quoted) { if (ch === '"') { if (line[i + 1] === '"') { field += '"'; i++; } else quoted = false; } else field += ch; }
    else if (ch === '"') quoted = true;
    else if (ch === ',') { out.push(field.trim()); field = ''; }
    else field += ch;
  }
  out.push(field.trim());
  return out;
}

const moduleName = file => {
  const base = String(file).split(/[\\/]/u).pop().trim();
  return text(base, 128) ? base : 'unknown';
};

/** Aggregates DPC/ISR durations per driver and per CPU from `tracerpt -of CSV` output of a dpc,isr,img kernel trace. */
export async function analyzeKernelTrace(file, { eventsLost = null } = {}) {
  const info = await stat(file);
  requireInput(info.isFile() && info.size <= MAX_TRACE_BYTES, 'Trace CSV must be a regular file within 4 GiB.');
  const lines = createInterface({ input: createReadStream(file, { encoding: 'latin1' }), crlfDelay: Infinity });
  let header = null, cpuRows = 0, minClock = null, maxClock = null, invalid = 0;
  const routines = new Map(), images = [], cpus = new Map();
  const cpu = index => { if (!cpus.has(index)) cpus.set(index, { dpcUs: 0, isrUs: 0, dpcMax: 0, isrMax: 0, dpc: 0, isr: 0 }); return cpus.get(index); };
  let dpcCount = 0, isrCount = 0, dpcOver500 = 0, dpcOver1000 = 0;
  for await (const line of lines) {
    requireInput(line.length <= MAX_LINE, 'Trace CSV line exceeds the length limit.');
    if (!header) {
      if (!/^\s*Event Name\s*,/u.test(line)) continue;
      const names = splitCsvLine(line);
      header = { cpu: names.indexOf('Processor Number'), clock: names.indexOf('Clock-Time'), data: names.indexOf('User Data') };
      requireInput(header.cpu > 0 && header.clock > 0 && header.data > header.clock, 'Not a tracerpt CSV with processor, clock and user-data columns.');
      continue;
    }
    const f = splitCsvLine(line), name = f[0], type = f[1];
    if (name === 'SystemConfig' && type === 'Processors') { cpuRows++; continue; }
    if (name === 'Image' && (type === 'DCStart' || type === 'Load')) {
      // Payload: ImageBase, ImageSize, ProcessId, ..., FileName. Kernel drivers live in process 0.
      const [base, size, pid] = f.slice(header.data, header.data + 3);
      if (pid === '0' && /^0x[0-9a-f]+$/iu.test(base) && /^0x[0-9a-f]+$/iu.test(size))
        images.push({ base: BigInt(base), end: BigInt(base) + BigInt(size), name: moduleName(f[f.length - 1]) });
      continue;
    }
    if (name !== 'PerfInfo' || !/^(?:DPC|TimerDPC|ThreadedDPC|ISR|ISR-MSI)$/u.test(type)) continue;
    const clock = f[header.clock], initial = f[header.data], routine = f[header.data + 1], processor = Number(f[header.cpu]);
    if (!/^\d+$/u.test(clock) || !/^\d+$/u.test(initial) || !/^0x[0-9a-f]+$/iu.test(routine) || !Number.isInteger(processor) || processor < 0 || processor >= MAX_CPUS) { invalid++; continue; }
    const end = BigInt(clock), us = Number(end - BigInt(initial)) / 10; // FILETIME units of 100 ns
    if (!(us >= 0 && us < 1e6)) { invalid++; continue; }
    if (minClock === null || end < minClock) minClock = end;
    if (maxClock === null || end > maxClock) maxClock = end;
    const isr = type.startsWith('ISR'), key = routine.toLowerCase();
    let r = routines.get(key);
    if (!r) routines.set(key, r = { dpc: 0, dpcUs: 0, dpcMax: 0, isr: 0, isrUs: 0, isrMax: 0 });
    const c = cpu(processor);
    if (isr) { isrCount++; r.isr++; r.isrUs += us; r.isrMax = Math.max(r.isrMax, us); c.isr++; c.isrUs += us; c.isrMax = Math.max(c.isrMax, us); }
    else { dpcCount++; r.dpc++; r.dpcUs += us; r.dpcMax = Math.max(r.dpcMax, us); c.dpc++; c.dpcUs += us; c.dpcMax = Math.max(c.dpcMax, us);
      if (us >= 500) dpcOver500++; if (us >= 1000) dpcOver1000++; }
  }
  requireInput(header, 'Not a tracerpt CSV: header row missing.');
  requireInput(dpcCount + isrCount > 0, 'The trace contains no DPC or ISR events. Record with the dpc,isr,img kernel flags.');
  images.sort((a, b) => (a.base < b.base ? -1 : a.base > b.base ? 1 : 0));
  const find = address => {
    let lo = 0, hi = images.length - 1;
    while (lo <= hi) { const mid = (lo + hi) >> 1, image = images[mid];
      if (address < image.base) hi = mid - 1; else if (address >= image.end) lo = mid + 1; else return image.name; }
    return 'unknown';
  };
  const modules = new Map();
  for (const [routine, r] of routines) {
    const name = find(BigInt(routine));
    const m = modules.get(name) ?? { module: name, dpcCount: 0, dpcMaxUs: 0, dpcUs: 0, isrCount: 0, isrMaxUs: 0, isrUs: 0 };
    m.dpcCount += r.dpc; m.dpcUs += r.dpcUs; m.dpcMaxUs = Math.max(m.dpcMaxUs, r.dpcMax);
    m.isrCount += r.isr; m.isrUs += r.isrUs; m.isrMaxUs = Math.max(m.isrMaxUs, r.isrMax);
    modules.set(name, m);
  }
  const all = [...modules.values()];
  const traceUs = Number(maxClock - minClock) / 10;
  const logical = cpuRows || Math.max(...cpus.keys()) + 1;
  const pct = us => traceUs > 0 ? round(us / traceUs * 100, 4) : null;
  const by = key => all.reduce((best, m) => (m[key] > (best?.[key] ?? 0) ? m : best), null);
  const ranked = [...new Set([by('dpcMaxUs'), by('isrMaxUs'), ...all.sort((a, b) => (b.dpcUs + b.isrUs) - (a.dpcUs + a.isrUs))].filter(Boolean))].slice(0, TOP_MODULES);
  return buildReport('kernel-trace-unattested', null, {
    source: 'kernel-trace', traceSeconds: round(traceUs / 1e6), logicalProcessors: logical, eventsLost,
    dpcCount, isrCount,
    highestDpcUs: round(by('dpcMaxUs')?.dpcMaxUs ?? null, 1), highestDpcModule: by('dpcMaxUs')?.module ?? null,
    highestIsrUs: round(by('isrMaxUs')?.isrMaxUs ?? null, 1), highestIsrModule: by('isrMaxUs')?.module ?? null,
    highestTotalDpcModule: by('dpcUs')?.module ?? null, highestTotalIsrModule: by('isrUs')?.module ?? null,
    dpcTimePercent: traceUs > 0 ? round(all.reduce((s, m) => s + m.dpcUs, 0) / (traceUs * logical) * 100, 4) : null,
    isrTimePercent: traceUs > 0 ? round(all.reduce((s, m) => s + m.isrUs, 0) / (traceUs * logical) * 100, 4) : null,
    dpcOver500UsCount: dpcOver500, dpcOver1000UsCount: dpcOver1000, highestInterruptToProcessUs: null, hardPagefaults: null,
    perCpu: [...cpus.entries()].sort((a, b) => a[0] - b[0]).map(([index, c]) => ({ cpu: index, dpcPercent: pct(c.dpcUs), isrPercent: pct(c.isrUs),
      dpcMaxUs: round(c.dpcMax, 1), isrMaxUs: round(c.isrMax, 1), dpcCount: c.dpc, isrCount: c.isr })),
    topModules: ranked.map(m => ({ module: m.module, dpcCount: m.dpcCount, dpcMaxUs: round(m.dpcMaxUs, 1), dpcTotalMs: round(m.dpcUs / 1000),
      isrCount: m.isrCount, isrMaxUs: round(m.isrMaxUs, 1), isrTotalMs: round(m.isrUs / 1000) }))
  }, invalid);
}

function decode(bytes) {
  if (bytes[0] === 0xff && bytes[1] === 0xfe) return new TextDecoder('utf-16le').decode(bytes.subarray(2));
  if (bytes[0] === 0xfe && bytes[1] === 0xff) return new TextDecoder('utf-16be').decode(bytes.subarray(2));
  try { return new TextDecoder('utf-8', { fatal: true }).decode(bytes).replace(/^﻿/u, ''); }
  catch { return new TextDecoder('windows-1252').decode(bytes); }
}
// LatencyMon follows the Windows locale: "215,40" or "215.40". Counts never carry decimals.
function decimal(raw) {
  if (raw === undefined) return null;
  const v = raw.trim();
  if (/^\d+$/u.test(v)) return Number(v);
  if (/^\d+[.,]\d+$/u.test(v)) return Number(v.replace(',', '.'));
  return null;
}
function count(raw) {
  if (raw === undefined) return null;
  const v = raw.trim().replace(/[.,\s']/gu, '');
  return /^\d+$/u.test(v) ? Number(v) : null;
}
const driver = raw => {
  if (raw === undefined) return null;
  const file = raw.match(/[\w.-]+\.(?:sys|exe|dll)/iu)?.[0];
  return file && text(file, 128) ? file : null;
};

/** Imports a saved LatencyMon report (File > Save report). Unknown lines are ignored; missing values stay null. */
export async function importLatencyMon(file) {
  const bytes = await readBytes(file, MAX_LATENCYMON_BYTES);
  const report = decode(bytes);
  requireInput(/LatencyMon/iu.test(report) && /DPC/u.test(report), 'Not a LatencyMon report. Save it from LatencyMon with File > Save report.');
  // Labels may end with "(µs):", "(Âµs):" or "(%)" depending on encoding; values follow after spaces.
  const value = label => report.match(new RegExp(`^\\s*${label}[^\\n:]*?(?::\\s+|\\)\\s+|\\s{2,})([^\\r\\n]+?)\\s*$`, 'mu'))?.[1];
  const duration = report.match(/analy[sz]ing your system for\s+(\d+):(\d{2}):(\d{2})/iu);
  const seconds = duration ? Number(duration[1]) * 3600 + Number(duration[2]) * 60 + Number(duration[3]) : null;
  const bucketSum = (kind, from) => {
    let total = 0, found = false;
    for (const m of report.matchAll(new RegExp(`^\\s*${kind} count \\(execution time\\s*(?:[<>]=?\\s*)?(\\d+)(?:\\s*-\\s*\\d+)?[^)]*\\)\\s*:?\\s+([\\d.,' ]+?)\\s*$`, 'gmu'))) {
      if (Number(m[1]) >= from) { total += count(m[2]) ?? 0; found = true; }
    }
    return found ? total : null;
  };
  const perCpu = [];
  for (const m of report.matchAll(/^\s*CPU (\d+) DPC count:?\s+([\d.,' ]+?)\s*$/gmu)) {
    const n = Number(m[1]);
    if (n >= MAX_CPUS) continue;
    const cpuValue = label => value(`CPU ${n} ${label}`);
    const dpcTotal = decimal(cpuValue('DPC total execution time')), isrTotal = decimal(cpuValue('ISR total execution time'));
    perCpu.push({ cpu: n, dpcPercent: seconds && dpcTotal !== null ? round(dpcTotal / seconds * 100, 4) : null,
      isrPercent: seconds && isrTotal !== null ? round(isrTotal / seconds * 100, 4) : null,
      dpcMaxUs: decimal(cpuValue('DPC highest execution time')), isrMaxUs: decimal(cpuValue('ISR highest execution time')),
      dpcCount: count(m[2]), isrCount: count(cpuValue('ISR count')) });
  }
  const dpcCount = bucketSum('DPC', 0), isrCount = bucketSum('ISR', 0);
  return buildReport('latencymon-import-unattested', sha256(bytes), {
    source: 'latencymon', traceSeconds: seconds, logicalProcessors: perCpu.length || null, eventsLost: null,
    dpcCount, isrCount,
    highestDpcUs: decimal(value('Highest DPC routine execution time')), highestDpcModule: driver(value('Driver with highest DPC routine execution time')),
    highestIsrUs: decimal(value('Highest ISR routine execution time')), highestIsrModule: driver(value('Driver with highest ISR routine execution time')),
    highestTotalDpcModule: driver(value('Driver with highest DPC total execution time')), highestTotalIsrModule: driver(value('Driver with highest ISR total time')),
    dpcTimePercent: decimal(value('Total time spent in DPCs')), isrTimePercent: decimal(value('Total time spent in ISRs')),
    dpcOver500UsCount: bucketSum('DPC', 500), dpcOver1000UsCount: bucketSum('DPC', 1000),
    highestInterruptToProcessUs: decimal(value('Highest measured interrupt to process latency')),
    hardPagefaults: count(value('Total number of hard pagefaults')), perCpu, topModules: []
  }, 0);
}

function buildReport(provenance, sourceSha256, facts, invalidEvents) {
  const report = seal({ schemaVersion: 1, kind: 'latency', provenance, capturedAt: new Date().toISOString(), sourceSha256, invalidEvents, facts });
  return validateLatency(report);
}

const number = v => v === null || (typeof v === 'number' && Number.isFinite(v) && v >= 0);
const optionalText = v => v === null || text(v, 128);
export function validateLatency(report) {
  verifySeal(report);
  exactKeys(report, ['schemaVersion', 'kind', 'provenance', 'capturedAt', 'sourceSha256', 'invalidEvents', 'facts', 'id']);
  requireInput(report.kind === 'latency' && ['kernel-trace-unattested', 'latencymon-import-unattested'].includes(report.provenance), 'Expected a latency report.');
  requireInput(Number.isFinite(Date.parse(report.capturedAt)) && (report.sourceSha256 === null || /^[0-9a-f]{64}$/u.test(report.sourceSha256)), 'Invalid latency metadata.');
  requireInput(Number.isSafeInteger(report.invalidEvents) && report.invalidEvents >= 0, 'Invalid event count.');
  const f = report.facts;
  exactKeys(f, factKeys);
  requireInput(['kernel-trace', 'latencymon'].includes(f.source), 'Unknown latency source.');
  for (const key of factKeys.filter(k => !['source', 'highestDpcModule', 'highestIsrModule', 'highestTotalDpcModule', 'highestTotalIsrModule', 'perCpu', 'topModules'].includes(k)))
    requireInput(number(f[key]), `Invalid latency value: ${key}.`);
  for (const key of ['highestDpcModule', 'highestIsrModule', 'highestTotalDpcModule', 'highestTotalIsrModule']) requireInput(optionalText(f[key]), 'Invalid driver name.');
  requireInput(Array.isArray(f.perCpu) && f.perCpu.length <= MAX_CPUS && Array.isArray(f.topModules) && f.topModules.length <= TOP_MODULES, 'Invalid latency tables.');
  for (const c of f.perCpu) { exactKeys(c, cpuKeys); requireInput(Number.isInteger(c.cpu) && cpuKeys.slice(1).every(k => number(c[k])), 'Invalid per-CPU latency row.'); }
  for (const m of f.topModules) { exactKeys(m, moduleKeys); requireInput(text(m.module, 128) && moduleKeys.slice(1).every(k => number(m[k])), 'Invalid driver latency row.'); }
  return report;
}
