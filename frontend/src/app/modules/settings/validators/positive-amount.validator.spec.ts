import {FormControl} from '@angular/forms';
import {describe, expect, it} from 'vitest';

import {POSITIVE_AMOUNT_ERROR, positiveAmount} from './positive-amount.validator';

const MAX = 100;

describe('positiveAmount', () => {
  it.each(['1', '0.5', ' 2.5 ', '100'])('accepts %s', text => {
    expect(positiveAmount(MAX)(new FormControl(text))).toBeNull();
  });

  it.each(['', '  ', '0', '-1', '100.01', 'abc'])('rejects "%s"', text => {
    expect(positiveAmount(MAX)(new FormControl(text))).toHaveProperty(POSITIVE_AMOUNT_ERROR);
  });
});
