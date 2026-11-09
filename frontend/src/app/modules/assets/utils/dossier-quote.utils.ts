import {type Nullable, type VerdictChange} from '@lifekit-hq/ui';

import {MoneyUtils} from '../../../shared/utils/money.utils';
import {
  EQUITY_ASSET_CLASS,
  MINOR_UNIT_CURRENCIES,
  MINOR_UNITS_PER_MAJOR,
} from '../constants/dossier/dossier-quote.constants';
import {
  type AssetDossierDto,
  type DossierDeltaDirection,
  type DossierQuoteDto,
  type DossierQuoteHeader,
} from '../models/dossier/dossier.model';

const CHANGE_FRACTION_DIGITS = 2;
const PERCENT_PER_RATIO = 100;

export class DossierQuoteUtils {
  /**
   * Research quotes price listed equities by the ticker as given, so crypto and cash symbols
   * (`BTC`, `USD`) would come back as unrelated ETFs. A holding decides by its asset class;
   * anything else needs the valuation source to have priced the ticker as an equity.
   */
  public static isQuotable(dossier: AssetDossierDto): boolean {
    if (dossier.position) {
      return dossier.position.assetClass === EQUITY_ASSET_CLASS;
    }
    return (
      dossier.valuation !== null &&
      !dossier.valuation.notApplicable &&
      dossier.valuation.price !== null
    );
  }

  /**
   * The shared quote endpoint answers in the unit the exchange quotes (1250 GBp), while the dossier's
   * valuation, performance and user target are in the major currency (12.50 GBP). A minor-unit quote is
   * scaled and relabelled here so the price, the analyst band and the targets share one unit.
   */
  public static toMajorUnits(quote: Nullable<DossierQuoteDto>): Nullable<DossierQuoteDto> {
    const major = quote ? MINOR_UNIT_CURRENCIES.get(quote.currency.trim()) : undefined;
    if (!quote || major === undefined) {
      return quote;
    }
    const scale = (value: Nullable<number>): Nullable<number> =>
      value === null ? null : value / MINOR_UNITS_PER_MAJOR;
    return {
      ...quote,
      price: quote.price / MINOR_UNITS_PER_MAJOR,
      previousClose: scale(quote.previousClose),
      currency: major,
    };
  }

  /** "$189.30" with "+1.25%"; a quote without a usable price yields null so the slot stays empty. */
  public static toHeader(quote: Nullable<DossierQuoteDto>): Nullable<DossierQuoteHeader> {
    if (!quote || !Number.isFinite(quote.price) || quote.price <= 0) {
      return null;
    }
    const priceText = MoneyUtils.format(quote.price, quote.currency);
    const changePct = quote.changePct;
    if (changePct === null || changePct === undefined || !Number.isFinite(changePct)) {
      return {priceText, changeText: null, direction: 'flat'};
    }
    const rounded = Number(changePct.toFixed(CHANGE_FRACTION_DIGITS));
    const direction: DossierDeltaDirection = rounded > 0 ? 'up' : rounded < 0 ? 'down' : 'flat';
    const sign = direction === 'up' ? '+' : direction === 'down' ? '-' : '';
    const magnitude = Math.abs(rounded).toFixed(CHANGE_FRACTION_DIGITS);
    return {priceText, changeText: `${sign}${magnitude}%`, direction};
  }

  /**
   * The day's move as the verdict header draws it: the percent as a ratio and the amount in the quote's
   * currency. The amount is the gap to the previous close; when the quote has none it is what the percent
   * implies of today's price. Null when the quote has no usable price or no percent to measure the day by.
   */
  public static toChange(quote: Nullable<DossierQuoteDto>): Nullable<VerdictChange> {
    if (!quote || !Number.isFinite(quote.price) || quote.price <= 0) {
      return null;
    }
    const changePct = quote.changePct;
    if (changePct === null || changePct === undefined || !Number.isFinite(changePct)) {
      return null;
    }
    const percent = changePct / PERCENT_PER_RATIO;
    if (quote.previousClose !== null && Number.isFinite(quote.previousClose)) {
      return {percent, amount: quote.price - quote.previousClose};
    }
    // A move of -100% leaves no price to derive a previous close from.
    return percent > -1 ? {percent, amount: quote.price - quote.price / (1 + percent)} : null;
  }
}
