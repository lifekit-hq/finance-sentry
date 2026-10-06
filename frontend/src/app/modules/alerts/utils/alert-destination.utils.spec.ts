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
