import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';

import {AuthStore} from '../../../modules/auth/store/auth.store';
import {Permission} from '../../../shared/enums/permission/permission.enum';
import {MorePageComponent} from './more-page.component';

describe('MorePageComponent', () => {
  const render = (permissions: string[]): HTMLElement => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {provide: AuthStore, useValue: {permissions: signal(permissions)}},
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
});
