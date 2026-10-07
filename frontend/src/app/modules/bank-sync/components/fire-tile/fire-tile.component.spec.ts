import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {describe, expect, it} from 'vitest';

import {type FireProjection} from '../../models/fire/fire.model';
import {fireComputed} from '../../store/fire/fire.computed';
import {FireStore} from '../../store/fire/fire.store';
import {FireTileComponent} from './fire-tile.component';

function fire(overrides: Partial<FireProjection> = {}): FireProjection {
  return {
    status: 'Projected',
    target: 1_000_000,
    currentNetWorth: 250_000,
    monthlySavings: 2_000,
    annualSpend: 40_000,
    safeWithdrawalRate: 0.04,
    realAnnualReturn: 0.05,
    projectedDate: '2041-03-26',
    monthsToFire: 150.2,
    hasStaleSleeves: false,
    baseCurrency: 'USD',
    ...overrides,
  };
}

function render(projection: Nullable<FireProjection>) {
  const computeds = fireComputed({projection: signal(projection), status: signal('idle')});
  TestBed.configureTestingModule({providers: [provideRouter([])]});
  TestBed.overrideComponent(FireTileComponent, {
    set: {providers: [{provide: FireStore, useValue: {...computeds}}]},
  });
  const fixture = TestBed.createComponent(FireTileComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('FireTileComponent', () => {
  it('shows the date, the time to go and every assumption in words', () => {
    const el = render(fire());
    expect(el.querySelector('[data-testid="fire-headline"]')?.textContent).toContain('Mar 2041');
    expect(el.querySelector('[data-testid="fire-runway"]')?.textContent).toContain(
      '12 years 7 months'
    );
    const assumptions = el.querySelector('[data-testid="fire-assumptions"]')?.textContent ?? '';
    expect(assumptions).toContain('4% safe withdrawal rate');
    expect(assumptions).toContain('5% real (after-inflation) annual return');
  });

  it('explains the missing date when nothing is being saved', () => {
    const el = render(fire({status: 'NotSaving', projectedDate: null, monthsToFire: null}));
    expect(el.querySelector('[data-testid="fire-headline"]')?.textContent).toContain(
      'nothing is being saved'
    );
    expect(el.querySelector('[data-testid="fire-runway"]')).toBeNull();
  });

  it('says so when the target is already reached', () => {
    const el = render(fire({status: 'AlreadyReached', projectedDate: null, monthsToFire: 0}));
    expect(el.querySelector('[data-testid="fire-headline"]')?.textContent).toContain('reached');
  });

  it('renders nothing below the complete-month minimum', () => {
    const el = render(fire({status: 'InsufficientHistory'}));
    expect(el.querySelector('[data-testid="fire-tile"]')).toBeNull();
  });

  it('carries the staleness notice when sleeves are stale', () => {
    const el = render(fire({hasStaleSleeves: true}));
    expect(el.textContent).toContain('stale');
  });

  it('links to the settings page to change the assumptions', () => {
    const el = render(fire());
    expect(el.querySelector('a')?.getAttribute('href')).toBe('/settings');
  });
});
