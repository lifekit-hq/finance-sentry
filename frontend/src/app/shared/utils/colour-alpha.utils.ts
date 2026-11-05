const HEX_SHORT = /^#([0-9a-f])([0-9a-f])([0-9a-f])$/i;
const HEX_LONG = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i;
const RGB = /^rgba?\(\s*(\d+)[\s,]+(\d+)[\s,]+(\d+)/i;
const HEX_RADIX = 16;
const NIBBLE_REPEAT = 17;

/** Canvas libraries parse `rgba(...)` and hex but not a CSS colour with alpha mixed in; this makes that form. */
export class ColourAlphaUtils {
  /** `colour` (hex or rgb) at `alpha` as `rgba(...)`; an unparseable colour comes back unchanged. */
  public static withAlpha(colour: string, alpha: number): string {
    const value = colour.trim();
    const short = HEX_SHORT.exec(value);
    if (short) {
      return ColourAlphaUtils.rgba(
        short.slice(1).map(n => parseInt(n, HEX_RADIX) * NIBBLE_REPEAT),
        alpha
      );
    }
    const long = HEX_LONG.exec(value);
    if (long) {
      return ColourAlphaUtils.rgba(
        long.slice(1).map(n => parseInt(n, HEX_RADIX)),
        alpha
      );
    }
    const rgb = RGB.exec(value);
    return rgb ? ColourAlphaUtils.rgba(rgb.slice(1).map(Number), alpha) : colour;
  }

  private static rgba(channels: number[], alpha: number): string {
    return `rgba(${channels[0]}, ${channels[1]}, ${channels[2]}, ${alpha})`;
  }
}
