import {AMOUNT_TEXT_MAX} from '../../validators/amount-text.validator';

export const MIN_OUTFLOW_UAH = 0;
export const MIN_STEP_EUR = 0.01;

export const OUTFLOW_ERROR = `Enter an amount from ${MIN_OUTFLOW_UAH} to ${AMOUNT_TEXT_MAX}.`;
export const STEP_ERROR = `Enter an amount from ${MIN_STEP_EUR} to ${AMOUNT_TEXT_MAX}.`;

export const RATE_DIGITS_FORMAT = '1.2-4';
