import {Component} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {RouterTestingHarness} from '@angular/router/testing';
import {describe, expect, it} from 'vitest';

import {RoutePatternUtils, type RouteSnapshotLike} from './route-pattern.utils';

@Component({template: ''})
class BlankComponent {}

const node = (
  path: string | undefined,
  child: RouteSnapshotLike | null = null
): RouteSnapshotLike => ({
  routeConfig: path === undefined ? null : {path},
  firstChild: child,
});

describe('RoutePatternUtils', () => {
  it('joins the configured paths down the tree', () => {
    const tree = node(undefined, node('', node('assets', node(':symbol'))));

    expect(RoutePatternUtils.fromSnapshot(tree)).toBe('/assets/:symbol');
  });

  it('names the root for an empty tree', () => {
    expect(RoutePatternUtils.fromSnapshot(null)).toBe('/');
    expect(RoutePatternUtils.fromSnapshot(node(undefined, node('')))).toBe('/');
  });

  describe('with the real router', () => {
    const pattern = async (url: string): Promise<string> => {
      TestBed.configureTestingModule({
        providers: [
          provideRouter([
            {
              path: '',
              children: [
                {path: 'accounts', children: [{path: ':id', component: BlankComponent}]},
                {path: 'transactions', component: BlankComponent},
              ],
            },
          ]),
        ],
      });
      await RouterTestingHarness.create(url);
      return RoutePatternUtils.fromSnapshot(TestBed.inject(Router).routerState.snapshot.root);
    };

    it('never carries the id, the query string or the fragment', async () => {
      const result = await pattern('/accounts/3f9c2d10-aaaa?q=secret%20merchant#frag-9');

      expect(result).toBe('/accounts/:id');
      expect(result).not.toContain('3f9c');
      expect(result).not.toContain('secret');
      expect(result).not.toContain('frag');
    });

    it('drops the query of a route with no params', async () => {
      expect(await pattern('/transactions?account=abc&from=2026-01-01')).toBe('/transactions');
    });
  });
});
