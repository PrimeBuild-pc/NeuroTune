import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { PassThrough } from 'node:stream';

const ui = fileURLToPath(new URL('../ui/', import.meta.url));
const { build } = createRequire(new URL('../ui/package.json', import.meta.url))('esbuild');
const bundle = await build({ entryPoints: [fileURLToPath(new URL('./pi-agent.mjs', import.meta.url))], nodePaths: [ui + '/node_modules'], bundle: true, platform: 'node', format: 'esm', write: false, logLevel: 'silent' });
const { runInvestigation, serve } = await import('data:text/javascript;base64,' + Buffer.from(bundle.outputFiles[0].contents).toString('base64'));

test('Pi executes evidence/follow-up tools and stops on a host-validated diagnosis', async () => {
  const events = [];
  const responses = [
    JSON.stringify({ kind: 'requestEvidence', evidenceIds: ['system:cpu'] }),
    JSON.stringify({ kind: 'requestInvestigation', toolId: 'memory-pressure', question: 'Check pressure' }),
    JSON.stringify({ kind: 'diagnosis', diagnosis: { summary: 'No change' } }),
  ];
  const turns = await runInvestigation(6, async request => {
    events.push(request);
    if (request.type === 'generate') return { response: responses[request.turn - 1] };
    assert.equal(request.response, responses[request.turn - 1]);
    return { finished: request.turn === 3 };
  });
  assert.equal(turns, 3);
  assert.deepEqual(events.map(event => event.type), ['generate', 'handle', 'generate', 'handle', 'generate', 'handle']);
});

test('Pi hard turn limit counts model requests, not just completed diagnoses', async () => {
  let generations = 0;
  await runInvestigation(2, async request => request.type === 'generate'
    ? (generations++, { response: '{"kind":"requestEvidence","evidenceIds":["system:cpu"]}' })
    : { finished: false });
  assert.equal(generations, 2);
});

test('malformed/refused output is still submitted to local validation, without retry', async () => {
  const events = [];
  await runInvestigation(2, async request => {
    events.push(request);
    return request.type === 'generate' ? { response: 'not JSON' } : { finished: true };
  });
  assert.equal(events.length, 2);
  assert.equal(events[1].response, 'not JSON');
});

test('transport errors fail closed without retries or another provider', async () => {
  let calls = 0;
  await assert.rejects(runInvestigation(2, async () => { calls++; throw new Error('secret diagnostic'); }), /transport failed/);
  assert.equal(calls, 1);
  await assert.rejects(runInvestigation(33, async () => {}), /budget/);
});

test('handle transport failures are fatal even when Pi treats tools as error results', async () => {
  let generations = 0;
  await assert.rejects(runInvestigation(4, async request => {
    if (request.type === 'generate') { generations++; return { response: '{"kind":"diagnosis"}' }; }
    throw new Error('broken pipe');
  }), /transport failed/);
  assert.equal(generations, 1);
});

test('serve correlates protocol frames and closes only after validated completion', async () => {
  const input = new PassThrough(), output = new PassThrough();
  const frames = [];
  output.on('data', bytes => {
    for (const line of bytes.toString().trim().split('\n')) {
      const frame = JSON.parse(line); frames.push(frame);
      if (frame.type === 'generate') input.write(JSON.stringify({ id: frame.id, response: '{"kind":"diagnosis"}' }) + '\n');
      if (frame.type === 'handle') input.write(JSON.stringify({ id: frame.id, finished: true }) + '\n');
    }
  });
  input.write('{"maxTurns":2}\n');
  await serve(input, output);
  assert.deepEqual(frames.map(frame => frame.type), ['generate', 'handle', 'done']);
  input.end(); output.end();
});

test('serve rejects wrong handle correlation without another generation or done frame', async () => {
  const input = new PassThrough(), output = new PassThrough();
  const frames = [];
  output.on('data', bytes => {
    for (const line of bytes.toString().trim().split('\n')) {
      const frame = JSON.parse(line); frames.push(frame);
      input.write(JSON.stringify(frame.type === 'generate'
        ? { id: frame.id, response: '{"kind":"diagnosis"}' }
        : { id: 999, finished: false }) + '\n');
    }
  });
  input.write('{"maxTurns":2}\n');
  await assert.rejects(serve(input, output), /transport failed/);
  assert.deepEqual(frames.map(frame => frame.type), ['generate', 'handle']);
  input.end(); output.end();
});
