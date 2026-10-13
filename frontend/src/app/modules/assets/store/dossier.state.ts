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
};
