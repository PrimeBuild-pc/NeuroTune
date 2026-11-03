import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, writeFile, rm, readFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { analyzeKernelTrace, importLatencyMon, splitCsvLine, validateLatency } from '../skills/neurotune/scripts/latency.mjs';
import { evidence } from '../skills/neurotune/scripts/evidence.mjs';

const cli = fileURLToPath(new URL('../skills/neurotune/scripts/cli.mjs', import.meta.url));
const header = '  Event Name,       Type,     Event ID,    Version,    Channel,      Level,     Opcode,       Task,            Keyword,        PID,        TID,     Processor Number,  Instance ID,   Parent Instance ID,                              Activity ID,                      Related Activity ID,           Clock-Time, Kernel(ms),   User(ms), User Data';
const pre = (name, type, cpu, clock) => `${name}, ${type}, 0, 2, 0, 0, 0, 0, 0x0, 0xFFFFFFFF, 0xFFFFFFFF, ${cpu}, , , {00000000-0000-0000-0000-000000000000}, , ${clock}, 0, 0`;
const T0 = 134381968769700000n;
// Synthetic trace: two drivers, two CPUs; durations are clock minus InitialTime in 100 ns units.
function trace() {
  const rows = [header,
    `${pre('SystemConfig', 'Processors', 0, T0)}, 0, 1, 3400, "Synthetic CPU", "x", "y"`,
    `${pre('SystemConfig', 'Processors', 0, T0)}, 0, 1, 3400, "Synthetic CPU", "x", "y"`,
    `${pre('Image', 'DCStart', 0, T0)}, 0xFFFFF80000000000, 0x100000, 0, 1, 2, 0, 0, 0x0, 0, 0, 0, 0, "\\SystemRoot\\system32\\drivers\\netfast.sys"`,
    `${pre('Image', 'DCStart', 0, T0)}, 0xFFFFF80000200000, 0x100000, 0, 1, 2, 0, 0, 0x0, 0, 0, 0, 0, "\\SystemRoot\\System32\\DriverStore\\gpu,kernel.sys"`,
    `${pre('Image', 'DCStart', 0, T0)}, 0x00007FF600000000, 0x100000, 1234, 1, 2, 0, 0, 0x0, 0, 0, 0, 0, "C:\\user-mode.exe"`];
  // netfast.sys: DPC 1200 us on CPU 0, DPC 100 us on CPU 1; gpu: ISR 30 us and DPC 600 us on CPU 1.
  const ev = (type, cpu, start, us, routine) => `${pre('PerfInfo', type, cpu, T0 + start + BigInt(us * 10))}, ${T0 + start}, ${routine}${type.startsWith('ISR') ? ', 1, 51, 0' : ''}`;
  rows.push(ev('DPC', 0, 10_000n, 1200, '0xFFFFF80000000100'), ev('DPC', 1, 20_000n, 100, '0xFFFFF80000000200'),
    ev('ISR', 1, 30_000n, 30, '0xFFFFF80000200010'), ev('DPC', 1, 40_000n, 600, '0xFFFFF80000200020'),
    ev('TimerDPC', 0, 10_000_000n, 5, '0xFFFFF80000900000'));
  return rows.join('\r\n') + '\r\n';
}

async function withFile(name, content, fn) {
  const dir = await mkdtemp(path.join(tmpdir(), 'neurotune-test-'));
  try { const file = path.join(dir, name); await writeFile(file, content); return await fn(file, dir); }
  finally { await rm(dir, { recursive: true, force: true }); }
}

test('csv splitting keeps quoted commas', () => {
  assert.deepEqual(splitCsvLine(' a, "b,c" ,d'), ['a', 'b,c', 'd']);
});

test('kernel trace attributes DPC/ISR time to drivers and CPUs', async () => {
  await withFile('k.csv', trace(), async file => {
    const report = await analyzeKernelTrace(file, { eventsLost: 0 });
    const f = report.facts;
    assert.equal(f.source, 'kernel-trace');
    assert.equal(f.logicalProcessors, 2);
    assert.equal(f.dpcCount, 4); assert.equal(f.isrCount, 1);
    assert.equal(f.highestDpcUs, 1200); assert.equal(f.highestDpcModule, 'netfast.sys');
    assert.equal(f.highestIsrUs, 30); assert.equal(f.highestIsrModule, 'gpu,kernel.sys');
    assert.equal(f.dpcOver500UsCount, 2); assert.equal(f.dpcOver1000UsCount, 1);
    assert.ok(f.topModules.some(m => m.module === 'unknown'), 'routine outside every kernel image stays unknown');
    assert.equal(f.topModules.find(m => m.module === 'netfast.sys').dpcTotalMs, 1.3);
    assert.deepEqual(f.perCpu.map(c => c.cpu), [0, 1]);
    assert.ok(f.dpcTimePercent > 0 && f.perCpu[0].dpcPercent > f.perCpu[1].dpcPercent);
    assert.equal(validateLatency(report), report);
  });
});

test('kernel trace import rejects files without the expected header or events', async () => {
  await withFile('x.csv', 'not,a,trace\n', async file => assert.rejects(analyzeKernelTrace(file), /header/));
  await withFile('y.csv', header + '\n', async file => assert.rejects(analyzeKernelTrace(file), /no DPC or ISR/));
});

const latencyMon = `CONCLUSION
LatencyMon has been analyzing your system for  0:01:40  (h:mm:ss) on all processors.

Highest measured interrupt to process latency (µs):   215,40
Highest ISR routine execution time (µs):              91,050
Driver with highest ISR routine execution time:       dxgkrnl.sys - DirectX Graphics Kernel, Microsoft Corporation
Driver with highest ISR total time:                   dxgkrnl.sys - DirectX Graphics Kernel, Microsoft Corporation
Total time spent in ISRs (%)                          0,090807
ISR count (execution time <250 µs):                   126669
ISR count (execution time 250-500 µs):                0
Highest DPC routine execution time (µs):              1454,710
Driver with highest DPC routine execution time:       ndis.sys - Network Driver Interface Specification (NDIS), Microsoft Corporation
Driver with highest DPC total execution time:         nvlddmkm.sys - NVIDIA Windows Kernel Mode Driver, NVIDIA Corporation
Total time spent in DPCs (%)                          0,173064
DPC count (execution time <250 µs):                   687440
DPC count (execution time 250-500 µs):                12
DPC count (execution time 500-10000 µs):              5
DPC count (execution time 1000-2000 µs):              2
DPC count (execution time 2000-4000 µs):              0
DPC count (execution time >=4000 µs):                 1
Total number of hard pagefaults                       1069
CPU 0 ISR highest execution time (µs):                91,050
CPU 0 ISR total execution time (s):                   0,467128
CPU 0 ISR count:                                      125939
CPU 0 DPC highest execution time (µs):                1454,710
CPU 0 DPC total execution time (s):                   0,861047
CPU 0 DPC count:                                      623049
CPU 1 DPC highest execution time (µs):                40,5
CPU 1 DPC total execution time (s):                   0,1
CPU 1 DPC count:                                      64391
`;

test('LatencyMon reports import from UTF-16 with locale decimals', async () => {
  const bytes = Buffer.concat([Buffer.from([0xff, 0xfe]), Buffer.from(latencyMon, 'utf16le')]);
  await withFile('lm.txt', bytes, async file => {
    const report = await importLatencyMon(file);
    const f = report.facts;
    assert.equal(report.provenance, 'latencymon-import-unattested');
    assert.equal(f.traceSeconds, 100);
    assert.equal(f.highestDpcUs, 1454.71); assert.equal(f.highestDpcModule, 'ndis.sys');
    assert.equal(f.highestIsrModule, 'dxgkrnl.sys'); assert.equal(f.highestTotalDpcModule, 'nvlddmkm.sys');
    assert.equal(f.highestInterruptToProcessUs, 215.4);
    assert.equal(f.dpcTimePercent, 0.173064);
    assert.equal(f.dpcCount, 687440 + 12 + 5 + 2 + 0 + 1); assert.equal(f.dpcOver500UsCount, 8); assert.equal(f.dpcOver1000UsCount, 3);
    assert.equal(f.hardPagefaults, 1069);
    assert.equal(f.perCpu.length, 2); assert.equal(f.perCpu[0].dpcPercent, 0.861); assert.equal(f.perCpu[1].isrMaxUs, null);
    const facts = evidence(report).facts;
    assert.ok(facts.some(x => x.id === `${report.id}:highestDpcModule` && x.value === 'ndis.sys'));
  });
});

test('LatencyMon import rejects other text and Windows-1252 still decodes', async () => {
  await withFile('n.txt', 'hello', async file => assert.rejects(importLatencyMon(file), /Not a LatencyMon report/));
  await withFile('w.txt', Buffer.from(latencyMon.replaceAll('µ', '\u00b5'), 'latin1'), async file => {
    assert.equal((await importLatencyMon(file)).facts.highestIsrUs, 91.05);
  });
});

test('tampered latency reports fail validation', async () => {
  await withFile('k.csv', trace(), async file => {
    const report = await analyzeKernelTrace(file);
    assert.throws(() => validateLatency({ ...report, facts: { ...report.facts, highestDpcUs: 1 } }), /integrity/);
  });
});

test('latencymon and measure-latency CLI commands are bounded and never overwrite', async () => {
  await withFile('lm.txt', latencyMon, async (file, dir) => {
    const out = path.join(dir, 'latency.json');
    const first = spawnSync(process.execPath, [cli, 'latencymon', '--input', file, '--out', out], { encoding: 'utf8' });
    assert.equal(first.status, 0, first.stdout);
    assert.equal(JSON.parse(first.stdout).data.kind, 'latency-receipt');
    assert.equal(JSON.parse(await readFile(out, 'utf8')).kind, 'latency');
    const again = spawnSync(process.execPath, [cli, 'latencymon', '--input', file, '--out', out], { encoding: 'utf8' });
    assert.equal(JSON.parse(again.stdout).error.code, 'output_exists');
    const bad = spawnSync(process.execPath, [cli, 'measure-latency', '--seconds', '9999', '--out', path.join(dir, 'x.json')], { encoding: 'utf8' });
    assert.equal(bad.status, 1);
    assert.equal(JSON.parse(bad.stdout).ok, false);
  });
});
