import {BreakpointObserver, type BreakpointState} from '@angular/cdk/layout';
import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {of} from 'rxjs';

import {AgentChatStore} from '../../store/agent-chat.store';
import {ChatWidgetComponent} from './chat-widget.component';

const OPEN_BUTTON = 'button[aria-label="Open Ledger chat"]';

const PANEL = '[role="dialog"]';

function mount(isDesktop: boolean): ComponentFixture<ChatWidgetComponent> {
  const state: BreakpointState = {matches: isDesktop, breakpoints: {}};
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {
        provide: BreakpointObserver,
        useValue: {observe: () => of(state), isMatched: () => isDesktop},
      },
    ],
  });
  TestBed.overrideComponent(ChatWidgetComponent, {
    set: {
      providers: [{provide: AgentChatStore, useValue: {historyKey: () => 0, history: () => []}}],
    },
  });
  const fixture = TestBed.createComponent(ChatWidgetComponent);
  fixture.detectChanges();
  return fixture;
}

function render(isDesktop: boolean): HTMLElement {
  return mount(isDesktop).nativeElement as HTMLElement;
}

describe('ChatWidgetComponent floating button', () => {
  it('is absent below the md breakpoint (phone)', () => {
    expect(render(false).querySelector(OPEN_BUTTON)).toBeNull();
  });

  it('is present at the md breakpoint and above (desktop)', () => {
    expect(render(true).querySelector(OPEN_BUTTON)).not.toBeNull();
  });
});

describe('ChatWidgetComponent overlay semantics', () => {
  const setup = () => {
    const fixture = mount(true);
    const el = fixture.nativeElement as HTMLElement;
    const toggle = (): HTMLButtonElement => {
      const button = el.querySelector<HTMLButtonElement>('button.rounded-full');
      if (!button) {
        throw new Error('toggle not rendered');
      }
      return button;
    };
    const panel = (): HTMLElement | null => el.querySelector<HTMLElement>(PANEL);
    const widget = (): HTMLElement => fixture.nativeElement as HTMLElement;
    const flush = async (): Promise<void> => {
      fixture.detectChanges();
      await fixture.whenStable();
    };
    return {fixture, toggle, panel, widget, flush};
  };

  it('starts collapsed with aria-expanded=false and no panel', () => {
    const {toggle, panel} = setup();
    expect(toggle().getAttribute('aria-expanded')).toBe('false');
    expect(toggle().hasAttribute('aria-controls')).toBe(false);
    expect(panel()).toBeNull();
  });

  it('opens a labelled dialog that the toggle controls and moves focus into it', async () => {
    const {toggle, panel, widget, flush} = setup();
    document.body.append(widget());
    toggle().click();
    await flush();
    expect(toggle().getAttribute('aria-expanded')).toBe('true');
    expect(panel()?.getAttribute('aria-label')).toBe('Ledger chat');
    expect(toggle().getAttribute('aria-controls')).toBe(panel()?.id);
    expect(panel()?.contains(document.activeElement)).toBe(true);
    widget().remove();
  });

  it('closes on Escape and returns focus to the toggle', async () => {
    const {toggle, panel, widget, flush} = setup();
    document.body.append(widget());
    toggle().click();
    await flush();
    panel()?.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape', bubbles: true}));
    await flush();
    expect(panel()).toBeNull();
    expect(toggle().getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(toggle());
    widget().remove();
  });

  it('closes from the toggle and restores focus to it', async () => {
    const {toggle, panel, widget, flush} = setup();
    document.body.append(widget());
    toggle().click();
    await flush();
    toggle().click();
    await flush();
    expect(panel()).toBeNull();
    expect(document.activeElement).toBe(toggle());
    widget().remove();
  });
});
