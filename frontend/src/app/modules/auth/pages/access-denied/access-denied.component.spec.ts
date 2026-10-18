import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {describe, expect, it, vi} from 'vitest';

import {AuthStore} from '../../store/auth.store';
import {AccessDeniedComponent} from './access-denied.component';

function setup() {
  const store = {startOidcSignIn: vi.fn()};
  TestBed.configureTestingModule({
    providers: [provideRouter([]), {provide: AuthStore, useValue: store}],
  });
  const fixture = TestBed.createComponent(AccessDeniedComponent);
  fixture.detectChanges();
  return {store, root: fixture.nativeElement as HTMLElement};
}

describe('AccessDeniedComponent', () => {
  it('tells the person to ask the owner for access', () => {
    const {root} = setup();

    expect(root.querySelector('[data-testid="access-denied-message"]')?.textContent).toContain(
      'Ask the owner for access.'
    );
  });

  it('Try again starts a fresh provider sign-in', () => {
    const {root, store} = setup();

    root.querySelector<HTMLElement>('cmn-button button')?.click();

    expect(store.startOidcSignIn).toHaveBeenCalledOnce();
  });
});
