import {type UrlParamCodec} from '@lifekit-hq/core';

import {type TransactionType} from '../../models/transaction/transaction.model';

const ISO_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;
const MONTH_KEY_PATTERN = /^\d{4}-\d{2}$/;
const MAX_MONTHS = 120;

export const DEFAULT_MONTHS_PARAM = 1;

/** `YYYY-MM-DD` date params (`from`, `to`): anything else reads as absent. */
export const ISO_DATE_PARAM_CODEC: UrlParamCodec<Nullable<string>> = {
  encode: value => value ?? '',
  decode: raw => (ISO_DATE_PATTERN.test(raw) ? raw : null),
};

/** `YYYY-MM` month param: anything else reads as the empty string (the current month). */
export const MONTH_KEY_PARAM_CODEC: UrlParamCodec<string> = {
  encode: value => value,
  decode: raw => (MONTH_KEY_PATTERN.test(raw) ? raw : ''),
};

/** The `months` history param, a whole number from 1 to 120: anything else reads as the default. */
export const MONTHS_PARAM_CODEC: UrlParamCodec<number> = {
  encode: value => String(value),
  decode: raw => {
    const months = Number(raw);
    return Number.isInteger(months) && months >= 1 && months <= MAX_MONTHS
      ? months
      : DEFAULT_MONTHS_PARAM;
  },
};

/** The ledger `type` param: only `credit` and `debit` are filters, anything else reads as All. */
export const TRANSACTION_TYPE_PARAM_CODEC: UrlParamCodec<Nullable<TransactionType>> = {
  encode: value => value ?? '',
  decode: raw => (raw === 'credit' || raw === 'debit' ? raw : null),
};
