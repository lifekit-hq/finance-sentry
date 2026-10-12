import {describe, expect, it} from 'vitest';

import {type NetWorthSnapshotDto} from '../models/dashboard/dashboard.model';
import {NetWorthSplitUtils} from './net-worth-split.utils';

const snapshot = (split: Partial<NetWorthSnapshotDto> = {}): NetWorthSnapshotDto => ({
  snapshotDate: '2026-08-01',
  bankingTotal: 0,
  brokerageTotal: 0,
  cryptoTotal: 0,
  totalNetWorth: 10_000,
  currency: 'USD',
  ...split,
});

describe('NetWorthSplitUtils', () => {
  describe('of', () => {
    it('reads the split and sums the invested parts', () => {
      expect(
        NetWorthSplitUtils.of(
          snapshot({cashTotal: 4_000, brokerageInvested: 5_000, cryptoInvested: 1_000})
        )
      ).toEqual({cash: 4_000, brokerageInvested: 5_000, cryptoInvested: 1_000, invested: 6_000});
    });

    it('keeps a zero or negative part: zero invested and net card debt are real values', () => {
      expect(
        NetWorthSplitUtils.of(snapshot({cashTotal: -200, brokerageInvested: 0, cryptoInvested: 0}))
      ).toEqual({cash: -200, brokerageInvested: 0, cryptoInvested: 0, invested: 0});
    });

    it('is null when the fields are absent', () => {
      expect(NetWorthSplitUtils.of(snapshot())).toBeNull();
    });

    it.each([
      ['cashTotal', {cashTotal: null, brokerageInvested: 1, cryptoInvested: 1}],
      ['brokerageInvested', {cashTotal: 1, brokerageInvested: null, cryptoInvested: 1}],
      ['cryptoInvested', {cashTotal: 1, brokerageInvested: 1, cryptoInvested: null}],
    ])('is null when %s is null', (_field, split) => {
      expect(NetWorthSplitUtils.of(snapshot(split))).toBeNull();
    });
  });
});
