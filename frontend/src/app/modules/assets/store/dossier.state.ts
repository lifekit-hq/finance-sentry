import {type Nullable} from '@lifekit-hq/ui';

import {type RecommendationDto} from '../../../shared/models/recommendation/recommendation.model';
import {DEFAULT_CHART_RANGE} from '../constants/dossier/dossier-chart.constants';
import {
  type AssetChartDto,
  type AssetDossierDto,
  type AssetLedgerReadDto,
  type ChartRangeId,
  type DossierQuoteDto,
} from '../models/dossier/dossier.model';

export interface DossierState {
  dossier: Nullable<AssetDossierDto>;
  ledgerRead: Nullable<AssetLedgerReadDto>;
  isThesisExpanded: boolean;
  quote: Nullable<DossierQuoteDto>;
  chart: Nullable<AssetChartDto>;
  chartRange: ChartRangeId;
  /** The target editor's text; parsed and validated in the computeds. */
  targetDraft: string;
  isEditingTarget: boolean;
  /** The proposal decided on this page, kept so the card's place shows the verdict rather than going blank. */
  decidedRecommendation: Nullable<RecommendationDto>;
}

export const initialDossierState: DossierState = {
  dossier: null,
  ledgerRead: null,
  isThesisExpanded: false,
  quote: null,
  chart: null,
  chartRange: DEFAULT_CHART_RANGE,
  targetDraft: '',
  isEditingTarget: false,
  decidedRecommendation: null,
};
