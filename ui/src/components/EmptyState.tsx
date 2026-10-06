import { ChevronRight, type LucideIcon } from 'lucide-react';
import { t } from '../i18n';

export function EmptyState({ icon: Icon, title, text, action, onAction }: { icon: LucideIcon; title: string; text: string; action?: string; onAction?: () => void }) {
  return <div className="empty-state"><div><Icon size={30} aria-hidden="true"/></div><h2>{t(title)}</h2><p>{t(text)}</p>{action && <button className="primary" onClick={onAction}>{t(action)}<ChevronRight size={17} aria-hidden="true"/></button>}</div>;
}
