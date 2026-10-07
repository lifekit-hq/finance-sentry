import {describe, expect, it} from 'vitest';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {type EventKind, type FiredEventKind} from '../models/event/event.model';
import {EventDestinationUtils} from './event-destination.utils';

const FILING_URL = 'https://www.sec.gov/Archives/edgar/data/320193/000032019324000123/a.htm';

describe('EventDestinationUtils.upcoming', () => {
  it.each<EventKind>(['earnings', 'ex_dividend', 'filing_due', 'thesis_catalyst'])(
    'opens the dossier of a %s row ticker',
    kind => {
      expect(EventDestinationUtils.upcoming({kind, subject: 'AAPL'})).toEqual({
        kind: 'route',
        commands: [AppRoute.AssetDossier, 'AAPL'],
      });
    }
  );

  it('has no target for a macro row', () => {
    expect(EventDestinationUtils.upcoming({kind: 'macro', subject: 'AAPL'})).toBeNull();
  });

  it('has no target for a non-ticker subject', () => {
    expect(EventDestinationUtils.upcoming({kind: 'earnings', subject: 'US CPI'})).toBeNull();
  });
});

describe('EventDestinationUtils.fired', () => {
  it.each<FiredEventKind>(['EarningsAhead', 'NewsCluster', 'MarketStructure'])(
    'opens the dossier of the subject for %s',
    kind => {
      expect(EventDestinationUtils.fired({kind, message: 'm', subject: 'MU'})).toEqual({
        kind: 'route',
        commands: [AppRoute.AssetDossier, 'MU'],
      });
    }
  );

  it('opens the SEC document of a filing', () => {
    expect(
      EventDestinationUtils.fired({
        kind: 'FilingLanded',
        message: `MU filed. ${FILING_URL}`,
        subject: 'MU',
      })
    ).toEqual({kind: 'external', url: FILING_URL});
  });

  it('falls back to the dossier for a filing without a document URL', () => {
    expect(
      EventDestinationUtils.fired({kind: 'FilingLanded', message: 'MU filed.', subject: 'MU'})
    ).toEqual({kind: 'route', commands: [AppRoute.AssetDossier, 'MU']});
  });

  it('opens the budgets page for a budget breach', () => {
    expect(
      EventDestinationUtils.fired({kind: 'BudgetBreach', message: 'm', subject: 'Groceries'})
    ).toEqual({kind: 'route', commands: [AppRoute.Budgets]});
  });
});
