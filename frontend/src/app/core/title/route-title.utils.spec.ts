import type {ActivatedRouteSnapshot, ResolveFn, RouterStateSnapshot} from '@angular/router';

import {RouteTitleUtils} from './route-title.utils';

describe('RouteTitleUtils', () => {
  const route: unknown = {};
  const state: unknown = {};
  const run = (resolver: unknown): unknown =>
    (resolver as ResolveFn<string>)(route as ActivatedRouteSnapshot, state as RouterStateSnapshot);

  it('resolves a static title for both Route.title and the header key', () => {
    const {title, resolve} = RouteTitleUtils.of('Budgets');
    expect(run(title)).toBe('Budgets');
    expect(run(resolve?.['title'])).toBe('Budgets');
  });

  it('shares one resolver between Route.title and the header key', () => {
    const resolver: ResolveFn<string> = () => 'AAPL';
    const {title, resolve} = RouteTitleUtils.of(resolver);
    expect(title).toBe(resolver);
    expect(resolve?.['title']).toBe(resolver);
  });
});
