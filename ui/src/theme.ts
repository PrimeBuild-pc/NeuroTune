import { useSyncExternalStore } from 'react';
import type { ThemePreference } from './types';

const systemThemeQuery = '(prefers-color-scheme: dark)';
function subscribeSystemTheme(change: () => void) {
  const media = window.matchMedia(systemThemeQuery);
  media.addEventListener('change', change);
  return () => media.removeEventListener('change', change);
}
export function useResolvedTheme(preference: ThemePreference): 'light' | 'dark' {
  const systemDark = useSyncExternalStore(subscribeSystemTheme, () => window.matchMedia(systemThemeQuery).matches, () => false);
  return resolveTheme(preference, systemDark);
}

const storageKey = 'neurotune.theme';

export function resolveTheme(preference: ThemePreference, systemDark: boolean): 'light' | 'dark' {
  return preference === 'system' ? (systemDark ? 'dark' : 'light') : preference;
}

export function loadThemePreference(): ThemePreference {
  const value = localStorage.getItem(storageKey);
  return value === 'light' || value === 'dark' ? value : 'system';
}

export function applyTheme(preference: ThemePreference): () => void {
  const media = window.matchMedia(systemThemeQuery);
  const update = () => {
    document.documentElement.dataset.theme = resolveTheme(preference, media.matches);
    document.documentElement.style.colorScheme = resolveTheme(preference, media.matches);
  };
  localStorage.setItem(storageKey, preference);
  update();
  media.addEventListener('change', update);
  return () => media.removeEventListener('change', update);
}
