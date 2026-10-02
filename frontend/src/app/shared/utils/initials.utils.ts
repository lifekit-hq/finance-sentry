export class InitialsUtils {
  /** First+last name initials when a name is known, else the email's first letter, else `?`. */
  public static fromProfile(
    firstName: Nullable<string>,
    lastName: Nullable<string>,
    email: Nullable<string>
  ): string {
    const nameInitials = `${firstName?.trim().charAt(0) ?? ''}${lastName?.trim().charAt(0) ?? ''}`;
    return (nameInitials || email?.trim().charAt(0) || '?').toUpperCase();
  }
}
