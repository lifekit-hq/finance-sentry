import {describe, expect, it} from 'vitest';

import {ErrorUtils} from './error.utils';

describe('ErrorUtils.extractCode', () => {
  it('returns errorCode when nested under error', () => {
    expect(ErrorUtils.extractCode({error: {errorCode: 'INVALID_TOKEN'}})).toBe('INVALID_TOKEN');
  });

  it('returns null when input is null or undefined', () => {
    expect(ErrorUtils.extractCode(null)).toBeNull();
    expect(ErrorUtils.extractCode(undefined)).toBeNull();
  });

  it('returns null when error body is missing', () => {
    expect(ErrorUtils.extractCode({})).toBeNull();
    expect(ErrorUtils.extractCode({error: {}})).toBeNull();
  });

  it('returns null when shape does not match ApiErrorResponse', () => {
    expect(ErrorUtils.extractCode('boom')).toBeNull();
    expect(ErrorUtils.extractCode(42)).toBeNull();
    expect(ErrorUtils.extractCode({message: 'boom'})).toBeNull();
  });
});

describe('ErrorUtils.extractMessage', () => {
  it('returns the server message when nested under error', () => {
    expect(ErrorUtils.extractMessage({error: {error: 'Try again in 3 minutes.'}})).toBe('Try again in 3 minutes.');
  });

  it('returns null when the body or message is missing', () => {
    expect(ErrorUtils.extractMessage(null)).toBeNull();
    expect(ErrorUtils.extractMessage({error: {}})).toBeNull();
    expect(ErrorUtils.extractMessage('boom')).toBeNull();
  });
});
