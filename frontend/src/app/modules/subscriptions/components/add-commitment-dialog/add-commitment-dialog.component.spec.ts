import {DialogRef} from '@angular/cdk/dialog';
import {TestBed} from '@angular/core/testing';
import {CMN_DIALOG_DATA} from '@lifekit-hq/ui';
import {type Observable, of, Subject} from 'rxjs';
import {describe, expect, it, vi} from 'vitest';

import {type CommitmentCandidate} from '../../models/commitment-candidate/commitment-candidate.model';
import {type CommitmentDialogData} from '../../models/commitment-candidate/commitment-dialog.model';
import {CommitmentCandidatesService} from '../../services/commitment-candidates.service';
import {SubscriptionsService} from '../../services/subscriptions.service';
import {AddCommitmentDialogComponent} from './add-commitment-dialog.component';

const CANDIDATE: CommitmentCandidate = {
  transactionId: 'tx-1',
  bankName: 'Test Bank',
  currency: 'EUR',
  amount: 16.19,
  date: '2026-09-21',
  description: 'ACME HOSTING GMBH',
  merchantName: 'Acme Hosting',
};

const LATEST_AMOUNT = 12.99;

function setup(data?: CommitmentDialogData, anchor$: Observable<unknown> | null = null) {
  const close = vi.fn();
  const getCommitmentAnchor = vi.fn(
    () =>
      anchor$ ??
      of({
        amount: CANDIDATE.amount,
        currency: 'EUR',
        date: '2026-09-21',
        chargeCount: 1,
        cadence: null,
      })
  );
  TestBed.configureTestingModule({
    providers: [
      {provide: DialogRef, useValue: {close}},
      {provide: CMN_DIALOG_DATA, useValue: data},
      {
        provide: SubscriptionsService,
        useValue: {getCommitmentAnchor},
      },
      {
        provide: CommitmentCandidatesService,
        useValue: {
          search: vi.fn(() =>
            of({items: [CANDIDATE], totalCount: 1, offset: 0, limit: 30, hasMore: false})
          ),
        },
      },
    ],
  });
  return {
    component: TestBed.createComponent(AddCommitmentDialogComponent).componentInstance,
    close,
    getCommitmentAnchor,
  };
}

describe('AddCommitmentDialogComponent', () => {
  it('does not close until a transaction is picked', () => {
    const {component, close} = setup();
    component.submit();
    expect(close).not.toHaveBeenCalled();
    expect(component.form.touched).toBe(true);
  });

  it('pre-fills name and amount from the picked transaction', () => {
    const {component} = setup();
    component.pick(CANDIDATE);
    expect(component.picker.selected()).toEqual(CANDIDATE);
    expect(component.form.getRawValue()).toMatchObject({
      transactionId: 'tx-1',
      merchant: 'Acme Hosting',
      monthlyAmount: 16.19,
    });
  });

  it('pre-fills the amount from the latest same-key charge, not the older pick', () => {
    const {component} = setup(
      undefined,
      of({
        amount: LATEST_AMOUNT,
        currency: 'EUR',
        date: '2026-10-05',
        chargeCount: 3,
        cadence: 'monthly',
      })
    );
    component.pick(CANDIDATE);
    expect(component.form.controls.monthlyAmount.value).toBe(LATEST_AMOUNT);
    expect(component.picker.anchor()?.amount).toBe(LATEST_AMOUNT);
  });

  it('reads the anchor for the kind being added', () => {
    const {component, getCommitmentAnchor} = setup();
    component.setKind('installment');
    component.pick(CANDIDATE);
    expect(getCommitmentAnchor).toHaveBeenCalledWith('tx-1', 'installment');
  });

  it('re-reads the anchor when the kind changes after a pick', () => {
    const {component, getCommitmentAnchor} = setup();
    component.pick(CANDIDATE);
    expect(getCommitmentAnchor).toHaveBeenLastCalledWith('tx-1', 'subscription');
    component.setKind('installment');
    expect(getCommitmentAnchor).toHaveBeenLastCalledWith('tx-1', 'installment');
  });

  it('does not read an anchor on a kind change before a pick', () => {
    const {component, getCommitmentAnchor} = setup();
    component.setKind('installment');
    expect(getCommitmentAnchor).not.toHaveBeenCalled();
  });

  it('keeps an amount the user edited before the latest charge arrived', () => {
    const lookup = new Subject<unknown>();
    const {component} = setup(undefined, lookup);
    component.pick(CANDIDATE);
    component.form.controls.monthlyAmount.setValue(11);
    component.form.controls.monthlyAmount.markAsDirty();
    lookup.next({
      amount: LATEST_AMOUNT,
      currency: 'EUR',
      date: '2026-10-05',
      chargeCount: 3,
      cadence: null,
    });
    expect(component.form.controls.monthlyAmount.value).toBe(11);
  });

  it('stores the amount the user confirmed over the latest charge', () => {
    const {component, close} = setup(
      undefined,
      of({
        amount: LATEST_AMOUNT,
        currency: 'EUR',
        date: '2026-10-05',
        chargeCount: 3,
        cadence: 'monthly',
      })
    );
    component.pick(CANDIDATE);
    component.form.controls.monthlyAmount.setValue(11);
    component.submit();
    expect(close).toHaveBeenCalledWith(expect.objectContaining({monthlyAmount: 11}));
  });

  it('falls back to the description when the transaction has no merchant', () => {
    const {component} = setup();
    component.pick({...CANDIDATE, merchantName: null});
    expect(component.form.controls.merchant.value).toBe('ACME HOSTING GMBH');
  });

  it('closes with a subscription request and no term', () => {
    const {component, close} = setup();
    component.pick(CANDIDATE);
    component.form.patchValue({merchant: ' Acme ', termCount: 12});
    component.submit();
    expect(close).toHaveBeenCalledWith({
      transactionId: 'tx-1',
      kind: 'subscription',
      merchant: 'Acme',
      monthlyAmount: 16.19,
      termCount: null,
      cadence: 'monthly',
    });
  });

  it('closes with an installment request carrying its term', () => {
    const {component, close} = setup();
    component.setKind('installment');
    component.pick(CANDIDATE);
    component.form.patchValue({monthlyAmount: 20, termCount: 12});
    component.submit();
    expect(close).toHaveBeenCalledWith({
      transactionId: 'tx-1',
      kind: 'installment',
      merchant: 'Acme Hosting',
      monthlyAmount: 20,
      termCount: 12,
      cadence: 'monthly',
    });
  });

  it('pre-selects the cadence read from the charge history', () => {
    const {component, close} = setup(
      undefined,
      of({
        amount: LATEST_AMOUNT,
        currency: 'EUR',
        date: '2026-10-05',
        chargeCount: 2,
        cadence: 'annual',
      })
    );
    component.pick(CANDIDATE);
    expect(component.form.controls.cadence.value).toBe('annual');
    component.submit();
    expect(close).toHaveBeenCalledWith(expect.objectContaining({cadence: 'annual'}));
  });

  it('keeps monthly when the history has too few charges to tell', () => {
    const {component} = setup();
    component.pick(CANDIDATE);
    expect(component.form.controls.cadence.value).toBe('monthly');
  });

  it('keeps a cadence the user chose before the history arrived', () => {
    const lookup = new Subject<unknown>();
    const {component} = setup(undefined, lookup);
    component.pick(CANDIDATE);
    component.setCadence('monthly');
    lookup.next({
      amount: LATEST_AMOUNT,
      currency: 'EUR',
      date: '2026-10-05',
      chargeCount: 2,
      cadence: 'annual',
    });
    expect(component.form.controls.cadence.value).toBe('monthly');
  });

  it('carries the chosen yearly cadence', () => {
    const {component, close} = setup();
    component.pick(CANDIDATE);
    component.setCadence('annual');
    component.submit();
    expect(close).toHaveBeenCalledWith(expect.objectContaining({cadence: 'annual'}));
  });

  it('links with only the picked transaction when opened for a legacy row', () => {
    const {component, close} = setup({linkTo: 'NetCup'});
    expect(component.isLinking).toBe(true);
    component.pick(CANDIDATE);
    component.form.patchValue({merchant: '', monthlyAmount: null});
    component.submit();
    expect(close).toHaveBeenCalledWith({transactionId: 'tx-1', cadence: 'monthly'});
  });

  it('reads the anchor for the kind of the row being linked', () => {
    const {component, getCommitmentAnchor} = setup({linkTo: 'Apple Store', kind: 'installment'});
    component.pick(CANDIDATE);
    expect(getCommitmentAnchor).toHaveBeenCalledWith('tx-1', 'installment');
  });

  it('links with the chosen cadence', () => {
    const {component, close} = setup({linkTo: 'NetCup'});
    component.pick(CANDIDATE);
    component.setCadence('annual');
    component.submit();
    expect(close).toHaveBeenCalledWith({transactionId: 'tx-1', cadence: 'annual'});
  });

  it('does not link until a transaction is picked', () => {
    const {component, close} = setup({linkTo: 'NetCup'});
    component.submit();
    expect(close).not.toHaveBeenCalled();
    expect(component.form.controls.transactionId.touched).toBe(true);
  });

  it('closes without a value on cancel', () => {
    const {component, close} = setup();
    component.cancel();
    expect(close).toHaveBeenCalledWith();
  });

  describe('single-choice controls', () => {
    function render(data?: CommitmentDialogData) {
      setup(data);
      const fixture = TestBed.createComponent(AddCommitmentDialogComponent);
      fixture.detectChanges();
      return {fixture, component: fixture.componentInstance};
    }

    function segmented(fixture: {nativeElement: HTMLElement}, label: string) {
      return fixture.nativeElement.querySelector<
        HTMLElement & {options: {label: string; value: string}[]; value: string}
      >(`lk-segmented[label="${label}"]`);
    }

    function choose(control: HTMLElement | null, value: string) {
      control?.dispatchEvent(new CustomEvent('lk-segmented-change', {detail: {value}}));
    }

    it('offers the kind as a segmented control on the current kind', () => {
      const {fixture} = render();
      const control = segmented(fixture, 'Type');

      expect(control?.options.map(o => o.value)).toEqual(['subscription', 'installment']);
      expect(control?.value).toBe('subscription');
    });

    it('changes the kind and re-reads the anchor when the control changes', () => {
      const {fixture, component} = render();
      const setKind = vi.spyOn(component, 'setKind');

      choose(segmented(fixture, 'Type'), 'installment');

      expect(setKind).toHaveBeenCalledWith('installment');
      expect(component.form.controls.kind.value).toBe('installment');
    });

    it('does not offer the kind when linking an existing row', () => {
      const {fixture} = render({linkTo: 'Apple Store', kind: 'installment'});

      expect(segmented(fixture, 'Type')).toBeNull();
    });

    it('offers the billing cadence once a charge is picked and applies the choice', () => {
      const {fixture, component} = render();
      expect(segmented(fixture, 'Billed')).toBeNull();

      component.pick(CANDIDATE);
      fixture.detectChanges();
      const control = segmented(fixture, 'Billed');
      choose(control, 'annual');

      expect(control?.options.map(o => o.value)).toEqual(['monthly', 'annual']);
      expect(component.form.controls.cadence.value).toBe('annual');
      expect(component.form.controls.cadence.dirty).toBe(true);
    });
  });
});
