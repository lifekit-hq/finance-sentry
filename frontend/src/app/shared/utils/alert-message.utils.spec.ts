import {describe, expect, it} from 'vitest';

import {AlertMessageUtils} from './alert-message.utils';

describe('AlertMessageUtils.roundNumbers', () => {
  it('rounds long decimals to two places', () => {
    expect(AlertMessageUtils.roundNumbers('Balance 1234.56789 EUR')).toBe('Balance 1234.57 EUR');
  });

  it('rounds every long decimal in the text', () => {
    expect(AlertMessageUtils.roundNumbers('1.23456 of 9.87654')).toBe('1.23 of 9.88');
  });

  it('leaves short decimals and integers untouched', () => {
    expect(AlertMessageUtils.roundNumbers('Spent 12.5 of 100')).toBe('Spent 12.5 of 100');
  });

  it('returns an empty string for null and empty input', () => {
    expect(AlertMessageUtils.roundNumbers(null)).toBe('');
    expect(AlertMessageUtils.roundNumbers('')).toBe('');
  });
});

describe('AlertMessageUtils filing URL', () => {
  const url = 'https://www.sec.gov/Archives/edgar/data/320193/000032019324000123/aapl-20240630.htm';
  const message = `AAPL filed a 10-Q on 2024-08-02. ${url}`;

  it('splits the trailing sec.gov URL off the sentence', () => {
    expect(AlertMessageUtils.stripFilingUrl(message)).toBe('AAPL filed a 10-Q on 2024-08-02.');
    expect(AlertMessageUtils.filingUrl(message)).toBe(url);
  });

  it('leaves messages without a sec.gov URL untouched', () => {
    expect(AlertMessageUtils.stripFilingUrl('Spent 12 of 100')).toBe('Spent 12 of 100');
    expect(AlertMessageUtils.filingUrl('Spent 12 of 100')).toBeNull();
  });

  it('ignores non-sec.gov and non-trailing URLs', () => {
    expect(AlertMessageUtils.filingUrl('See https://example.com/sec.gov')).toBeNull();
    expect(AlertMessageUtils.filingUrl(`${url} was filed`)).toBeNull();
    expect(AlertMessageUtils.stripFilingUrl('See https://example.com/x')).toBe(
      'See https://example.com/x'
    );
  });

  it('returns empty / null for null and empty input', () => {
    expect(AlertMessageUtils.stripFilingUrl(null)).toBe('');
    expect(AlertMessageUtils.filingUrl(null)).toBeNull();
  });
});

describe('AlertMessageUtils.stripReviewTail', () => {
  const headline = 'NVDA filed a 10-Q: thesis review due.';
  const tail =
    '[review subject=thesis:3f2a9c1e-0000-4000-8000-000000000001 ticker=NVDA source=filing ref=0001045810-26-000123 evidence=filing:0001045810-26-000123|url:https://www.sec.gov/Archives/edgar/data/1045810/x.htm]';

  it('removes the machine tail and its evidence url from a thesis review message', () => {
    expect(AlertMessageUtils.stripReviewTail(`${headline} ${tail}`)).toBe(headline);
  });

  it('removes the machine tail from a candidate review message', () => {
    expect(
      AlertMessageUtils.stripReviewTail(
        'Earnings passed. [review subject=candidate:3f2a9c1e-0000-4000-8000-000000000002 ticker=AMD source=earnings ref=2026-11-04]'
      )
    ).toBe('Earnings passed.');
  });

  it('leaves messages without the tail untouched', () => {
    expect(AlertMessageUtils.stripReviewTail('Spent 12 of 100 [budget]')).toBe(
      'Spent 12 of 100 [budget]'
    );
  });

  it('returns an empty string for null and empty input', () => {
    expect(AlertMessageUtils.stripReviewTail(null)).toBe('');
    expect(AlertMessageUtils.stripReviewTail('')).toBe('');
  });

  it('works alongside the sec.gov strip and decimal rounding', () => {
    const url = 'https://www.sec.gov/Archives/edgar/data/1045810/x.htm';
    const message = `Drop of 12.34567% on NVDA. ${url} ${tail}`;
    const result = AlertMessageUtils.roundNumbers(
      AlertMessageUtils.stripFilingUrl(AlertMessageUtils.stripReviewTail(message))
    );
    expect(result).toBe('Drop of 12.35% on NVDA.');
  });
});

describe('AlertMessageUtils.duplicateChargeMerchant', () => {
  it('reads the statement merchant out of a duplicate-charge message', () => {
    expect(
      AlertMessageUtils.duplicateChargeMerchant(
        'Charged 3× for 12.50 USD at PAYPAL *SPOTIFY within the detection window.'
      )
    ).toBe('PAYPAL *SPOTIFY');
  });

  it('returns null for any other message, or none', () => {
    expect(AlertMessageUtils.duplicateChargeMerchant('Balance dropped')).toBeNull();
    expect(AlertMessageUtils.duplicateChargeMerchant(null)).toBeNull();
  });
});
