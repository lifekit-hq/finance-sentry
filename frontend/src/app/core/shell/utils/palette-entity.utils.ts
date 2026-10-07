import {type CommandPaletteItem} from '@lifekit-hq/ui';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {type PaletteEntities} from '../models/palette-entities.model';

export const PALETTE_GROUP_HOLDINGS = 'Holdings';
export const PALETTE_GROUP_WATCHLIST = 'Watchlist';
export const PALETTE_GROUP_ACCOUNTS = 'Accounts';

/** The ledger's existing account filter (`?account=`), so an account item opens its transactions. */
const LEDGER_ACCOUNT_PARAM = 'account';

export class PaletteEntityUtils {
  /** Dossier route of a ticker; the symbol is encoded because the router takes it as one segment. */
  public static assetRoute(symbol: string): string {
    return `${AppRoute.AssetDossier}/${encodeURIComponent(symbol)}`;
  }

  /** Ledger route filtered to one account. */
  public static accountRoute(accountId: string): string {
    return `${AppRoute.Transactions}?${LEDGER_ACCOUNT_PARAM}=${encodeURIComponent(accountId)}`;
  }

  /** Palette items for held names, watchlist names (not already held) and accounts; ids are routes. */
  public static items(entities: PaletteEntities): CommandPaletteItem[] {
    const seen = new Set<string>();
    const asset = (symbol: string, group: string): CommandPaletteItem[] => {
      const key = symbol.trim().toUpperCase();
      if (key === '' || seen.has(key)) {
        return [];
      }
      seen.add(key);
      return [{id: PaletteEntityUtils.assetRoute(symbol), label: symbol, icon: 'ChartLine', group}];
    };

    const held = entities.holdings
      .filter(h => !h.isVenueCash)
      .flatMap(h => asset(h.symbol, PALETTE_GROUP_HOLDINGS));
    const watched = entities.watchlist.flatMap(w => asset(w.ticker, PALETTE_GROUP_WATCHLIST));
    const accounts = entities.accounts.map(a => ({
      id: PaletteEntityUtils.accountRoute(a.accountId),
      label: a.accountNumberLast4 ? `${a.bankName} ····${a.accountNumberLast4}` : a.bankName,
      icon: 'Building2',
      group: PALETTE_GROUP_ACCOUNTS,
    }));
    return [...held, ...watched, ...accounts];
  }
}
