import {DialogRef} from '@angular/cdk/dialog';
import {TestBed} from '@angular/core/testing';
import {of} from 'rxjs';
import {describe, expect, it, vi} from 'vitest';

import {type CommitmentCandidate} from '../../models/commitment-candidate/commitment-candidate.model';
import {CommitmentCandidatesService} from '../../services/commitment-candidates.service';
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

function setup() {
  const close = vi.fn();
  TestBed.configureTestingModule({
    providers: [
      {provide: DialogRef, useValue: {close}},
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
    });
  });

  it('closes without a value on cancel', () => {
    const {component, close} = setup();
    component.cancel();
    expect(close).toHaveBeenCalledWith();
  });
});
