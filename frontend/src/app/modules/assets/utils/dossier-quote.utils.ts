import {MoneyUtils} from '../../../shared/utils/money.utils';
import {EQUITY_ASSET_CLASS} from '../constants/dossier/dossier-quote.constants';
import {
  type AssetDossierDto,
  type DossierDeltaDirection,
  type DossierQuoteDto,
  type DossierQuoteHeader,
} from '../models/dossier/dossier.model';

const CHANGE_FRACTION_DIGITS = 2;

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
}
