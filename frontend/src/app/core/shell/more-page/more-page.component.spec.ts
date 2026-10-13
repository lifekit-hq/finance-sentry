import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';

import {AlertsStore} from '../../../modules/alerts/store/alerts/alerts.store';
import {AuthStore} from '../../../modules/auth/store/auth.store';
import {APP_VERSION} from '../../../shared/constants/version/version.constants';
import {Permission} from '../../../shared/enums/permission/permission.enum';
import {ReportProblemPanelService} from '../../report-problem/services/report-problem-panel.service';
import {WhatsNewPanelService} from '../../whats-new/services/whats-new-panel.service';
import {MorePageComponent} from './more-page.component';

describe('MorePageComponent', () => {
  const logout = vi.fn();
  const open = vi.fn();
  const openReport = vi.fn();

  const render = (permissions: string[]): HTMLElement => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthStore,
          useValue: {
            permissions: signal(permissions),
            email: signal('me@test.dev'),
            fullName: signal('Ada Lovelace'),
            avatarInitials: signal('AL'),
            logout,
          },
        },
        {provide: AlertsStore, useValue: {unreadCount: signal(0)}},
        {provide: WhatsNewPanelService, useValue: {open}},
        {provide: ReportProblemPanelService, useValue: {open: openReport}},
      ],
    });
    const fixture = TestBed.createComponent(MorePageComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const navLinks = (host: HTMLElement): (string | null)[] =>
    Array.from(host.querySelectorAll('[data-more-section="nav"] a')).map(link =>
      link.getAttribute('href')
    );

  const accountRow = (host: HTMLElement, label: string): HTMLElement | undefined =>
    Array.from(
      host.querySelectorAll<HTMLElement>(
        '[data-more-section="account"] a, [data-more-section="account"] button'
      )
    ).find(row => row.textContent?.includes(label));

  it('links each nav item past the phone tabs', () => {
    expect(navLinks(render([Permission.AiUse]))).toEqual([
      '/budgets',
      '/subscriptions',
      '/events',
      '/ledger',
    ]);
  });

  it('leaves out Ledger without the AI permission, as the sidebar does', () => {
    expect(navLinks(render([]))).not.toContain('/ledger');
  });

  it('keeps Settings out of the nav list and reaches it from the account card', () => {
    const host = render([]);

    expect(navLinks(host)).not.toContain('/settings');
    expect(accountRow(host, 'Settings')?.getAttribute('href')).toBe('/settings');
  });

  it('names the signed-in person at the head of the account card', () => {
    const identity = render([]).querySelector('[data-more-identity]');

    expect(identity?.textContent).toContain('Ada Lovelace');
    expect(identity?.textContent).toContain('me@test.dev');
  });

  it('signs out from the account card, labelled "Sign out"', () => {
    const row = accountRow(render([]), 'Sign out');

    expect(row).toBeDefined();
    row?.click();
    expect(logout).toHaveBeenCalledOnce();
  });

  it("opens the what's new panel from the account card", () => {
    accountRow(render([]), "What's new")?.click();

    expect(open).toHaveBeenCalledOnce();
  });

  it('opens the report sheet from the account card', () => {
    accountRow(render([]), 'Report a problem')?.click();

    expect(openReport).toHaveBeenCalledOnce();
  });

  it('shows the running version', () => {
    const version = render([]).querySelector('[data-more-version]');

    expect(version?.textContent).toContain(`v${APP_VERSION}`);
  });
});
