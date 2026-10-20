import {type UrlParamCodec} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';

/** Free-form id params (`account`, `category`, `provider`): an empty value reads as absent. */
export const NULLABLE_STRING_PARAM_CODEC: UrlParamCodec<Nullable<string>> = {
  encode: value => value ?? '',
  decode: raw => (raw === '' ? null : raw),
};
