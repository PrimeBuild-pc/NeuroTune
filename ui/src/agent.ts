import { invoke } from '@tauri-apps/api/core';

export async function agent<T>(command: string, payload?: unknown): Promise<T> {
  return invoke<T>('agent', { command, payload: payload ?? null });
}
