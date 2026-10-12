import { describe, expect, it } from 'vitest';
import { analysisPresetsFor, planKindLabel, scenarioProfiles, scriptArtifactFilename, selectActionIdsForProfile } from './plan';
import { messages } from './i18n';
import type { OptimizationAction, Recommendation } from './types';

const actions: OptimizationAction[] = (['low', 'medium', 'high'] as const).map(risk => ({
  id: `action.${risk}`,
  name: risk,
  description: risk,
  category: 'test',
  risk,
  requiresRestart: false,
  availability: { canApply: true, status: 'available', currentValue: 'test' },
}));

const recommendations = actions.map(action => ({
  id: `plan.${action.risk}`,
  kind: 'executableAction',
  title: action.name,
  actionId: action.id,
  resourceId: '',
  updateId: '',
  evidenceIds: ['test.evidence'],
  reason: 'test',
  risk: action.risk,
  expectedImpact: '',
  tradeoffs: [],
  prerequisites: [],
  requiresRestart: false,
  sourceReferences: [],
  scriptLanguage: '',
  script: '',
  reviewWarnings: [],
})) satisfies Recommendation[];

describe('dynamic plan helpers', () => {
  it('preselects only compatible recommended actions for each risk profile', () => {
    expect(selectActionIdsForProfile(actions, recommendations, 'safe')).toEqual(['action.low']);
    expect(selectActionIdsForProfile(actions, recommendations, 'balanced')).toEqual(['action.low', 'action.medium']);
    expect(selectActionIdsForProfile(actions, recommendations, 'aggressive')).toEqual(['action.low', 'action.medium', 'action.high']);
    expect(selectActionIdsForProfile(actions, recommendations, 'none')).toEqual([]);
  });

  it('keeps unavailable and non-recommended capabilities out of preselection', () => {
    const unavailable = actions.map(action => action.id === 'action.low'
      ? { ...action, availability: { ...action.availability, canApply: false } }
      : action);
    expect(selectActionIdsForProfile(unavailable, recommendations.slice(0, 2), 'aggressive')).toEqual(['action.medium']);
  });

  it('excludes manual guidance even when it carries an artificial valid action reference', () => {
    const manual = { ...recommendations[0], kind: 'manualGuidance' as const, actionId: 'action.low' };
    for (const profile of ['safe', 'balanced', 'aggressive'] as const)
      expect(selectActionIdsForProfile(actions, [manual], profile)).toEqual([]);
  });

  it('labels every item kind and creates inert text filenames', () => {
    expect(planKindLabel('executableAction')).toBe('NeuroTune action');
    expect(planKindLabel('manualGuidance')).toBe('Manual guidance');
    expect(planKindLabel('scriptArtifact')).toBe('Unverified script');
    expect(planKindLabel('externalResource')).toBe('Verified resource');
    expect(planKindLabel('updateNotice')).toBe('Official update notice');
    expect(scriptArtifactFilename('../../unsafe.ps1')).toBe('______unsafe_ps1.txt');
  });
  it('scenario profiles use current objectives, fit the notes field and are translated', () => {
    const current = analysisPresetsFor('balanced').map(item => item.id);
    expect(scenarioProfiles.map(item => item.id)).toEqual(['competitive', 'streaming', 'quiet', 'privacy', 'compromise', 'ports']);
    for (const item of scenarioProfiles) {
      expect(current).toContain(item.priority);
      expect(item.notes.length).toBeLessThanOrEqual(1000);
      for (const text of [item.label, item.detail, item.notes]) expect(messages[text], text).toHaveLength(5);
    }
  });
});
