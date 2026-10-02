import {InitialsUtils} from './initials.utils';

describe('InitialsUtils.fromProfile', () => {
  it('uses first and last name initials', () => {
    expect(InitialsUtils.fromProfile('denys', 'test', 't@x.io')).toBe('DT');
  });

  it('uses a single initial when only one name part is set', () => {
    expect(InitialsUtils.fromProfile('Denys', '', 't@x.io')).toBe('D');
  });

  it('falls back to the email first letter', () => {
    expect(InitialsUtils.fromProfile(null, null, ' test@x.io')).toBe('T');
  });

  it('returns ? when nothing is known', () => {
    expect(InitialsUtils.fromProfile(null, null, null)).toBe('?');
  });
});
