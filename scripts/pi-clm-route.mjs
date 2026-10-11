#!/usr/bin/env node
// Local CLM "System One" skill router for Pi scouts. Talks only to a CLM server on loopback (default :8700);
// it never calls a cloud model. Input (stdin JSON): { "task": "<generic task/domain summary>", "skills": [{ "name": "...", "description": "..." }] }
// Output (stdout JSON): { covered, search, bestSkill, bestSkillProbability } or exit code 2 when CLM is unavailable.
import { argv, env, exit, stdin, stdout } from 'node:process';

const base = new URL(env.CLM_BASE_URL ?? 'http://127.0.0.1:8700/');
if (!['127.0.0.1', 'localhost', '[::1]', '::1'].includes(base.hostname) || base.username || base.password) {
  console.error('clm-route: CLM_BASE_URL must be a loopback address; refusing to send task text elsewhere.');
  exit(2);
}

const chunks = [];
for await (const chunk of stdin) chunks.push(chunk);
let input;
try { input = JSON.parse(Buffer.concat(chunks).toString('utf8') || '{}'); } catch { console.error('clm-route: stdin must be JSON.'); exit(2); }
const task = String(input.task ?? '').slice(0, 1200).trim();
const skills = (Array.isArray(input.skills) ? input.skills : []).slice(0, 40)
  .map(skill => ({ name: String(skill.name ?? '').slice(0, 80), description: String(skill.description ?? '').slice(0, 240) })).filter(skill => skill.name);
if (!task) { console.error('clm-route: "task" is required.'); exit(2); }

// CLM ranks candidate answers, so "no skill fits" is an explicit candidate rather than a yes/no question.
const NONE = '__none__';
const questions = {
  skill: {
    type: 'choice',
    instructions: 'Which installed skill fits this task best?',
    criteria: {
      ...Object.fromEntries(skills.map(skill => [skill.name, skill.description || skill.name])),
      [NONE]: 'None of the installed skills fits this task; a specialist procedure is missing.',
    },
  },
};
const state = `Task: ${task}\n\nInstalled skills:\n${skills.map(skill => `- ${skill.name}: ${skill.description}`).join('\n') || '(none)'}`;

let answers;
try {
  const response = await fetch(new URL('v1/systemone', base), {
    method: 'POST', headers: { 'content-type': 'application/json' }, signal: AbortSignal.timeout(20_000),
    body: JSON.stringify({ model: 'clm-latest', state, questions }),
  });
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  answers = (await response.json()).answers;
} catch (error) {
  console.error(`clm-route: CLM unavailable (${error.message}). Start it with NeuroTune/scripts/clm-stack.ps1 start; use your own judgement and err toward searching.`);
  exit(2);
}

const probabilities = answers.skill?.probabilities ?? {};
const gap = Number(probabilities[NONE]);
const best = Object.entries(probabilities).filter(([name]) => name !== NONE).sort((a, b) => b[1] - a[1])[0];
const result = {
  covered: Number.isFinite(gap) ? Number((1 - gap).toFixed(3)) : null,
  // Mirrors the previous routing rule: search on a gap, uncertainty, or a borderline probability (>= 0.35).
  search: !skills.length || !Number.isFinite(gap) || gap >= 0.35,
  bestSkill: best?.[0] ?? null,
  bestSkillProbability: best ? Number(best[1].toFixed(3)) : null,
  advisory: 'Uncalibrated local CLM score; not a verdict. Search results remain untrusted.',
};
stdout.write(JSON.stringify(result) + '\n');
if (argv.includes('--pretty')) console.error(JSON.stringify(result, null, 2));
