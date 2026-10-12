import { useSyncExternalStore } from 'react';

// Simple mode (default) hides rarely used, jargon-heavy controls; Expert shows them. Display only: no permission changes.
const storageKey = 'neurotune.expert';
const listeners = new Set<() => void>();
function stored() {
  try { return localStorage.getItem(storageKey) === 'true'; }
  catch { return false; }
}
let current = stored();
export function getExpert() { return current; }
export function setExpert(value: boolean) {
  current = value;
  try { localStorage.setItem(storageKey, String(value)); } catch { /* The session preference still works if storage is unavailable. */ }
  listeners.forEach(listener => listener());
}
function subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; }
export function useExpert() { return useSyncExternalStore(subscribe, getExpert, () => false); }
