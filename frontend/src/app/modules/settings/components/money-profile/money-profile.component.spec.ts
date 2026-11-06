import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {CmnDialogService} from '@lifekit-hq/ui';
import {of} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {
  type IncomeSourceInput,
  type IncomeSourceRow,
} from '../../models/income-source/income-source.model';
import {MONTHLY_SALARY, RETIRED_SIDE} from '../../store/income-sources/income-sources.fixtures';
import {IncomeSourcesStore} from '../../store/income-sources/income-sources.store';
import {MoneyProfileComponent} from './money-profile.component';

const INPUT: IncomeSourceInput = {
  kind: 'Salary',
  label: 'Day job',
  amount: 3000,
  currency: 'EUR',
  cadence: 'Monthly',
  grossOrNet: 'Net',
  startDate: '2026-01-01',
  endDate: null,
};

const ROWS: IncomeSourceRow[] = [
  {...MONTHLY_SALARY, sublabel: 'Salary · Monthly · After tax'},
  {...RETIRED_SIDE, sublabel: 'Side income · Irregular · Before tax'},
];

function setup(dialogResult?: unknown) {
  const store = {
    rows: signal<IncomeSourceRow[]>(ROWS),
    isLoading: signal(false),
    sourcesErrorMessage: signal(''),
    saveErrorMessage: signal(''),
    actionErrorMessage: signal(''),
    isSaveLoading: signal(false),
    load: vi.fn(),
    save: vi.fn(),
    retire: vi.fn(),
    remove: vi.fn(),
  };
  const dialog = {open: vi.fn(() => ({afterClosed: () => of(dialogResult)}))};
  TestBed.configureTestingModule({
    providers: [
      {provide: CmnDialogService, useValue: dialog},
      {provide: ConnectivityService, useValue: {offline: signal(false)}},
    ],
  });
  TestBed.overrideComponent(MoneyProfileComponent, {
    set: {providers: [{provide: IncomeSourcesStore, useValue: store}]},
  });
  const fixture = TestBed.createComponent(MoneyProfileComponent);
  fixture.detectChanges();
  return {store, dialog, fixture, component: fixture.componentInstance};
}

describe('MoneyProfileComponent', () => {
  beforeEach(() => TestBed.resetTestingModule());

  it('lists each source with its amount and monthly equivalent', () => {
    const {fixture} = setup();
    const el = fixture.nativeElement as HTMLElement;

    const rows = el.querySelectorAll('[data-testid="income-source-row"]');
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('Day job');
    expect(rows[0].querySelector('[data-testid="income-amount"]')?.textContent).toContain(
      '€3,000.00'
    );
    expect(rows[0].querySelector('[data-testid="income-monthly"]')?.textContent).toContain(
      '€3,000.00 / month'
    );
    expect(rows[1].querySelector('[data-testid="income-monthly"]')?.textContent).toContain(
      'Irregular'
    );
  });

  it('shows the save error above the list', () => {
    const {store, fixture} = setup();
    store.saveErrorMessage.set('Could not save the income source.');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Could not save the income source.'
    );
  });

  it('saves a new source from the add dialog', () => {
    const {store, component} = setup(INPUT);

    component.openSource();

    expect(store.save).toHaveBeenCalledWith({id: null, input: INPUT});
  });

  it('saves an edit against the source id', () => {
    const {store, component} = setup(INPUT);

    component.openSource(ROWS[0]);

    expect(store.save).toHaveBeenCalledWith({id: 's-1', input: INPUT});
  });

  it('saves nothing when the dialog is dismissed', () => {
    const {store, component} = setup(undefined);

    component.openSource();

    expect(store.save).not.toHaveBeenCalled();
  });

  it('opens the editor from the menu', () => {
    const {store, component} = setup(INPUT);

    component.onAction(ROWS[0], 'edit');

    expect(store.save).toHaveBeenCalledWith({id: 's-1', input: INPUT});
  });

  it('retires a source once confirmed', () => {
    const {store, component} = setup(true);

    component.onAction(ROWS[0], 'retire');

    expect(store.retire).toHaveBeenCalledWith('s-1');
  });

  it('does not retire when the confirmation is declined', () => {
    const {store, component} = setup(false);

    component.onAction(ROWS[0], 'retire');

    expect(store.retire).not.toHaveBeenCalled();
  });

  it('deletes a source once confirmed', () => {
    const {store, component} = setup(true);

    component.onAction(ROWS[1], 'delete');

    expect(store.remove).toHaveBeenCalledWith('s-2');
  });

  it('ignores an unknown menu action', () => {
    const {store, dialog, component} = setup(true);

    component.onAction(ROWS[0], 'nope');

    expect(dialog.open).not.toHaveBeenCalled();
    expect(store.retire).not.toHaveBeenCalled();
  });
});
