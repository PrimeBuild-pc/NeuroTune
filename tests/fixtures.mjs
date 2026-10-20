export function csv({ frameTime = 10, frames = 120, process = 'game.exe', pid = 42, swap = '0x1', trial = 1, column = 'MsBetweenPresents' } = {}) {
  return `Application,ProcessID,SwapChainAddress,${column},PresentMode,Trial\n` + Array.from({ length: frames }, () => `${process},${pid},${swap},${frameTime},Hardware: Independent Flip,${trial}`).join('\n');
}
export const options = { process: 'game.exe', conditions: 'Synthetic fixed scene; 1080p; same settings' };
export const rawInventory = {
  schemaVersion: 1, collectorVersion: '0.1.0', generatedAtUtc: '2026-01-01T00:00:00.000Z',
  windows: { version: '10.0', build: '26100', architecture: '64-bit', secureBoot: null, virtualizationBasedSecurityStatus: 2 },
  platform: { totalPhysicalMemoryBytes: 34359738368, hypervisorPresent: true, model: 'not exported' },
  processors: [{ name: 'Synthetic CPU', physicalCores: 8, logicalProcessors: 16 }],
  gpus: [{ name: 'Synthetic GPU', vendor: 'Example', driverVersion: '1.0' }], warnings: ['Secure Boot unavailable'],
  userName: 'PRIVATE_SENTINEL', serialNumber: 'PRIVATE_SENTINEL'
};
