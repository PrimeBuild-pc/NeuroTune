import { useEffect, useRef, useSyncExternalStore } from 'react';
import { LayoutGroup, motion } from 'motion/react';
import type { LucideIcon } from 'lucide-react';
import { t, useLanguage } from '../i18n';

const reducedMotionQuery = '(prefers-reduced-motion: reduce)';
function subscribeMotion(change: () => void) {
  const media = window.matchMedia(reducedMotionQuery);
  media.addEventListener('change', change);
  const observer = new MutationObserver(change);
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-motion'] });
  return () => { media.removeEventListener('change', change); observer.disconnect(); };
}
function motionAllowed() {
  return document.documentElement.dataset.motion !== 'quiet' && !window.matchMedia(reducedMotionQuery).matches;
}

export type Page = 'overview' | 'provider' | 'scan' | 'advanced' | 'measurements' | 'review' | 'activity' | 'settings' | 'security' | 'tools';
export type NavigationItem = { id: Page; label: string; icon: LucideIcon };
// Five sections in the sidebar; the remaining pages are tabs inside the section that owns them.
const sections: Page[] = ['overview', 'scan', 'measurements', 'review', 'settings'];
const sectionTabs: Partial<Record<Page, { page: Page; label: string; expert?: boolean }[]>> = {
  scan: [{ page: 'scan', label: 'Complete diagnosis' }, { page: 'security', label: 'Privacy & security' }],
  review: [{ page: 'review', label: 'Review changes' }, { page: 'activity', label: 'Activity & restore' }],
  settings: [{ page: 'settings', label: 'General' }, { page: 'provider', label: 'AI provider' },
    { page: 'tools', label: 'Advanced tools', expert: true }, { page: 'advanced', label: 'Local evidence', expert: true }],
};
function sectionOf(page: Page): Page {
  return sections.find(section => sectionTabs[section]?.some(tab => tab.page === page)) ?? page;
}

export function SectionTabs({ page, onPage, expert, disabled }: { page: Page; onPage: (page: Page) => void; expert: boolean; disabled: boolean }) {
  const tabs = sectionTabs[sectionOf(page)]?.filter(tab => expert || !tab.expert || tab.page === page);
  if (!tabs || tabs.length < 2) return null;
  return <div className="section-switcher section-tabs" role="group" aria-label={t('Section pages')}>
    {tabs.map(tab => <button key={tab.page} aria-pressed={tab.page === page} disabled={disabled} onClick={() => onPage(tab.page)}>{t(tab.label)}</button>)}
  </div>;
}

export function Navigation({ items, page, onPage, disabled, quiet }: {
  items: NavigationItem[]; page: Page; onPage: (page: Page) => void; disabled: boolean; quiet: boolean;
}) {
  const language = useLanguage();
  const navRef = useRef<HTMLElement>(null);
  useEffect(() => {
    const nav = navRef.current;
    if (!nav) return;
    const revealActive = () => {
      const active = nav.querySelector<HTMLElement>('.nav-item:focus') ?? nav.querySelector<HTMLElement>('[aria-current="page"]');
      if (!active) return;
      const bounds = nav.getBoundingClientRect();
      const item = active.getBoundingClientRect();
      if (item.top < bounds.top) nav.scrollTop -= bounds.top - item.top + 4;
      else if (item.bottom > bounds.bottom) nav.scrollTop += item.bottom - bounds.bottom + 4;
    };
    revealActive();
    const observer = new ResizeObserver(revealActive);
    observer.observe(nav);
    return () => observer.disconnect();
  }, [page, language]);
  // CSS cannot cancel Motion's JS layout animations. Unmount the moving indicator when quiet/reduced.
  const animate = useSyncExternalStore(subscribeMotion, motionAllowed, () => false) && !quiet;
  function itemButton(item: NavigationItem) {
    const active = sectionOf(page) === item.id;
    return <button key={item.id} data-page={item.id} disabled={disabled} title={t(item.label)} aria-label={t(item.label)} aria-current={active ? 'page' : undefined} className={`nav-item${active ? ' active' : ''}`} onClick={() => onPage(item.id)}>
      {active && (animate
        ? <motion.div className="nav-active-background" layoutId="active-navigation" initial={false} transition={{ duration: .22, ease: [.2, .8, .2, 1] }} aria-hidden="true"/>
        : <div className="nav-active-background" aria-hidden="true"/>)}
      <item.icon size={18} aria-hidden="true"/><span>{t(item.label)}</span>
    </button>;
  }
  return <LayoutGroup id="main-navigation"><nav ref={navRef} aria-label={t('Main navigation')}>
    <div className="nav-group">{sections.flatMap(id => items.filter(item => item.id === id)).map(itemButton)}</div>
  </nav></LayoutGroup>;
}
