import {describe, expect, it} from 'vitest';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {ALERT_TYPE_META_REGISTRY} from '../constants/alert-type-meta.constants';
import {type Alert, type AlertType} from '../models/alert/alert.model';
import {AlertDestinationUtils} from './alert-destination.utils';

function makeAlert(overrides: Partial<Alert> = {}): Alert {
  return {
    id: 'a1',
    type: 'LowBalance',
    severity: 'Warning',
    title: 'Low balance',
    message: 'Balance dropped',
    referenceId: null,
    referenceLabel: null,
    isRead: false,
    isResolved: false,
    createdAt: '2026-10-01T09:00:00Z',
    resolvedAt: null,
    occurrenceCount: 1,
    lastOccurredAt: '2026-10-01T09:00:00Z',
    ...overrides,
  };
}

const FILING_URL =
  'https://www.sec.gov/Archives/edgar/data/320193/000032019324000123/aapl-20240630.htm';

describe('AlertDestinationUtils.resolve', () => {
  it('opens the ledger searched by the statement merchant for a duplicate charge, over the bare server path', () => {
    expect(
      AlertDestinationUtils.resolve(
        makeAlert({
          type: 'DuplicateCharge',
          message: 'Charged 2× for 9.99 EUR at Anthropic Ireland within the detection window.',
          referenceLabel: 'claude',
          appPath: '/transactions',
        })
      )
    ).toEqual({kind: 'url', url: '/transactions?q=Anthropic%20Ireland'});
  });

  it('falls back to the plain ledger for a duplicate charge whose message names no merchant', () => {
    expect(
      AlertDestinationUtils.resolve(
        makeAlert({type: 'DuplicateCharge', message: 'Balance dropped', referenceLabel: 'claude'})
      )
    ).toEqual({kind: 'route', commands: [AppRoute.Transactions]});
  });

  it('prefers the server appPath, query string included, over the type destination', () => {
    const appPath = '/transactions?account=acc-1';
    expect(AlertDestinationUtils.resolve(makeAlert({type: 'LowBalance', appPath}))).toEqual({
      kind: 'url',
      url: appPath,
    });
  });

  it('uses appPath even for a type with no fallback destination', () => {
    expect(
      AlertDestinationUtils.resolve(makeAlert({type: 'JobFailure', appPath: '/dashboard'}))
    ).toEqual({kind: 'url', url: '/dashboard'});
  });

  it.each([null, undefined, '', '  ', 'https://evil.example/x', '//evil.example/x'])(
    'falls back to the type destination when appPath is %j',
    appPath => {
      expect(AlertDestinationUtils.resolve(makeAlert({type: 'PriceHike', appPath}))).toEqual({
        kind: 'route',
        commands: [AppRoute.Subscriptions],
      });
    }
  );

  it("opens a filing alert's sec.gov document externally", () => {
    const target = AlertDestinationUtils.resolve(
      makeAlert({
        type: 'FilingLanded',
        referenceLabel: 'AAPL',
        message: `AAPL filed. ${FILING_URL}`,
      })
    );
    expect(target).toEqual({kind: 'external', url: FILING_URL});
  });

  it('falls back to the dossier when a filing alert has no document URL', () => {
    const target = AlertDestinationUtils.resolve(
      makeAlert({type: 'FilingLanded', referenceLabel: 'AAPL', message: 'AAPL filed.'})
    );
    expect(target).toEqual({kind: 'route', commands: [AppRoute.AssetDossier, 'AAPL']});
  });

  it.each<[AlertType, AppRoute]>([
    ['LowBalance', AppRoute.AccountsList],
    ['SyncFailure', AppRoute.AccountsList],
    ['CashShortfall', AppRoute.AccountsList],
    ['ConsentExpiring', AppRoute.AccountsList],
    ['FamilyStatement', AppRoute.AccountsList],
    ['FireBrief', AppRoute.Dashboard],
    ['UnusualSpend', AppRoute.Transactions],
    ['CategorySpike', AppRoute.Transactions],
    ['DuplicateCharge', AppRoute.Transactions],
    ['FxSpread', AppRoute.Transactions],
    ['PriceHike', AppRoute.Subscriptions],
    ['BudgetBreach', AppRoute.Budgets],
    ['PolicyViolation', AppRoute.AccountsInvestments],
    ['RebalanceProposal', AppRoute.AccountsInvestments],
    ['CashSweepProposal', AppRoute.AccountsInvestments],
    ['PolicyReview', AppRoute.AccountsInvestments],
    ['PolicyReviewMissed', AppRoute.AccountsInvestments],
    ['PerformanceBrief', AppRoute.AccountsInvestments],
    ['RelativeUnderperformance', AppRoute.AccountsInvestments],
  ])('routes %s to its page', (type, route) => {
    expect(AlertDestinationUtils.resolve(makeAlert({type}))).toEqual({
      kind: 'route',
      commands: [route],
    });
  });

  it.each<AlertType>([
    'ThesisBroken',
    'MarketStructure',
    'Opportunity',
    'EarningsAhead',
    'NewsCluster',
    'AnalystRatingChange',
  ])('routes %s to the dossier of its ticker', type => {
    expect(AlertDestinationUtils.resolve(makeAlert({type, referenceLabel: 'BRK.B'}))).toEqual({
      kind: 'route',
      commands: [AppRoute.AssetDossier, 'BRK.B'],
    });
  });

  it('does not open a dossier for a non-ticker label (radar feed freshness)', () => {
    expect(
      AlertDestinationUtils.resolve(
        makeAlert({type: 'MarketStructure', referenceLabel: 'freshness'})
      )
    ).toBeNull();
    expect(
      AlertDestinationUtils.resolve(makeAlert({type: 'Opportunity', referenceLabel: null}))
    ).toBeNull();
  });

  it('leaves job failures and unknown types as mark-read only', () => {
    expect(AlertDestinationUtils.resolve(makeAlert({type: 'JobFailure'}))).toBeNull();
    expect(
      AlertDestinationUtils.resolve(makeAlert({type: 'SomethingNew' as AlertType}))
    ).toBeNull();
  });

  it('gives every registered type a destination', () => {
    for (const type of Object.keys(ALERT_TYPE_META_REGISTRY) as AlertType[]) {
      expect(() =>
        AlertDestinationUtils.resolve(makeAlert({type, referenceLabel: 'AAPL'}))
      ).not.toThrow();
    }
  });
});

describe('AlertDestinationUtils.dossier', () => {
  it('opens the dossier of a ticker, trimmed', () => {
    expect(AlertDestinationUtils.dossier(' BRK.B ')).toEqual({
      kind: 'route',
      commands: [AppRoute.AssetDossier, 'BRK.B'],
    });
  });

  it('returns null for a non-ticker, blank or missing label', () => {
    expect(AlertDestinationUtils.dossier('US CPI')).toBeNull();
    expect(AlertDestinationUtils.dossier('  ')).toBeNull();
    expect(AlertDestinationUtils.dossier(null)).toBeNull();
  });
});
