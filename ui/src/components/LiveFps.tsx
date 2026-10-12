import { useEffect, useState } from 'react';
import { listen } from '@tauri-apps/api/event';
import { Gauge } from 'lucide-react';
import { agent, cancelAgent, newRequestId } from '../agent';
import { t } from '../i18n';

type Sample = { Fps: number; OnePercentLowFps: number; FrameTimeP99Ms: number; Frames: number };

// Display only: frames the game presents through DirectX, while it is being recorded. Not part of any report or decision.
export function LiveFps({ processId, seconds }: { processId: number; seconds: number }) {
  const [sample, setSample] = useState<Sample>();
  useEffect(() => {
    const id = newRequestId();
    const stop = listen<{ requestId: string; message: string }>('agent-progress', event => {
      if (event.payload.requestId !== id || !event.payload.message.startsWith('live-fps ')) return;
      try { setSample(JSON.parse(event.payload.message.slice(9)) as Sample); } catch { /* A malformed line is skipped. */ }
    });
    void agent('measurement-fps-live', { processId, seconds }, id).catch(() => {});
    return () => { void cancelAgent(id).catch(() => {}); void stop.then(unlisten => unlisten()); };
  }, [processId, seconds]);
  return <section className="section-card live-fps" aria-live="polite" aria-label={t('Live FPS')}>
    <div className="section-heading"><h3><Gauge size={18} aria-hidden="true"/> {t('Live FPS')}</h3><span className="status-pill">{t('Display only')}</span></div>
    {sample && sample.Frames > 1
      ? <dl className="compare-numbers"><div><dt>FPS</dt><dd>{sample.Fps}</dd></div><div><dt>{t('1% low')}</dt><dd>{sample.OnePercentLowFps}</dd></div><div><dt>{t('Frame-time P99')}</dt><dd>{sample.FrameTimeP99Ms} <small>ms</small></dd></div></dl>
      : <p className="muted-copy">{t('Waiting for DirectX frames from the game. Vulkan or OpenGL games may not report here; the recorded trace is unaffected.')}</p>}
  </section>;
}
