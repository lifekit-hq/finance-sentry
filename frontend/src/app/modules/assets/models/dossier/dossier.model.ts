import {type RecommendationDto} from '../../../../shared/models/recommendation/recommendation.model';

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
  /** Canonical asset class (`Equities`, `Crypto`, `Bonds`, ...). */
  assetClass: string;
  quantity: number;
  currentValueUsd: number;
  costBasisUsd: Nullable<number>;
  unrealizedPnlUsd: Nullable<number>;
  unrealizedPnlPercent: Nullable<number>;
  /** Share of the whole book total (cash included, the holdings-weight denominator), in percent. */
  shareOfBookPercent: Nullable<number>;
  taxLots: DossierTaxLotEntry[];
  /** The IPS single-position cap, in percent; null when the IPS sets none. */
  positionCapPercent?: Nullable<number>;
  /** Where the return came from (money-semantics §1); null when the ledger could not be read. */
  returns?: Nullable<PositionReturnsDto>;
}

/** `complete` = the ingested fills explain the whole holding; `partial` / `unavailable` = they do not. */
export type ReturnsCoverage = 'complete' | 'partial' | 'unavailable';

/** Return decomposition in USD; a component is null when the ledger cannot support it, never zero-filled. */
export interface PositionReturnsDto {
  unrealizedUsd: Nullable<number>;
  realizedUsd: Nullable<number>;
  dividendsUsd: Nullable<number>;
  currencyUsd: Nullable<number>;
  totalUsd: Nullable<number>;
  coverage: ReturnsCoverage;
  averageCostNative: Nullable<number>;
  costCurrency: Nullable<string>;
  fillCount: number;
}

/** The #824 performance strip, read from daily closes in `currency`. */
export interface DossierPerformanceDto {
  lastClose: Nullable<number>;
  yearChangePct: Nullable<number>;
  high52Week: Nullable<number>;
  low52Week: Nullable<number>;
  fromHighPct: Nullable<number>;
  benchmarkYearChangePct: Nullable<number>;
  currency: Nullable<string>;
  asOf: Nullable<string>;
}

export type DossierCheckUnit = 'percent' | 'multiple' | 'none';

/** One named rule: `passed` null = not evaluated (`note` says why); never a black-box score. */
export interface DossierCheckRuleDto {
  key: string;
  label: string;
  passed: Nullable<boolean>;
  value: Nullable<number>;
  threshold: Nullable<number>;
  unit: DossierCheckUnit;
  note: Nullable<string>;
}

export type DossierCheckAxisKey = 'analysts' | 'growth' | 'value' | 'momentum' | 'fit';

export interface DossierCheckAxisDto {
  key: DossierCheckAxisKey;
  label: string;
  passed: number;
  evaluated: number;
  rules: DossierCheckRuleDto[];
}

export interface DossierChecksDto {
  axes: DossierCheckAxisDto[];
}

/** The user's own fair-value target, in the quote currency it was set in. */
export interface DossierUserTargetDto {
  targetPrice: number;
  currency: Nullable<string>;
  updatedAt: string;
}

export type ChartRangeId = '1M' | '6M' | 'YTD' | '1Y' | '5Y' | 'MAX';

export interface AssetChartPointDto {
  date: string;
  close: number;
  /** The S&P 500 rebased onto the asset's close on the index's first date; null where it has no close yet. */
  benchmark: Nullable<number>;
}

export interface AssetChartTradeDto {
  date: string;
  side: 'buy' | 'sell';
  quantity: number;
  price: number;
  currency: string;
}

/** GET /research/assets/{symbol}/chart: closes in `currency`, an empty `points` = no history. */
export interface AssetChartDto {
  symbol: string;
  range: ChartRangeId;
  currency: Nullable<string>;
  points: AssetChartPointDto[];
  benchmarkLabel: string;
  trades: AssetChartTradeDto[];
  averageCost: Nullable<number>;
  changePct: Nullable<number>;
  benchmarkChangePct: Nullable<number>;
  source: string;
  retrievedAt: string;
}

export interface SetPriceTargetRequest {
  targetPrice: number;
  currency: Nullable<string>;
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
  /** Company profile the valuation source returns with the metrics; null when it gave none. */
  name: Nullable<string>;
  sector: Nullable<string>;
  industry: Nullable<string>;
  /** When the quote path last saw `price`; null when unknown. */
  priceAsOf: Nullable<string>;
  /** The analyst target range behind `consensusTarget` (the mean). */
  targetLow?: Nullable<number>;
  targetHigh?: Nullable<number>;
  analystCount?: Nullable<number>;
  /** Quote currency of every price in the snapshot. */
  currency?: Nullable<string>;
  marketCap?: Nullable<number>;
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
  /** Null = no price history. */
  performance?: Nullable<DossierPerformanceDto>;
  checks?: Nullable<DossierChecksDto>;
  userTarget?: Nullable<DossierUserTargetDto>;
  /** Ledger's pending proposal on this ticker (#1069), awaiting the owner's Confirm or Reject. */
  pendingRecommendation?: Nullable<RecommendationDto>;
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

export type RecommendationBucket = 'strongBuy' | 'buy' | 'hold' | 'sell' | 'strongSell';

export interface RecommendationBucketStyle {
  bucket: RecommendationBucket;
  label: string;
  /** Tailwind background of the bar segment. */
  barClass: string;
}

/** One slice of the analyst recommendation bar. `sharePct` is of the month's analyst total. */
export interface RecommendationSegment {
  bucket: RecommendationBucket;
  label: string;
  count: number;
  sharePct: number;
  /** Tailwind background of the slice, carried so the template needs no lookup. */
  barClass: string;
}

/**
 * The latest month of the recommendation trend as one verdict: the winning side (Buy = strong buy
 * + buy, Sell = sell + strong sell) and how many of the `total` analysts hold it.
 */
export interface RecommendationSummary {
  period: string;
  total: number;
  verdict: string;
  verdictCount: number;
  segments: RecommendationSegment[];
}

/** A section of the dossier the sticky chip bar can jump to. */
export interface DossierSectionChip {
  id: string;
  label: string;
}

/** The hero's identity lines: the company name when the source gave one, then what it is. */
export interface DossierHeroProfile {
  name: Nullable<string>;
  detail: Nullable<string>;
}
