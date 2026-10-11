import { type ComponentProps, type ReactElement, isValidElement } from 'react';
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import { SetupGuide } from './components/SetupGuide';
import { ProviderPage } from './components/ProviderPage';
import { agent } from './agent';
import type { ProviderSettings } from './types';

// Exercise the real callbacks without a DOM or native/provider calls.
const hooks = vi.hoisted(() => ({ values: [] as unknown[], cursor: 0 }));
vi.mock('react', async importOriginal => ({
  ...await importOriginal<typeof import('react')>(),
  useState: (initial: unknown) => {
    const index = hooks.cursor++;
    if (!(index in hooks.values)) hooks.values[index] = typeof initial === 'function' ? initial() : initial === true ? false : initial;
    return [hooks.values[index], (next: unknown) => { hooks.values[index] = typeof next === 'function' ? next(hooks.values[index]) : next; }];
  },
  useEffect: () => {},
  useMemo: (factory: () => unknown) => factory(),
  useRef: (current: unknown) => ({ current }),
  useSyncExternalStore: (_subscribe: unknown, _snapshot: unknown, serverSnapshot: () => unknown) => serverSnapshot(),
}));
vi.mock('./agent', () => ({ agent: vi.fn(), cancelAgent: vi.fn(), newRequestId: () => 'test' }));

function elements(node: unknown): ReactElement<Record<string, unknown>>[] {
  if (Array.isArray(node)) return node.flatMap(elements);
  if (!isValidElement<Record<string, unknown>>(node)) return [];
  return [node, ...elements(node.props.children)];
}
function renderApp() { hooks.cursor = 0; return elements(App()); }
function setup() {
  return renderApp().find(node => node.type === SetupGuide)?.props as ComponentProps<typeof SetupGuide> | undefined;
}
const storage = new Map<string, string>();
const authSettings = (provider: 'chatGpt' | 'openRouter'): ProviderSettings => ({ provider, providerName: provider === 'chatGpt' ? 'ChatGPT plan' : 'OpenRouter', baseUrl: 'https://example.com/v1', protocol: 'openAiCompatible', model: provider === 'chatGpt' ? '' : 'model', requiresApiKey: true, ...(provider === 'chatGpt' ? { chatGptAccountId: 'account' } : {}) });

beforeEach(() => {
  hooks.values = []; hooks.cursor = 0; storage.clear(); vi.mocked(agent).mockReset();
  vi.stubGlobal('localStorage', { getItem: (key: string) => storage.get(key) ?? null, setItem: (key: string, value: string) => storage.set(key, value) });
});
afterEach(() => vi.unstubAllGlobals());

describe('guided setup browser authorization', () => {
  it.each(['chatGpt', 'openRouter'] as const)('uses existing %s auth and model callbacks without finishing optional choices', async kind => {
    let authorize!: (value: unknown) => void;
    let models!: (value: unknown) => void;
    vi.mocked(agent).mockImplementation(command => {
      if (command === 'models') return new Promise(resolve => { models = resolve; });
      if (command === 'save-provider') return Promise.resolve({ saved: true });
      return new Promise(resolve => { authorize = resolve; });
    });
    setup()!.onSignIn(kind);
    expect(agent).toHaveBeenCalledWith(kind === 'chatGpt' ? 'oauth-chatgpt' : 'oauth-openrouter', kind === 'chatGpt' ? { accountId: null } : undefined);
    expect(setup()!.open).toBe(true); expect(setup()!.busy).toContain('browser authorization');
    authorize({ settings: authSettings(kind), hasCredential: true, chatGptAccounts: [] });
    await vi.waitFor(() => expect(agent).toHaveBeenCalledWith('models'));
    expect(setup()!.open).toBe(true); expect(setup()!.busy).toBeTruthy();
    models({ models: ['model'], modelLabels: { model: 'Account model' } });
    await vi.waitFor(() => expect(setup()!.busy).toBe(''));
    expect(setup()!.message).toContain('1 models available');
    expect(storage.get('neurotune.setupReviewed.v1')).toBeUndefined();
    expect(storage.get('neurotune.optionalTelemetryConsent')).toBeUndefined();
    expect(storage.get('neurotune.firmwareReadConsent')).toBeUndefined();
    const allowed = new Set(['oauth-chatgpt', 'oauth-openrouter', 'models', 'save-provider']);
    expect(vi.mocked(agent).mock.calls.every(([command]) => allowed.has(command))).toBe(true);
    setup()!.onFinish({ telemetry: true, firmware: false });
    expect(setup()).toBeUndefined(); expect(storage.get('neurotune.setupReviewed.v1')).toBe('true');
    expect(storage.get('neurotune.optionalTelemetryConsent')).toBe('true');
    const provider = renderApp().find(node => node.type === ProviderPage)?.props as ComponentProps<typeof ProviderPage> | undefined;
    expect(provider).toBeUndefined(); // A configured provider stays on Overview.
  });

  it.each(['authorization', 'models', 'plan'] as const)('retains setup and permissions when %s fails', async failure => {
    storage.set('neurotune.optionalTelemetryConsent', 'true');
    vi.mocked(agent).mockImplementation(command => {
      if (failure === 'authorization' || command === 'models') return Promise.reject(Error('Authorization or model discovery failed'));
      return Promise.resolve({ settings: authSettings('chatGpt'), hasCredential: failure !== 'plan' });
    });
    setup()!.onSignIn('chatGpt');
    await vi.waitFor(() => expect(setup()!.busy).toBe(''));
    expect(setup()!.open).toBe(true); expect(setup()!.message).toBeTruthy();
    expect(setup()!.error).toBe(failure !== 'plan');
    expect(storage.get('neurotune.setupReviewed.v1')).toBeUndefined();
    expect(storage.get('neurotune.optionalTelemetryConsent')).toBe('true');
    expect(storage.get('neurotune.firmwareReadConsent')).toBeUndefined();
    if (failure === 'plan') expect(agent).not.toHaveBeenCalledWith('models');
  });

  it.each(['identity-only', 'catalog-error'] as const)('reuses the ChatGPT registration on %s sign-in retry', async failure => {
    storage.set('neurotune.optionalTelemetryConsent', 'true');
    vi.mocked(agent).mockImplementation(command => {
      if (command === 'oauth-chatgpt') return Promise.resolve({ settings: authSettings('chatGpt'), hasCredential: failure !== 'identity-only' });
      if (command === 'models') return Promise.reject(Error('Catalog unavailable'));
      throw Error('Unexpected command: ' + command);
    });
    setup()!.onSignIn('chatGpt');
    await vi.waitFor(() => expect(setup()!.busy).toBe(''));
    expect(setup()!.connectedProvider).toBe(failure === 'identity-only' ? undefined : 'ChatGPT plan');
    setup()!.onSignIn('chatGpt');
    expect(agent).toHaveBeenLastCalledWith('oauth-chatgpt', { accountId: 'account' });
    // A second click while authorization is pending cannot start another login.
    setup()!.onSignIn('chatGpt');
    expect(vi.mocked(agent).mock.calls.filter(([command]) => command === 'oauth-chatgpt')).toHaveLength(2);
    await vi.waitFor(() => expect(setup()!.busy).toBe(''));
    expect(storage.get('neurotune.optionalTelemetryConsent')).toBe('true');
    expect(storage.get('neurotune.firmwareReadConsent')).toBeUndefined();
    expect(storage.get('neurotune.setupReviewed.v1')).toBeUndefined();
  });

  it.each(['chatGpt', 'openRouter'] as const)('retries %s model discovery from setup without a second OAuth flow', async kind => {
    let available = false;
    vi.mocked(agent).mockImplementation(command => {
      if (command.startsWith('oauth-')) return Promise.resolve({ settings: authSettings(kind), hasCredential: true });
      if (command === 'save-provider') return Promise.resolve({ saved: true, hasCredential: true });
      if (command === 'models') return available ? Promise.resolve({ models: ['model'] }) : Promise.reject(Error('Catalog unavailable'));
      throw Error('Unexpected command: ' + command);
    });
    setup()!.onSignIn(kind);
    await vi.waitFor(() => expect(setup()!.busy).toBe(''));
    expect(setup()!.error).toBe(true);
    expect(setup()!.connectedProvider).toBe(authSettings(kind).providerName);
    available = true;
    setup()!.onLoadModels();
    expect(agent).toHaveBeenLastCalledWith('save-provider', { settings: authSettings(kind), apiKey: null });
    expect(setup()!.busy).toBeTruthy();
    setup()!.onLoadModels(); // Busy guard also covers model retry.
    await vi.waitFor(() => expect(setup()!.busy).toBe(''));
    expect(setup()!.message).toContain('1 models available');
    expect(vi.mocked(agent).mock.calls.filter(([command]) => command.startsWith('oauth-'))).toHaveLength(1);
    expect(vi.mocked(agent).mock.calls.filter(([command]) => command === 'models')).toHaveLength(2);
    expect(setup()!.open).toBe(true);
    expect(storage.get('neurotune.optionalTelemetryConsent')).toBeUndefined();
    expect(storage.get('neurotune.firmwareReadConsent')).toBeUndefined();
    expect(storage.get('neurotune.setupReviewed.v1')).toBeUndefined();
  });

  it('Other opens provider settings without marking setup reviewed, changing consents or calling the agent', async () => {
    setup()!.onOther();
    expect(setup()).toBeUndefined();
    const provider = renderApp().find(node => node.type === ProviderPage)!.props as ComponentProps<typeof ProviderPage>;
    expect(provider.provider.provider).toBe('openRouter');
    expect(agent).not.toHaveBeenCalled(); expect(storage.size).toBe(0);
    // The full provider page still invokes the same browser-auth implementation.
    await provider.onBrowserSignIn();
    expect(agent).toHaveBeenCalledWith('oauth-openrouter', undefined);
  });

  it('keeps quick actions explicit and blocks Escape while busy', () => {
    const onSignIn = vi.fn(), onLoadModels = vi.fn(), onOther = vi.fn(), onFinish = vi.fn(), onClose = vi.fn();
    const props = { open: true, initial: { telemetry: false, firmware: false }, connectedProvider: 'ChatGPT plan', onSignIn, onLoadModels, onOther, onFinish, onClose };
    const render = (busy = '') => { hooks.cursor = 0; return elements(SetupGuide({ ...props, busy })); };
    const tree = render();
    const buttons = tree.filter(node => node.type === 'button');
    (buttons[0].props.onClick as () => void)(); (buttons[1].props.onClick as () => void)(); (buttons[2].props.onClick as () => void)();
    expect(onSignIn.mock.calls).toEqual([['chatGpt'], ['openRouter']]); expect(onOther).toHaveBeenCalledOnce(); expect(onFinish).not.toHaveBeenCalled();
    expect(tree.some(node => node.props.children === 'Signed in with ChatGPT plan')).toBe(true);
    const load = buttons.find(node => node.props.children === 'Load available models')!;
    (load.props.onClick as () => void)(); expect(onLoadModels).toHaveBeenCalledOnce();
    const blocked = render('Waiting for browser authorization…');
    expect(blocked.find(node => node.type === 'fieldset')!.props.disabled).toBe(true);
    const preventDefault = vi.fn();
    (blocked[0].props.onCancel as (event: unknown) => void)({ preventDefault });
    expect(preventDefault).toHaveBeenCalledOnce(); expect(onClose).not.toHaveBeenCalled();
    (render()[0].props.onCancel as (event: unknown) => void)({ preventDefault });
    expect(onClose).toHaveBeenCalledOnce();
    for (let i = 0; i < 3; i++) {
      const next = render().find(node => node.type === 'button' && node.props.className === 'primary' && node.props.children === 'Next')!;
      (next.props.onClick as () => void)();
    }
    expect(render().find(node => typeof node.type === 'function' && node.props.title)?.props.title).toBe('Configure an optional cloud or local System One assistant?');
  });
});
