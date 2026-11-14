import {type GapFigures} from '../models/gap-mode/gap-mode.model';

/** Two decimals: scale up, round, scale back. */
const TWO_DECIMALS = 100;

/**
 * Pure helpers of gap mode (spec 726 US4). A gap line is descriptive ("missing", "over by 3.1 pts"); the trade
 * call is never in it (FR-020).
 */
export class GapModeUtils {
  /** The gap in words: the size in weight points and its direction, or "missing" / "on target". */
  public static gapText(figures: Pick<GapFigures, 'status' | 'gapPct'>): string {
    switch (figures.status) {
      case 'Missing':
        return 'missing';
      case 'OnTarget':
        return 'on target';
      case 'Underweight':
        return `short by ${GapModeUtils.points(figures.gapPct)} pts`;
      case 'Overweight':
        return `over by ${GapModeUtils.points(figures.gapPct)} pts`;
    }
  }

  /** The size of a gap in weight points, without sign, trimmed to at most two decimals. */
  public static points(gapPct: number): string {
    return String(Math.round(Math.abs(gapPct) * TWO_DECIMALS) / TWO_DECIMALS);
  }
}
