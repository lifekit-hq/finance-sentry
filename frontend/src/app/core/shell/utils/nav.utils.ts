import {PERMISSION_BY_ENTRY} from '../app-shell.constants';

export class NavUtils {
  /** Whether a user holding `permissions` is shown the nav route or palette entry `entry`. */
  public static isPermitted(entry: string, permissions: readonly string[]): boolean {
    const permission = PERMISSION_BY_ENTRY[entry];
    return permission === undefined || permissions.includes(permission);
  }
}
