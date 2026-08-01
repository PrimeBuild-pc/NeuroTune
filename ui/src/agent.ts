import { invoke } from '@tauri-apps/api/core';

export function newRequestId(): string {
  return crypto.randomUUID();
}

export async function agent<T>(command: string, payload?: unknown, requestId = newRequestId()): Promise<T> {
  return invoke<T>('agent', { requestId, command, payload: payload ?? null });
}

export async function cancelAgent(requestId: string): Promise<boolean> {
  return invoke<boolean>('cancel_agent', { requestId });
}
