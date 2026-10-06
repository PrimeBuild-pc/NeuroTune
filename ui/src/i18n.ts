import { useSyncExternalStore } from 'react';
import { appMessages } from './locales/app';
import { diagnosisMessages } from './locales/diagnosis';
import { reportMessages } from './locales/reports';
import { toolMessages } from './locales/tools';

export const languages = [
  { id: 'en', label: 'English' },
  { id: 'zh-CN', label: '简体中文' },
  { id: 'ja', label: '日本語' },
  { id: 'es', label: 'Español' },
  { id: 'ru', label: 'Русский' },
] as const;
export type Language = typeof languages[number]['id'];
export type Messages = Record<string, readonly [string, string, string, string]>;
export const messages: Messages = { ...appMessages, ...diagnosisMessages, ...reportMessages, ...toolMessages };
const storageKey = 'neurotune.language';
const listeners = new Set<() => void>();
export function isLanguage(value: unknown): value is Language { return languages.some(item => item.id === value); }
function storedLanguage(): Language {
  try { const value = localStorage.getItem(storageKey); return isLanguage(value) ? value : 'en'; }
  catch { return 'en'; }
}
let current = storedLanguage();
export function getLanguage(): Language { return current; }
export function setLanguage(value: Language) {
  if (!isLanguage(value)) throw new RangeError('Unsupported language');
  current = value;
  try { localStorage.setItem(storageKey, value); } catch { /* The session preference still works if storage is unavailable. */ }
  if (typeof document !== 'undefined') document.documentElement.lang = value;
  listeners.forEach(listener => listener());
}
function subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; }
export function useLanguage(): Language { return useSyncExternalStore(subscribe, getLanguage, () => 'en'); }
export function t(source: string, values: Record<string, string | number> = {}): string {
  const index = languages.findIndex(item => item.id === current) - 1;
  const template = index < 0 ? source : messages[source]?.[index] ?? source;
  return template.replace(/\{(\w+)\}/g, (match, key: string) => Object.hasOwn(values, key) ? String(values[key]) : match);
}
export function formatDate(value: string): string { return new Date(value).toLocaleString(current); }
if (typeof document !== 'undefined') document.documentElement.lang = current;
if (typeof window !== 'undefined') window.addEventListener('storage', event => {
  if (event.key !== storageKey) return;
  current = storedLanguage(); document.documentElement.lang = current; listeners.forEach(listener => listener());
});
