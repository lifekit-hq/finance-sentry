import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {Router} from '@angular/router';
import {describe, expect, it, vi} from 'vitest';

import {LEGAL_TEMPLATE_NOTICE} from '../../../../shared/constants/legal/legal.constants';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {AuthStore} from '../../../auth/store/auth.store';
import {PrivacyComponent} from './privacy.component';

function setup(authenticated: boolean) {
  const navigateByUrl = vi.fn();
  TestBed.configureTestingModule({
    providers: [
      {provide: AuthStore, useValue: {isAuthenticated: signal(authenticated)}},
      {provide: Router, useValue: {navigateByUrl}},
    ],
  });
  const fixture = TestBed.createComponent(PrivacyComponent);
  fixture.detectChanges();
  return {navigateByUrl, root: fixture.nativeElement as HTMLElement};
}

describe('PrivacyComponent', () => {
  it('says it is an unreviewed template and covers only this instance', () => {
    const {root} = setup(false);

    expect(root.querySelector('[data-testid="template-notice"]')?.textContent).toContain(
      LEGAL_TEMPLATE_NOTICE
    );
    expect(root.textContent).toContain('this instance only');
    expect(root.textContent).toContain('has not had legal review');
  });

  it('discloses the access record for declared profile data, fields only and never values', () => {
    const {root} = setup(false);
    const text = (root.textContent ?? '').replace(/\s+/g, ' ');

    expect(text).toContain('An access record for the personal details you declare');
    expect(text).toContain('never the values themselves');
    expect(text).toContain('the access record after about 13 months');
  });

  it('names the data export and account closure as the reader’s choices', () => {
    const {root} = setup(false);

    expect(root.textContent).toContain('Download your data');
    expect(root.textContent).toContain('Close your account');
  });

  it('goes back to sign-in for a visitor and to settings for a signed-in person', () => {
    const guest = setup(false);
    guest.root.querySelector<HTMLElement>('[data-testid="privacy-back"] button')?.click();
    expect(guest.navigateByUrl).toHaveBeenCalledWith(AppRoute.Login);

    TestBed.resetTestingModule();
    const member = setup(true);
    member.root.querySelector<HTMLElement>('[data-testid="privacy-back"] button')?.click();
    expect(member.navigateByUrl).toHaveBeenCalledWith(AppRoute.Settings);
  });

  it('links on to the terms', () => {
    const {root, navigateByUrl} = setup(false);

    root.querySelector<HTMLElement>('[data-testid="privacy-terms-link"] button')?.click();

    expect(navigateByUrl).toHaveBeenCalledWith(AppRoute.Terms);
  });
});
