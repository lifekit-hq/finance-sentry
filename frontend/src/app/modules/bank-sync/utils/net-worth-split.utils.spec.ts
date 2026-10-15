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
      ).toEqual({
        cash: 4_000,
        brokerageInvested: 5_000,
        cryptoInvested: 1_000,
        otherInvested: 0,
        invested: 6_000,
      });
    });

    it('counts the invested value of a source outside the three sleeves', () => {
      expect(
        NetWorthSplitUtils.of(
          snapshot({
            cashTotal: 4_000,
            brokerageInvested: 5_000,
            cryptoInvested: 1_000,
            otherInvested: 700,
          })
        )
      ).toEqual({
        cash: 4_000,
        brokerageInvested: 5_000,
        cryptoInvested: 1_000,
        otherInvested: 700,
        invested: 6_700,
      });
    });

    it('keeps a zero or negative part: zero invested and net card debt are real values', () => {
      expect(
        NetWorthSplitUtils.of(snapshot({cashTotal: -200, brokerageInvested: 0, cryptoInvested: 0}))
      ).toEqual({
        cash: -200,
        brokerageInvested: 0,
        cryptoInvested: 0,
        otherInvested: 0,
        invested: 0,
      });
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

  describe('otherSourcesTotal', () => {
    const source = (sourceId: string, total: number | null) => ({
      sourceId,
      kind: 'Bank',
      total,
      health: 'COMPLETE' as const,
      reasons: [],
    });

    it('sums the sources outside the three sleeves', () => {
      expect(
        NetWorthSplitUtils.otherSourcesTotal(
          snapshot({
            sources: [
              source('banking', 100),
              source('brokerage', 200),
              source('fund', 700),
              source('property', 50),
            ],
          })
        )
      ).toBe(750);
    });

    it('is zero for a row with only the sleeves, with no sources, and for an unread source', () => {
      expect(
        NetWorthSplitUtils.otherSourcesTotal(
          snapshot({sources: [source('banking', 100), source('fund', null)]})
        )
      ).toBe(0);
      expect(NetWorthSplitUtils.otherSourcesTotal(snapshot())).toBe(0);
    });
  });
});
