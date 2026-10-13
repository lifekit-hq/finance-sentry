import {type ReturnsCoverage} from '../../models/dossier/dossier.model';
import {type ReturnLegView} from '../../models/dossier/dossier-view.model';

export const RETURN_LEG_LABELS: Readonly<Record<ReturnLegView['key'], string>> = {
  unrealized: 'Unrealized',
  realized: 'Realized',
  dividends: 'Dividends',
  currency: 'Currency',
};

export const RETURNS_COVERAGE_NOTES: Readonly<Record<ReturnsCoverage, string>> = {
  complete: 'From every ingested fill',
  partial:
    'The ingested fills cover only part of this holding, so realized, dividends and currency are incomplete.',
  unavailable: 'No fills were ingested for this holding, so only the unrealized part is known.',
};

/** The size bar runs a quarter past the larger of the share and the cap, so both stay readable. */
export const CAP_SCALE_HEADROOM = 1.25;

/** The target bar's scale extends this share of its span past the lowest and highest marks. */
export const TARGET_SCALE_PADDING = 0.1;

/** The analyst range and count come from Yahoo Finance's financialData module. */
export const ANALYST_TARGET_SOURCE = 'yahoo-finance';

/** The API rejects a target at or above this (INVALID_PRICE_TARGET); the editor checks first. */
export const MAX_TARGET_PRICE = 1_000_000_000;
