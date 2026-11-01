import {FormControl} from '@angular/forms';
import {describe, expect, it} from 'vitest';

import {AMOUNT_TEXT_ERROR, AMOUNT_TEXT_MAX, amountText} from './amount-text.validator';

const errorFor = (text: string, min: number) =>
  amountText(min)(new FormControl(text, {nonNullable: true}));

describe('amountText', () => {
  it('accepts whole and two-decimal amounts', () => {
    expect(errorFor('15000', 0)).toBeNull();
    expect(errorFor(' 50.25 ', 0.01)).toBeNull();
  });

  it('rejects empty text', () => {
    expect(errorFor('', 0)).toHaveProperty(AMOUNT_TEXT_ERROR);
  });

  it('rejects non-numeric and negative text', () => {
    expect(errorFor('abc', 0)).toHaveProperty(AMOUNT_TEXT_ERROR);
    expect(errorFor('-5', 0)).toHaveProperty(AMOUNT_TEXT_ERROR);
  });

  it('rejects more than two decimals', () => {
    expect(errorFor('1.234', 0)).toHaveProperty(AMOUNT_TEXT_ERROR);
  });

  it('allows zero only when the minimum is zero', () => {
    expect(errorFor('0', 0)).toBeNull();
    expect(errorFor('0', 0.01)).toHaveProperty(AMOUNT_TEXT_ERROR);
  });

  it('rejects amounts above the cap', () => {
    expect(errorFor(String(AMOUNT_TEXT_MAX), 0)).toBeNull();
    expect(errorFor(String(AMOUNT_TEXT_MAX + 1), 0)).toHaveProperty(AMOUNT_TEXT_ERROR);
  });
});
