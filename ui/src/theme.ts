import type { ThemePreference } from './types';

const storageKey = 'neurotune.theme';

export function resolveTheme(preference: ThemePreference, systemDark: boolean): 'light' | 'dark' {
  return preference === 'system' ? (systemDark ? 'dark' : 'light') : preference;
}

export function loadThemePreference(): ThemePreference {
  const value = localStorage.getItem(storageKey);
  return value === 'light' || value === 'dark' ? value : 'system';
}

export function applyTheme(preference: ThemePreference): () => void {
  const media = window.matchMedia('(prefers-color-scheme: dark)');
  const update = () => {
    document.documentElement.dataset.theme = resolveTheme(preference, media.matches);
    document.documentElement.style.colorScheme = resolveTheme(preference, media.matches);
  };
  localStorage.setItem(storageKey, preference);
  update();
  media.addEventListener('change', update);
  return () => media.removeEventListener('change', update);
}
