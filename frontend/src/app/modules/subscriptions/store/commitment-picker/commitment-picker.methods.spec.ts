import {signalState} from '@ngrx/signals';
import {describe, expect, it} from 'vitest';

import {type CommitmentCandidate} from '../../models/commitment-candidate/commitment-candidate.model';
import {commitmentPickerMethods} from './commitment-picker.methods';
import {initialCommitmentPickerState} from './commitment-picker.state';

const CANDIDATE: CommitmentCandidate = {
  transactionId: 'tx-1',
  bankName: 'Test Bank',
  currency: 'EUR',
  amount: 9.99,
  date: '2026-09-01',
  description: 'ACME HOSTING',
  merchantName: 'Acme Hosting',
};

describe('commitmentPickerMethods', () => {
  it('setLoading marks loading and clears the error', () => {
    const state = signalState({...initialCommitmentPickerState, errorCode: 'X'});
    commitmentPickerMethods(state).setLoading();
    expect(state.status()).toBe('loading');
    expect(state.errorCode()).toBeNull();
  });

  it('setCandidates stores the list and goes idle', () => {
    const state = signalState(initialCommitmentPickerState);
    commitmentPickerMethods(state).setCandidates([CANDIDATE]);
    expect(state.candidates()).toEqual([CANDIDATE]);
    expect(state.status()).toBe('idle');
  });

  it('setError keeps the code', () => {
    const state = signalState(initialCommitmentPickerState);
    commitmentPickerMethods(state).setError('INVALID_SEARCH');
    expect(state.status()).toBe('error');
    expect(state.errorCode()).toBe('INVALID_SEARCH');
  });

  it('setSearch stores the term', () => {
    const state = signalState(initialCommitmentPickerState);
    commitmentPickerMethods(state).setSearch('acme');
    expect(state.search()).toBe('acme');
  });

  it('select keeps the pick even when the list changes', () => {
    const state = signalState(initialCommitmentPickerState);
    const methods = commitmentPickerMethods(state);
    methods.select(CANDIDATE);
    methods.setCandidates([]);
    expect(state.selected()).toEqual(CANDIDATE);
  });
});
