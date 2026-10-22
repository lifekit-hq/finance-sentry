import {type Nullable} from '@lifekit-hq/ui';

import {MoneyUtils} from './money.utils';

/** Below this a quantity is dust: a per-unit figure over it would be noise, so none is stated. */
const MIN_UNIT_QUANTITY = 1e-9;
/** A unit worth less than one currency unit (most altcoins) keeps this many decimals, not cents. */
const SUB_UNIT_FRACTION_DIGITS = 8;

export class UnitPriceUtils {
  /**
   * A total spread over the units held (value / quantity, cost basis / quantity); null when the total
   * is unknown or the quantity is zero, near zero or not a number.
   */
  public static perUnit(total: Nullable<number> | undefined, quantity: number): Nullable<number> {
    if (total === null || total === undefined || !Number.isFinite(total)) {
      return null;
    }
    if (!Number.isFinite(quantity) || Math.abs(quantity) < MIN_UNIT_QUANTITY) {
      return null;
    }
    return total / quantity;
  }

  /**
   * The average purchase price per unit: the provider's own average when it sends one, else the cost
   * basis spread over the units. Null when neither is known; never derived from the current price.
   */
  public static average(
    averagePrice: Nullable<number> | undefined,
    costBasis: Nullable<number> | undefined,
    quantity: number
  ): Nullable<number> {
    if (averagePrice !== null && averagePrice !== undefined && Number.isFinite(averagePrice)) {
      return averagePrice;
    }
    return UnitPriceUtils.perUnit(costBasis, quantity);
  }

  /**
   * A per-unit price as money: cents from one currency unit up, more decimals below it so a $0.00004
   * token never reads "$0.00"; an unknown price is the em dash.
   */
  public static format(price: Nullable<number> | undefined, currency?: Nullable<string>): string {
    const subUnit = price !== null && price !== undefined && Math.abs(price) < 1;
    return MoneyUtils.format(
      price,
      currency,
      subUnit ? {maxFractionDigits: SUB_UNIT_FRACTION_DIGITS} : {}
    );
  }
}
