import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {Router} from '@angular/router';
import {describe, expect, it, vi} from 'vitest';

import {
  LEGAL_TEMPLATE_NOTICE,
  NOT_ADVICE_NOTICE,
} from '../../../../shared/constants/legal/legal.constants';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {AuthStore} from '../../../auth/store/auth.store';
import {TermsComponent} from './terms.component';

function setup(authenticated: boolean) {
  const navigateByUrl = vi.fn();
  TestBed.configureTestingModule({
    providers: [
      {provide: AuthStore, useValue: {isAuthenticated: signal(authenticated)}},
      {provide: Router, useValue: {navigateByUrl}},
    ],
  });
  const fixture = TestBed.createComponent(TermsComponent);
  fixture.detectChanges();
  return {navigateByUrl, root: fixture.nativeElement as HTMLElement};
}

describe('TermsComponent', () => {
  it('says it is an unreviewed template', () => {
    const {root} = setup(false);

    expect(root.querySelector('[data-testid="template-notice"]')?.textContent).toContain(
      LEGAL_TEMPLATE_NOTICE
    );
  });

  it('carries the not-financial-advice clause and the no-warranty clause', () => {
    const {root} = setup(false);

    const advice = root.querySelector('[data-testid="not-advice"]')?.textContent ?? '';
    expect(advice).toContain(NOT_ADVICE_NOTICE);
    expect(advice).toContain('never places a trade');
    const warranty = root.querySelector('[data-testid="no-warranty"]')?.textContent ?? '';
    expect(warranty).toContain('provided as it is');
    expect(warranty).toContain('not responsible for losses');
  });

  it('goes back to sign-in for a visitor and to settings for a signed-in person', () => {
    const guest = setup(false);
    guest.root.querySelector<HTMLElement>('[data-testid="terms-back"] button')?.click();
    expect(guest.navigateByUrl).toHaveBeenCalledWith(AppRoute.Login);

    TestBed.resetTestingModule();
    const member = setup(true);
    member.root.querySelector<HTMLElement>('[data-testid="terms-back"] button')?.click();
    expect(member.navigateByUrl).toHaveBeenCalledWith(AppRoute.Settings);
  });

  it('links on to the privacy notice', () => {
    const {root, navigateByUrl} = setup(false);

    root.querySelector<HTMLElement>('[data-testid="terms-privacy-link"] button')?.click();

    expect(navigateByUrl).toHaveBeenCalledWith(AppRoute.Privacy);
  });
});
