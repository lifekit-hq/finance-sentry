import {type Config, type Filesystem, Generator} from '@angular/service-worker/config';

import ngswConfig from '../../../../ngsw-config.json';

interface NavigationRule {
  positive: boolean;
  regex: string;
}

interface GeneratedAssetGroup {
  name: string;
  urls: string[];
}

interface GeneratedControlFile {
  index: string;
  assetGroups: GeneratedAssetGroup[];
  dataGroups: unknown[];
  navigationUrls: NavigationRule[];
}

const BUILD_OUTPUT = [
  '/favicon.ico',
  '/index.html',
  '/index.csr.html',
  '/manifest.webmanifest',
  '/main-ABC123.js',
  '/styles-DEF456.css',
  '/icon-192.png',
  '/apple-touch-icon.png',
  '/api/v1/accounts.png',
  '/api/v1/portfolio.js',
];

class InMemoryFilesystem implements Filesystem {
  public list(): Promise<string[]> {
    return Promise.resolve(BUILD_OUTPUT);
  }

  public read(file: string): Promise<string> {
    return Promise.resolve(file);
  }

  public hash(file: string): Promise<string> {
    return Promise.resolve(`hash:${file}`);
  }

  public write(): Promise<void> {
    return Promise.resolve();
  }
}

const isNavigationFallback = (rules: NavigationRule[], url: string): boolean =>
  rules.some(r => r.positive && new RegExp(r.regex).test(url)) &&
  !rules.some(r => !r.positive && new RegExp(r.regex).test(url));

describe('ngsw-config.json', () => {
  let generated: GeneratedControlFile;

  beforeAll(async () => {
    generated = (await new Generator(new InMemoryFilesystem(), '/').process(
      ngswConfig as Config
    )) as GeneratedControlFile;
  });

  it('caches the app shell only — no data groups, so no financial data lands on the device', () => {
    expect(generated.dataGroups).toEqual([]);
  });

  it('precaches no file served under /api', () => {
    const cached = generated.assetGroups.flatMap(g => g.urls);

    expect(cached).toContain('/index.html');
    expect(cached.filter(url => url.startsWith('/api/'))).toEqual([]);
  });

  it.each(['/api/v1/accounts', '/api/v1/auth/refresh', '/healthz', '/readyz', '/metrics'])(
    'passes backend path %s through to the network instead of the index.html fallback',
    url => {
      expect(isNavigationFallback(generated.navigationUrls, url)).toBe(false);
    }
  );

  it.each(['/', '/dashboard', '/accounts/42'])(
    'serves app route %s from the index.html fallback',
    url => {
      expect(generated.index).toBe('/index.html');
      expect(isNavigationFallback(generated.navigationUrls, url)).toBe(true);
    }
  );
});
