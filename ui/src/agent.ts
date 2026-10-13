import { invoke } from '@tauri-apps/api/core';

export function newRequestId(): string {
  return crypto.randomUUID();
}

export async function agent<T>(command: string, payload?: unknown, requestId = newRequestId()): Promise<T> {
  return invoke<T>('agent', { requestId, command, payload: payload ?? null });
}

// F10 sign-in task lives in the desktop shell, which knows its own executable path.
export async function logonTask(enabled?: boolean): Promise<boolean> {
  return invoke<boolean>('logon_task', { enabled: enabled ?? null });
}

export async function cancelAgent(requestId: string): Promise<boolean> {
  return invoke<boolean>('cancel_agent', { requestId });
}
