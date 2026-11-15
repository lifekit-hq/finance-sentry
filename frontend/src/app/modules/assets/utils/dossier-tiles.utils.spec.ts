import {describe, expect, it} from 'vitest';

import {type AssetDossierDto, type DossierNotableMoveDto} from '../models/dossier/dossier.model';
import {type TargetBandView} from '../models/dossier/dossier-view.model';
import {
  DOSSIER,
  DOSSIER_ANALYSTS,
  POSITION,
  STALE_VALUATION,
} from '../pages/asset-dossier/asset-dossier.fixtures';
import {DossierTilesUtils, type FactTileInput} from './dossier-tiles.utils';
import {DossierViewUtils} from './dossier-view.utils';

const BAND: TargetBandView = {
  currency: 'USD',
  low: 35,
  high: 60,
  lowPct: 10,
  highPct: 90,
  markers: [
    {key: 'price', label: 'Price', value: 40, leftPct: 30},
    {key: 'analyst', label: 'Analyst target', value: 50, leftPct: 60},
  ],
  source: 'Yahoo Finance · 12 analysts',
};

const NEWS_MOVE: DossierNotableMoveDto = {
  date: '2026-10-20',
  returnPct: 8.2,
  links: [
    {
      kind: 'news',
      timestamp: '2026-10-20T12:00:00Z',
      title: 'Memory prices climb',
      source: 'Wire',
      url: 'https://example.com/a',
      severity: null,
      relevance: 3,
    },
    {
      kind: 'signal',
      timestamp: '2026-10-21T09:00:00Z',
      title: 'volume_spike',
      source: 'scanner',
      url: null,
      severity: 'notable',
      relevance: 2,
    },
  ],
};

function input(dossier: Partial<AssetDossierDto> = {}, rest: Partial<FactTileInput> = {}) {
  const merged = {...DOSSIER, ...dossier};
  return {
    dossier: merged,
    valuation: merged.valuation,
    targetBand: null,
    recommendation: DossierViewUtils.recommendationSummary(merged.analysts?.trends ?? []),
    ...rest,
  };
}

const headlineOf = (tiles: ReturnType<typeof DossierTilesUtils.tiles>, id: string) =>
  tiles.find(tile => tile.id === id)?.headline;

describe('DossierTilesUtils.tiles', () => {
  it('gives each section with something to say one tile, in page order, under the id its card had', () => {
    const tiles = DossierTilesUtils.tiles(
      input({
        valuation: {...STALE_VALUATION, isStale: false},
        nextEarnings: {
          ticker: 'DRAM',
          eventType: 'Earnings',
          eventDate: '2026-11-19',
          isEstimate: false,
          source: 'x',
        },
        radarSignals: [
          {
            timestamp: '2026-09-01T00:00:00Z',
            scanner: 's',
            signalType: 't',
            severity: 'info',
            payload: {},
          },
        ],
        notableMoves: {thresholdPct: 5, windowDays: 1, visibleDays: 30, moves: [NEWS_MOVE]},
      })
    );

    expect(tiles.map(tile => tile.id)).toEqual([
      'valuation',
      'analyst-coverage',
      'thesis',
      'next-earnings',
      'recent-news',
      'radar-signals',
      'ownership',
      'move-2026-10-20',
    ]);
  });

  it('leaves out a section the card was not shown for', () => {
    const tiles = DossierTilesUtils.tiles(
      input({thesis: null, analysts: null, recentNews: [], position: POSITION})
    );

    expect(tiles.map(tile => tile.id)).toEqual(['ownership']);
  });

  it('says what the street thinks as a count of the analysts', () => {
    const tiles = DossierTilesUtils.tiles(input({analysts: DOSSIER_ANALYSTS}));

    expect(headlineOf(tiles, 'analyst-coverage')).toBe('17 of 26 Buy');
  });

  it('counts the recent actions when no month of ratings is held', () => {
    const tiles = DossierTilesUtils.tiles(input({analysts: {...DOSSIER_ANALYSTS, trends: []}}));

    expect(headlineOf(tiles, 'analyst-coverage')).toBe('2 actions');
  });

  it('reads the thesis as active or invalidated', () => {
    const active = DossierTilesUtils.tiles(input());
    const broken = DossierTilesUtils.tiles(
      input({
        thesis: {
          ...(DOSSIER.thesis as NonNullable<typeof DOSSIER.thesis>),
          brokenAt: '2026-10-01T00:00:00Z',
        },
      })
    );

    expect(headlineOf(active, 'thesis')).toBe('Active');
    expect(headlineOf(broken, 'thesis')).toBe('Invalidated');
  });

  it('counts the news in the singular and the plural', () => {
    const one = DossierTilesUtils.tiles(input());
    const two = DossierTilesUtils.tiles(
      input({recentNews: [...DOSSIER.recentNews, {...DOSSIER.recentNews[0], id: 'n2'}]})
    );

    expect(headlineOf(one, 'recent-news')).toBe('1 article');
    expect(headlineOf(two, 'recent-news')).toBe('2 articles');
  });

  it('puts the date of the next report on the tile', () => {
    const tiles = DossierTilesUtils.tiles(
      input({
        nextEarnings: {
          ticker: 'DRAM',
          eventType: 'Earnings',
          eventDate: '2026-11-19',
          isEstimate: true,
          source: 'x',
        },
      })
    );

    expect(headlineOf(tiles, 'next-earnings')).toBe('2026-11-19');
  });

  describe('valuation headline', () => {
    const valued = {...STALE_VALUATION, isStale: false};

    it('leads with the analysts target and the room to it', () => {
      const tiles = DossierTilesUtils.tiles(input({valuation: valued}, {targetBand: BAND}));

      expect(headlineOf(tiles, 'valuation')).toBe('$50.00 · +25.0%');
    });

    it('falls back to the owners own target', () => {
      const band: TargetBandView = {
        ...BAND,
        markers: [
          {key: 'price', label: 'Price', value: 40, leftPct: 30},
          {key: 'mine', label: 'Your target', value: 55, leftPct: 70},
        ],
      };
      const tiles = DossierTilesUtils.tiles(input({valuation: valued}, {targetBand: band}));

      expect(headlineOf(tiles, 'valuation')).toBe('Your target $55.00');
    });

    it('falls back to the forward multiple, then to an invitation to set a target', () => {
      const withPe = {
        ...valued,
        metrics: {...valued.metrics, forwardPe: {...valued.metrics.forwardPe, value: 24.14}},
      };

      expect(headlineOf(DossierTilesUtils.tiles(input({valuation: withPe})), 'valuation')).toBe(
        'Fwd P/E 24.1x'
      );
      expect(headlineOf(DossierTilesUtils.tiles(input({valuation: valued})), 'valuation')).toBe(
        'Set your target'
      );
    });

    it('is the price card for crypto, with no analyst or ownership tile', () => {
      const tiles = DossierTilesUtils.tiles(
        input(
          {
            position: {...POSITION, assetClass: 'Crypto'},
            valuation: {...valued, notApplicable: true},
            performance: {
              lastClose: 40,
              yearChangePct: null,
              high52Week: null,
              low52Week: null,
              fromHighPct: null,
              benchmarkYearChangePct: null,
              currency: 'USD',
              asOf: null,
            },
          },
          {targetBand: BAND}
        )
      );

      expect(tiles.find(tile => tile.id === 'valuation')?.label).toBe('Price & target');
      expect(tiles.map(tile => tile.id)).not.toContain('analyst-coverage');
      expect(tiles.map(tile => tile.id)).not.toContain('ownership');
    });
  });

  describe('a notable move', () => {
    const tilesFor = (moves: DossierNotableMoveDto[]) =>
      DossierTilesUtils.tiles(
        input({notableMoves: {thresholdPct: 5, windowDays: 1, visibleDays: 30, moves}})
      ).filter(tile => tile.id.startsWith('move-'));

    it('is a tile labelled with the day and headlined with the size and the causes found', () => {
      expect(tilesFor([NEWS_MOVE])).toEqual([
        {id: 'move-2026-10-20', label: 'Moved 20 Oct', headline: '+8.2% · news, signal'},
      ]);
    });

    it('names a single cause, or says none was found', () => {
      const newsOnly = {...NEWS_MOVE, links: [NEWS_MOVE.links[0]]};
      const none = {date: '2026-10-14', returnPct: -6.1, links: []};

      expect(tilesFor([newsOnly, none]).map(tile => tile.headline)).toEqual([
        '+8.2% · news',
        '-6.1% · no cause found',
      ]);
    });

    it('keeps the order the API sent, newest first', () => {
      const older = {date: '2026-10-14', returnPct: -6.1, links: []};

      expect(tilesFor([NEWS_MOVE, older]).map(tile => tile.id)).toEqual([
        'move-2026-10-20',
        'move-2026-10-14',
      ]);
    });
  });
});

describe('DossierTilesUtils.moves', () => {
  it('counts the news and the signals around each move and gives the direction', () => {
    const [move] = DossierTilesUtils.moves({
      ...DOSSIER,
      notableMoves: {thresholdPct: 5, windowDays: 1, visibleDays: 30, moves: [NEWS_MOVE]},
    });

    expect(move).toMatchObject({
      tileId: 'move-2026-10-20',
      direction: 'up',
      newsCount: 1,
      signalCount: 1,
    });
  });

  it('is empty without a dossier or a move', () => {
    expect(DossierTilesUtils.moves(null)).toEqual([]);
    expect(DossierTilesUtils.moves(DOSSIER)).toEqual([]);
  });

  it('marks a falling day as down', () => {
    const [move] = DossierTilesUtils.moves({
      ...DOSSIER,
      notableMoves: {
        thresholdPct: 5,
        windowDays: 1,
        visibleDays: 30,
        moves: [{date: '2026-10-14', returnPct: -6.1, links: []}],
      },
    });

    expect(move.direction).toBe('down');
  });
});

describe('DossierTilesUtils.isTileId', () => {
  it('knows every section id a card had and every move tile, and nothing else', () => {
    expect(DossierTilesUtils.isTileId('analyst-coverage')).toBe(true);
    expect(DossierTilesUtils.isTileId('thesis')).toBe(true);
    expect(DossierTilesUtils.isTileId('move-2026-10-20')).toBe(true);
    expect(DossierTilesUtils.isTileId('price-chart')).toBe(false);
    expect(DossierTilesUtils.isTileId('data-status')).toBe(false);
  });
});
