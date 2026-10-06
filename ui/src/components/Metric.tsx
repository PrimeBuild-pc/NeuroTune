import type { LucideIcon } from 'lucide-react';

export function Metric({ icon: Icon, label, value, tone }: { icon: LucideIcon; label: string; value: string; tone?: 'good' | 'warn' }) {
  return <article className="metric-card"><div className={`metric-icon ${tone ?? ''}`}><Icon size={19} aria-hidden="true"/></div><div><span>{label}</span><strong>{value}</strong></div></article>;
}
