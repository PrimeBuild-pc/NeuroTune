import { describe, expect, it } from 'vitest';
import { planKindLabel, scriptArtifactFilename, selectActionIdsForProfile } from './plan';
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

  it('labels every item kind and creates inert text filenames', () => {
    expect(planKindLabel('scriptArtifact')).toBe('Unverified script');
    expect(planKindLabel('externalResource')).toBe('Verified resource');
    expect(scriptArtifactFilename('../../unsafe.ps1')).toBe('______unsafe_ps1.txt');
  });
});
