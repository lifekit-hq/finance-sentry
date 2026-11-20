import {type Nullable} from '@lifekit-hq/ui';

import {
  RISK_BASIS,
  RISK_BETA_DIGITS,
  RISK_BETA_REASON_LABELS,
  RISK_EXPLANATIONS,
  RISK_PERCENT_DIGITS,
  RISK_PERCENT_POINTS,
  RISK_REASON_FALLBACK,
  RISK_REASON_LABELS,
  RISK_TOLERANCE_NOT_SET,
} from '../constants/risk-picture/risk-picture.constants';
import {
  type RiskFigure,
  type RiskFigureRow,
  type RiskPicture,
} from '../models/risk-picture/risk-picture.model';

export class RiskPictureUtils {
  /** The panel's rows, in reading order. Every row carries its explanation and its basis, measured or not. */
  public static rows(picture: RiskPicture): RiskFigureRow[] {
    const months = RiskPictureUtils.monthsBasis(picture);
    return [
      {
        key: 'volatility',
        label: 'Volatility',
        value: RiskPictureUtils.rate(picture.volatility),
        explanation: RISK_EXPLANATIONS.volatility,
        basis: `${RISK_BASIS.returns}, ${months}, annualised.`,
        warning: false,
      },
      {
        key: 'max-drawdown',
        label: 'Maximum drawdown',
        value: RiskPictureUtils.drawdown(picture),
        explanation: RISK_EXPLANATIONS.maxDrawdown,
        basis: `${RISK_BASIS.returns}, ${months}.`,
        warning: RiskPictureUtils.drawdownBreached(picture),
      },
      {
        key: 'beta',
        label: 'Beta vs SPY',
        value: RiskPictureUtils.beta(picture.beta),
        explanation: RISK_EXPLANATIONS.beta,
        basis: `${RISK_BASIS.beta}: ${picture.betaMonths} ${RiskPictureUtils.plural(picture.betaMonths)}.`,
        warning: false,
      },
      {
        key: 'top-1',
        label: 'Largest position',
        value: RiskPictureUtils.weight(picture.top1Weight, picture.top1Symbol),
        explanation: RISK_EXPLANATIONS.top1,
        basis: `${RISK_BASIS.weights}. ${RiskPictureUtils.flagBasis(picture.positionFlag)}`,
        warning: picture.top1OverFlag === true,
      },
      {
        key: 'top-3',
        label: 'Top three positions',
        value: RiskPictureUtils.weight(picture.top3Weight, picture.top3Symbols.join(', ')),
        explanation: RISK_EXPLANATIONS.top3,
        basis: `${RISK_BASIS.weights}.`,
        warning: false,
      },
      {
        key: 'crypto',
        label: 'Crypto share',
        value: RiskPictureUtils.weight(picture.cryptoShare, null),
        explanation: RISK_EXPLANATIONS.crypto,
        basis: `${RISK_BASIS.weights}.`,
        warning: false,
      },
    ];
  }

  private static percent(fraction: number): string {
    return `${(fraction * RISK_PERCENT_POINTS).toFixed(RISK_PERCENT_DIGITS)}%`;
  }

  private static reason(
    figure: RiskFigure,
    labels: ReadonlyMap<string, string> = RISK_REASON_LABELS
  ): string {
    return labels.get(figure.reason ?? '') ?? RISK_REASON_FALLBACK;
  }

  private static rate(figure: RiskFigure): string {
    return figure.value == null
      ? RiskPictureUtils.reason(figure)
      : RiskPictureUtils.percent(figure.value);
  }

  private static beta(figure: RiskFigure): string {
    return figure.value == null
      ? RiskPictureUtils.reason(figure, RISK_BETA_REASON_LABELS)
      : figure.value.toFixed(RISK_BETA_DIGITS);
  }

  private static weight(value: Nullable<number>, names: Nullable<string>): string {
    if (value == null) {
      return RISK_REASON_FALLBACK;
    }
    return names
      ? `${RiskPictureUtils.percent(value)} (${names})`
      : RiskPictureUtils.percent(value);
  }

  /** The drawdown beside the tolerance when one is set, and the plain "tolerance not set" when it is NULL. */
  private static drawdown(picture: RiskPicture): string {
    const measured = RiskPictureUtils.rate(picture.maxDrawdown);
    return picture.drawdownTolerance == null
      ? `${measured}, ${RISK_TOLERANCE_NOT_SET}`
      : `${measured} vs ${RiskPictureUtils.percent(picture.drawdownTolerance)} tolerance`;
  }

  private static drawdownBreached(picture: RiskPicture): boolean {
    return (
      picture.maxDrawdown.value != null &&
      picture.drawdownTolerance != null &&
      picture.maxDrawdown.value > picture.drawdownTolerance
    );
  }

  private static flagBasis(flag: Nullable<number>): string {
    return flag == null
      ? 'Position flag: none stored.'
      : `Position flag used: ${RiskPictureUtils.percent(flag)} (stored risk rule).`;
  }

  private static monthsBasis(picture: RiskPicture): string {
    return picture.from && picture.to
      ? `${picture.monthsObserved} ${RiskPictureUtils.plural(picture.monthsObserved)} from ${picture.from} to ${picture.to}`
      : 'no months measured yet';
  }

  private static plural(months: number): string {
    return months === 1 ? 'month' : 'months';
  }
}
