import {describe, expect, it} from 'vitest';

import {ClientInfoUtils} from './client-info.utils';

const IOS_SAFARI =
  'Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1';
const ANDROID_CHROME =
  'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Mobile Safari/537.36';
const SAMSUNG =
  'Mozilla/5.0 (Linux; Android 14; SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/25.0 Chrome/121.0.0.0 Mobile Safari/537.36';
const MAC_FIREFOX =
  'Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:128.0) Gecko/20100101 Firefox/128.0';
const WIN_EDGE =
  'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0';
const LINUX_CHROME =
  'Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36';
const IPAD =
  'Mozilla/5.0 (iPad; CPU OS 17_5 like Mac OS X) AppleWebKit/605.1.15 Version/17.5 Safari/604.1';

describe('ClientInfoUtils', () => {
  it.each([
    [IOS_SAFARI, 'iOS', 'Safari'],
    [ANDROID_CHROME, 'Android', 'Chrome'],
    [SAMSUNG, 'Android', 'Samsung Internet'],
    [MAC_FIREFOX, 'macOS', 'Firefox'],
    [WIN_EDGE, 'Windows', 'Edge'],
    [LINUX_CHROME, 'Linux', 'Chrome'],
    [IPAD, 'iPadOS', 'Safari'],
  ])('reads the families from %s', (userAgent, os, browser) => {
    expect(ClientInfoUtils.os(userAgent)).toBe(os);
    expect(ClientInfoUtils.browser(userAgent)).toBe(browser);
  });

  it('returns null for a user agent it does not know', () => {
    expect(ClientInfoUtils.os('curl/8.0')).toBeNull();
    expect(ClientInfoUtils.browser('curl/8.0')).toBeNull();
  });

  it('maps the shell mode to the device', () => {
    expect(ClientInfoUtils.device(true)).toBe('Phone');
    expect(ClientInfoUtils.device(false)).toBe('Desktop');
  });
});
