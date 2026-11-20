import {describe, expect, it} from 'vitest';

import {type RiskPicture} from '../models/risk-picture/risk-picture.model';
import {RiskPictureUtils} from './risk-picture.utils';

const PICTURE: RiskPicture = {
  from: '2026-01-31',
  to: '2026-09-30',
  monthsObserved: 8,
  betaMonths: 7,
  volatility: {value: 0.182, reason: null},
  maxDrawdown: {value: 0.214, reason: null},
  beta: {value: 1.314, reason: null},
  drawdownTolerance: 0.2,
  top1Symbol: 'DRAM',
  top1Weight: 0.378,
  top3Symbols: ['DRAM', 'BTC'],
  top3Weight: 0.6,
  cryptoShare: 0.19,
  positionFlag: 0.3,
  top1OverFlag: true,
};

describe('RiskPictureUtils.rows', () => {
  it('gives every row an explanation and a basis', () => {
    for (const row of RiskPictureUtils.rows(PICTURE)) {
      expect(row.explanation, row.key).not.toBe('');
      expect(row.basis, row.key).not.toBe('');
    }
  });

  it('warns on a drawdown past the tolerance and on a position past the flag', () => {
    const rows = new Map(RiskPictureUtils.rows(PICTURE).map(r => [r.key, r]));

    expect(rows.get('max-drawdown')?.warning).toBe(true);
    expect(rows.get('top-1')?.warning).toBe(true);
    expect(rows.get('volatility')?.warning).toBe(false);
  });

  it('never warns on a drawdown when the tolerance is not set', () => {
    const rows = RiskPictureUtils.rows({...PICTURE, drawdownTolerance: null});

    expect(rows.find(r => r.key === 'max-drawdown')?.warning).toBe(false);
  });

  it('states the observed months in the basis', () => {
    const beta = RiskPictureUtils.rows(PICTURE).find(r => r.key === 'beta');

    expect(beta?.value).toBe('1.31');
    expect(beta?.basis).toContain('7 months');
  });
});

describe('RiskPictureUtils.rows failure reasons', () => {
  const row = (picture: RiskPicture, key: string) =>
    RiskPictureUtils.rows(picture).find(r => r.key === key);

  it('blames the benchmark only on beta, never on a book figure that fails on invalid input', () => {
    const invalid = {value: null, reason: 'InvalidInput'};
    const picture = {...PICTURE, volatility: invalid, maxDrawdown: invalid, beta: invalid};

    expect(row(picture, 'beta')?.value).toBe('the benchmark did not move');
    expect(row(picture, 'volatility')?.value).toBe('not measured');
    expect(row(picture, 'max-drawdown')?.value).toContain('not measured');
    expect(row(picture, 'max-drawdown')?.value).not.toContain('benchmark');
  });
});

