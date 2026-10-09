import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';

import {AuthStore} from '../../../modules/auth/store/auth.store';
import {APP_VERSION} from '../../../shared/constants/version/version.constants';
import {Permission} from '../../../shared/enums/permission/permission.enum';
import {WhatsNewPanelService} from '../../whats-new/services/whats-new-panel.service';
import {WhatsNewStore} from '../../whats-new/store/whats-new.store';
import {MorePageComponent} from './more-page.component';

describe('MorePageComponent', () => {
  const logout = vi.fn();
  const open = vi.fn();

  const render = (permissions: string[], unread = false): HTMLElement => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthStore,
          useValue: {permissions: signal(permissions), email: signal('me@test.dev'), logout},
        },
        {provide: WhatsNewStore, useValue: {hasUnread: signal(unread)}},
        {provide: WhatsNewPanelService, useValue: {open}},
      ],
    });
    const fixture = TestBed.createComponent(MorePageComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const links = (host: HTMLElement): (string | null)[] =>
    Array.from(host.querySelectorAll('nav a')).map(link => link.getAttribute('href'));

  it('links each nav item past the phone tabs', () => {
    expect(links(render([Permission.AiUse]))).toEqual([
      '/budgets',
      '/subscriptions',
      '/events',
      '/ledger',
      '/settings',
    ]);
  });

  it('leaves out Ledger without the AI permission, as the sidebar does', () => {
    expect(links(render([]))).not.toContain('/ledger');
  });

  it('signs out from the account row, which names the signed-in email', () => {
    const host = render([]);
    const row = host.querySelector<HTMLButtonElement>('[data-testid="more-logout"]');

    expect(row?.textContent).toContain('Log out');
    expect(row?.textContent).toContain('me@test.dev');
    row?.click();
    expect(logout).toHaveBeenCalledOnce();
  });

  it("opens the what's new panel from its row, with no dot once read", () => {
    const host = render([]);
    const row = host.querySelector<HTMLButtonElement>('[data-testid="more-whats-new"]');

    expect(row?.textContent).toContain("What's new");
    expect(row?.querySelector('.cmn-badge-indicator')).toBeNull();
    row?.click();
    expect(open).toHaveBeenCalledOnce();
  });

  it("marks the what's new row while a release is unread", () => {
    const row = render([], true).querySelector('[data-testid="more-whats-new"]');

    expect(row?.querySelector('.cmn-badge-indicator')).not.toBeNull();
  });

  it('shows the running version in a row of its own', () => {
    const row = render([]).querySelector('[data-testid="more-version"]');

    expect(row?.textContent).toContain(`Version ${APP_VERSION}`);
  });
});
