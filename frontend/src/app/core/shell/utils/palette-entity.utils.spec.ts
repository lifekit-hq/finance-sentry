import {PaletteEntityUtils} from './palette-entity.utils';

describe('PaletteEntityUtils', () => {
  const entities = {
    holdings: [
      {symbol: 'AAPL', assetClass: 'equity'},
      {symbol: 'AAPL', assetClass: 'equity'},
      {symbol: 'EUR', assetClass: 'cash'},
      {symbol: 'BRK.B', assetClass: 'equity'},
    ],
    watchlist: [{ticker: 'aapl'}, {ticker: 'NVDA'}],
    accounts: [
      {accountId: 'a1', bankName: 'Monobank', accountNumberLast4: '1234'},
      {accountId: 'a b', bankName: 'AIB', accountNumberLast4: ''},
    ],
  };

  it('opens each held name on its dossier, once, skipping cash rows (venue or broker)', () => {
    const held = PaletteEntityUtils.items(entities).filter(i => i.group === 'Holdings');
    expect(held.map(i => [i.id, i.label])).toEqual([
      ['/assets/AAPL', 'AAPL'],
      ['/assets/BRK.B', 'BRK.B'],
    ]);
  });

  it('lists watchlist names not already held', () => {
    const watched = PaletteEntityUtils.items(entities).filter(i => i.group === 'Watchlist');
    expect(watched.map(i => i.id)).toEqual(['/assets/NVDA']);
  });

  it('opens an account on the ledger filtered to it', () => {
    const accounts = PaletteEntityUtils.items(entities).filter(i => i.group === 'Accounts');
    expect(accounts.map(i => [i.id, i.label])).toEqual([
      ['/transactions?account=a1', 'Monobank ····1234'],
      ['/transactions?account=a%20b', 'AIB'],
    ]);
  });

  it('encodes a symbol into one route segment', () => {
    expect(PaletteEntityUtils.assetRoute('A/B')).toBe('/assets/A%2FB');
  });

  it('yields nothing for an empty book', () => {
    expect(PaletteEntityUtils.items({holdings: [], watchlist: [], accounts: []})).toEqual([]);
  });
});
