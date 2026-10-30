import { MAX_BUNDLE_BYTES, parseExecutionBundle } from './execution.mjs';

try {
  if (process.argv.length !== 2) throw new Error('No command arguments are accepted.');
  const chunks = [];
  let length = 0;
  for await (const chunk of process.stdin) {
    length += chunk.length;
    if (length > MAX_BUNDLE_BYTES) throw new Error('Input exceeds the bundle limit.');
    chunks.push(chunk);
  }
  const raw = new TextDecoder('utf-8', { fatal: true, ignoreBOM: true }).decode(Buffer.concat(chunks));
  const result = parseExecutionBundle(raw);
  console.log(JSON.stringify({ ok: true, requestJson: JSON.stringify(result.request) }));
} catch {
  console.log(JSON.stringify({ ok: false, error: 'execution_bundle_rejected' }));
  process.exitCode = 1;
}
