import ngswConfig from '../../../../ngsw-config.json';

describe('ngsw-config.json', () => {
  it('caches the app shell only — no dataGroups, so no financial data lands on the device', () => {
    expect((ngswConfig as {dataGroups?: unknown}).dataGroups).toBeUndefined();
  });

  it('keeps backend-served paths out of the navigation fallback', () => {
    expect(ngswConfig.navigationUrls).toContain('!/api/**');
  });

  it('still falls back to index.html for app routes', () => {
    expect(ngswConfig.navigationUrls).toContain('/**');
  });

  it('prefetches no asset group that matches /api', () => {
    const files = ngswConfig.assetGroups.flatMap(g => g.resources.files ?? []);
    expect(files.some(f => f.includes('/api'))).toBe(false);
  });
});
