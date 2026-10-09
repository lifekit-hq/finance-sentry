export class VersionUtils {
  /** Whether semver `a` is newer than `b`; a version that is not `x.y.z` is never newer. */
  public static isNewer(a: string, b: string): boolean {
    const left = VersionUtils.parts(a);
    const right = VersionUtils.parts(b);
    if (left === null || right === null) {
      return false;
    }
    for (let index = 0; index < left.length; index++) {
      if (left[index] !== right[index]) {
        return left[index] > right[index];
      }
    }
    return false;
  }

  private static parts(version: string): Nullable<number[]> {
    const match = /^(\d+)\.(\d+)\.(\d+)$/.exec(version);
    return match === null ? null : [Number(match[1]), Number(match[2]), Number(match[3])];
  }
}
