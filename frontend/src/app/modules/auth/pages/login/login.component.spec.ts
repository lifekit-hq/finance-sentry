import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {describe, expect, it, vi} from 'vitest';

import {type SignInMethods} from '../../models/auth/auth.model';
import {type SignInMethodsStatus} from '../../store/auth.state';
import {AuthStore} from '../../store/auth.store';
import {LoginComponent} from './login.component';

function setup(status: SignInMethodsStatus, methods: SignInMethods | null = null) {
  const store = {
    signInMethods: signal(methods),
    signInMethodsStatus: signal(status),
    isLoading: signal(false),
    errorMessage: signal(''),
    flashMessage: signal(null),
    loadSignInMethods: vi.fn(),
    startOidcSignIn: vi.fn(),
  };
  TestBed.configureTestingModule({
    providers: [provideRouter([]), {provide: AuthStore, useValue: store}],
  });
  const fixture = TestBed.createComponent(LoginComponent);
  fixture.detectChanges();
  return {store, root: fixture.nativeElement as HTMLElement};
}

describe('LoginComponent sign-in methods', () => {
  it('shows a skeleton, not a password form, until the methods answer', () => {
    const {root} = setup('loading');

    expect(root.querySelector('cmn-skeleton')).not.toBeNull();
    expect(root.querySelector('form')).toBeNull();
  });

  it('on a methods failure shows an error with Retry and keeps the OIDC path reachable', () => {
    const {root, store} = setup('error');

    expect(root.querySelector('[data-testid="methods-error"]')?.textContent).toContain(
      "Couldn't load the sign-in options."
    );
    expect(root.querySelector('form')).toBeNull();
    const buttons = Array.from(root.querySelectorAll<HTMLElement>('cmn-button'));
    const signIn = buttons.find(b => b.textContent?.trim() === 'Sign in');
    expect(signIn).toBeDefined();
    signIn?.querySelector('button')?.click();
    expect(store.startOidcSignIn).toHaveBeenCalledOnce();

    buttons
      .find(b => b.textContent?.includes('Retry'))
      ?.querySelector('button')
      ?.click();
    expect(store.loadSignInMethods).toHaveBeenCalledOnce();
  });

  it('renders the reported methods once loaded, without the failure banner', () => {
    const {root} = setup('success', {oidc: false, passwordLogin: true});

    expect(root.querySelector('[data-testid="methods-error"]')).toBeNull();
    expect(root.querySelector('form')).not.toBeNull();
  });
});
