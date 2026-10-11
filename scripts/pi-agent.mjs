import { Agent } from '@earendil-works/pi-agent-core';
import { AssistantMessageEventStream } from '@earendil-works/pi-ai/utils/event-stream';
import { createInterface } from 'node:readline';
import { pathToFileURL } from 'node:url';

// NeuroTune owns transport, evidence and validation; Pi owns scheduling and tool execution.
export async function runInvestigation(maxTurns, rpc) {
  if (!Number.isInteger(maxTurns) || maxTurns < 2 || maxTurns > 32) throw new Error('Invalid investigation budget.');
  let turns = 0;
  let transportFailed = false;
  const tools = ['request_evidence', 'investigate', 'conclude'].map(name => ({
    name, label: name, description: 'Submit a planner envelope to NeuroTune for local validation.',
    parameters: { type: 'object', properties: { response: { type: 'string' } }, required: ['response'], additionalProperties: false },
    execute: async (_id, { response }) => {
      try {
        const result = await rpc({ type: 'handle', turn: turns, response });
        if (typeof result.finished !== 'boolean') throw new Error('Invalid host result.');
        return { content: [{ type: 'text', text: result.finished ? 'Investigation finished.' : 'Validated observations are available in the next host prompt.' }], details: {}, terminate: result.finished };
      } catch {
        transportFailed = true;
        throw new Error('NeuroTune Pi transport failed; no alternate agent was used.');
      }
    },
  }));
  const agent = new Agent({
    initialState: {
      systemPrompt: 'Use only NeuroTune registered read-only evidence tools. No execution or approval authority.',
      model: { id: 'neurotune', name: 'Selected NeuroTune provider', api: 'openai-completions', provider: 'neurotune', baseUrl: '', reasoning: false, input: ['text'], cost: { input: 0, output: 0, cacheRead: 0, cacheWrite: 0 }, contextWindow: 200000, maxTokens: 32768 },
      tools,
    },
    toolExecution: 'sequential',
    finishTurn: () => transportFailed || turns >= maxTurns ? { action: 'end' } : undefined,
    streamFn: () => {
      const stream = new AssistantMessageEventStream();
      const message = { role: 'assistant', content: [], api: 'openai-completions', provider: 'neurotune', model: 'neurotune', usage: { input: 0, output: 0, cacheRead: 0, cacheWrite: 0, totalTokens: 0, cost: { input: 0, output: 0, cacheRead: 0, cacheWrite: 0, total: 0 } }, stopReason: 'toolUse', timestamp: Date.now() };
      void (async () => {
        try {
          if (turns >= maxTurns) throw new Error('Investigation turn limit exceeded.');
          const { response } = await rpc({ type: 'generate', turn: ++turns });
          let kind;
          try { kind = JSON.parse(response).kind; } catch { /* The host rejects malformed envelopes without retry. */ }
          const name = kind === 'requestEvidence' ? 'request_evidence' : kind === 'requestInvestigation' ? 'investigate' : 'conclude';
          message.content = [{ type: 'toolCall', id: `turn-${turns}`, name, arguments: { response } }];
          stream.push({ type: 'done', reason: 'toolUse', message });
        } catch {
          message.stopReason = 'error'; message.errorMessage = 'NeuroTune Pi transport failed; no alternate agent was used.';
          stream.push({ type: 'error', reason: 'error', error: message });
        } finally { stream.end(); }
      })();
      return stream;
    },
  });
  await agent.prompt('Investigate the selected NeuroTune goals using the host contract.');
  if (transportFailed) throw new Error('NeuroTune Pi transport failed; no alternate agent was used.');
  if (agent.state.errorMessage) throw new Error(agent.state.errorMessage);
  return turns;
}

export async function serve(input = process.stdin, output = process.stdout) {
  const lines = createInterface({ input, crlfDelay: Infinity })[Symbol.asyncIterator]();
  async function receive() {
    const { value, done } = await lines.next();
    if (done || value.length > 2_000_000) throw new Error('Invalid Pi host frame.');
    return JSON.parse(value);
  }
  const { maxTurns } = await receive();
  let id = 0;
  await runInvestigation(maxTurns, async request => {
    const requestId = ++id;
    output.write(JSON.stringify({ ...request, id: requestId }) + '\n');
    const response = await receive();
    if (response.id !== requestId) throw new Error('Invalid Pi host correlation.');
    return response;
  });
  output.write(JSON.stringify({ type: 'done' }) + '\n');
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  serve().catch(() => { process.stderr.write('NeuroTune Pi bridge failed.\n'); process.exitCode = 1; });
}
