export interface DossierTaxLotEntry {
  quantity: number;
  currentValueUsd: number;
  averageCostUsd: Nullable<number>;
  costBasisUsd: Nullable<number>;
  unrealizedPnlUsd: Nullable<number>;
  unrealizedPnlPercent: Nullable<number>;
  acquiredAt: Nullable<string>;
  isLongTerm: boolean;
}

export interface DossierPositionSection {
  provider: string;
  quantity: number;
  currentValueUsd: number;
  costBasisUsd: Nullable<number>;
  unrealizedPnlUsd: Nullable<number>;
  unrealizedPnlPercent: Nullable<number>;
  taxLots: DossierTaxLotEntry[];
}

export interface ThesisDataPoint {
  metric: string;
  direction: string;
  threshold: number;
  source: string;
}

export interface ThesisCatalyst {
  date: string;
  event: string;
}

export interface ThesisInvalidationTrigger {
  metric: string;
  direction: string;
  threshold: number;
  proxyTicker: Nullable<string>;
  consecutivePeriods: number;
  periodType: string;
}

export interface ThesisDto {
  id: string;
  ticker: string;
  thesisText: string;
  keyDataPoints: ThesisDataPoint[];
  catalysts: ThesisCatalyst[];
  invalidationTriggers: ThesisInvalidationTrigger[];
  createdAt: string;
  updatedAt: string;
  brokenAt: Nullable<string>;
  brokenReason: Nullable<string>;
  entryPrice: Nullable<number>;
}

export interface MetricValue {
  value: Nullable<number>;
  fiveYearAvg: Nullable<number>;
  historyWindowYears: Nullable<number>;
  historyUnavailable: boolean;
}

export interface ValuationMetricsDto {
  trailingPe: MetricValue;
  forwardPe: MetricValue;
  evToEbitda: MetricValue;
  dividendYield: MetricValue;
}

export interface ValuationPeer {
  ticker: string;
  forwardPe: Nullable<number>;
  evToEbitda: Nullable<number>;
}

export interface ValuationPeerSet {
  name: string;
  peers: ValuationPeer[];
}

export interface ValuationSnapshotDto {
  ticker: string;
  notApplicable: boolean;
  price: Nullable<number>;
  isStale: boolean;
  metrics: ValuationMetricsDto;
  consensusTarget: Nullable<number>;
  impliedUpsidePct: Nullable<number>;
  peerSet: Nullable<ValuationPeerSet>;
  sources: string[];
  retrievedAt: string;
}

export interface AnalystActionDto {
  ticker: string;
  firm: string;
  actionType: string;
  priorRating: Nullable<string>;
  newRating: Nullable<string>;
  priorTarget: Nullable<number>;
  newTarget: Nullable<number>;
  actionDate: string;
  source: string;
  sourceUrl: Nullable<string>;
  ingestedAt: string;
}

export interface RecommendationTrendDto {
  period: string;
  strongBuy: number;
  buy: number;
  hold: number;
  sell: number;
  strongSell: number;
  source: string;
  ingestedAt: string;
}

export interface DossierAnalystsSection {
  recentActions: AnalystActionDto[];
  trends: RecommendationTrendDto[];
  coverage: string;
}

export interface NewsArticleDto {
  id: string;
  source: string;
  title: string;
  url: string;
  summary: Nullable<string>;
  tickers: string[];
  categories: string[];
  publishedAt: string;
}

export interface EarningsEventDto {
  ticker: string;
  eventType: string;
  eventDate: string;
  isEstimate: boolean;
  source: string;
}

export interface DossierSignalItem {
  timestamp: string;
  scanner: string;
  signalType: string;
  severity: string;
  payload: Record<string, unknown>;
}

export interface AssetDossierDto {
  symbol: string;
  position: Nullable<DossierPositionSection>;
  thesis: Nullable<ThesisDto>;
  valuation: Nullable<ValuationSnapshotDto>;
  analysts: Nullable<DossierAnalystsSection>;
  recentNews: NewsArticleDto[];
  nextEarnings: Nullable<EarningsEventDto>;
  radarSignals: DossierSignalItem[];
  generatedAt: string;
}

/**
 * "Ledger's read" for a ticker (feature 421, US3). `narrative` is null until one is generated.
 * `isStale` = older than a day or the dossier facts moved since; the copy still renders, flagged.
 * `cached` = served from the server-side cache rather than freshly generated.
 */
export interface AssetLedgerReadDto {
  symbol: string;
  narrative: Nullable<string>;
  generatedAt: Nullable<string>;
  isStale: boolean;
  cached: boolean;
}

/** One entry of `research/quotes`; `changePct` is already a percent (1.5 = +1.5%). */
export interface DossierQuoteDto {
  ticker: string;
  price: number;
  previousClose: Nullable<number>;
  changePct: Nullable<number>;
  currency: string;
}

export type DossierDeltaDirection = 'up' | 'down' | 'flat';

export interface DossierQuoteHeader {
  priceText: string;
  /** Null when the quote has no previous close to measure the day against. */
  changeText: Nullable<string>;
  direction: DossierDeltaDirection;
}
