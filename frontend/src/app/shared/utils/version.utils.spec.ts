import {VersionUtils} from './version.utils';

describe('VersionUtils.isNewer', () => {
  it('is true when the major, minor or patch is higher', () => {
    expect(VersionUtils.isNewer('2.0.0', '1.14.0')).toBe(true);
    expect(VersionUtils.isNewer('1.15.0', '1.14.9')).toBe(true);
    expect(VersionUtils.isNewer('1.14.1', '1.14.0')).toBe(true);
  });

  it('compares numbers, not strings', () => {
    expect(VersionUtils.isNewer('1.10.0', '1.9.0')).toBe(true);
    expect(VersionUtils.isNewer('1.9.0', '1.10.0')).toBe(false);
  });

  it('is false for the same or an older version', () => {
    expect(VersionUtils.isNewer('1.14.0', '1.14.0')).toBe(false);
    expect(VersionUtils.isNewer('1.13.0', '1.14.0')).toBe(false);
  });

  it('is false when either side is not x.y.z', () => {
    expect(VersionUtils.isNewer('1.15', '1.14.0')).toBe(false);
    expect(VersionUtils.isNewer('1.15.0', 'abc')).toBe(false);
    expect(VersionUtils.isNewer('1.15.0-rc1', '1.14.0')).toBe(false);
  });
});
