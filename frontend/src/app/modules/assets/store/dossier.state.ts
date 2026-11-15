import {type PricePeriod} from '@lifekit-hq/charts-core';
import {type Nullable} from '@lifekit-hq/ui';

import {type RecommendationDto} from '../../../shared/models/recommendation/recommendation.model';
import {type ThesisRevisionDto} from '../../../shared/models/thesis-revision/thesis-revision.model';
import {
  type AssetChartDto,
  type AssetDossierDto,
  type AssetLedgerReadDto,
  type DossierQuoteDto,
} from '../models/dossier/dossier.model';

export interface DossierState {
  dossier: Nullable<AssetDossierDto>;
  ledgerRead: Nullable<AssetLedgerReadDto>;
  isThesisExpanded: boolean;
  quote: Nullable<DossierQuoteDto>;
  chart: Nullable<AssetChartDto>;
  /** The price block's selected period; the S&P 500 overlay depends on it. */
  chartPeriod: PricePeriod;
  /** The target editor's text; parsed and validated in the computeds. */
  targetDraft: string;
  isEditingTarget: boolean;
  /** The proposal decided on this page, kept so the card's place shows the verdict rather than going blank. */
  decidedRecommendation: Nullable<RecommendationDto>;
  /** Ledger's pending thesis change for this ticker, which only the owner can confirm; null when none or not read. */
  pendingRevision: Nullable<ThesisRevisionDto>;
  /** The thesis change decided on this page, kept so the card's place shows the verdict rather than going blank. */
  decidedRevision: Nullable<ThesisRevisionDto>;
}

export const initialDossierState: DossierState = {
  dossier: null,
  ledgerRead: null,
  isThesisExpanded: false,
  quote: null,
  chart: null,
  chartPeriod: '1Y',
  targetDraft: '',
  isEditingTarget: false,
  decidedRecommendation: null,
  pendingRevision: null,
  decidedRevision: null,
};
