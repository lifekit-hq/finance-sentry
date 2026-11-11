import {type Nullable} from '@lifekit-hq/ui';

import {type Headline} from '../../../shared/models/headline/headline.model';
import {type ScorecardVerdictSummary} from '../../../shared/models/scorecard/scorecard.model';
import {type Position} from '../models/position/position.model';

export interface HoldingsState {
  positions: Position[];
  /** Typed record of the positions' total: quality, reasons, and a failed feed as STALE_SOURCE. */
  headline: Nullable<Headline>;
  /** Day change % per ticker (upper-cased). A missing key means no quote; never cleared on refresh. */
  dayChangePctByTicker: Record<string, number>;
  /** Verdict word and what turns it BUY per held equity ticker (upper-cased), owner only. A missing key means no chip. */
  verdictByTicker: Record<string, ScorecardVerdictSummary>;
}

export const initialHoldingsState: HoldingsState = {
  positions: [],
  headline: null,
  dayChangePctByTicker: {},
  verdictByTicker: {},
};
