namespace FinanceSentry.Tests.Unit.CryptoSync.RevolutX;

/// <summary>
/// Response bodies shaped exactly as the Revolut X REST reference documents them
/// (https://developer.revolut.com/docs/x-api/revolut-x-crypto-exchange-rest-api): monetary values
/// as strings, configuration as a map, ticker symbols in slash form, an optional <c>staked</c>.
/// No live key exists yet; these stand in for recorded responses.
/// </summary>
internal static class RevolutXFixtures
{
    public const string Balances = """
        [
          {"currency": "BTC", "available": "0.25000000", "reserved": "0.05000000", "total": "0.30000000"},
          {"currency": "ETH", "available": "1.50000000", "reserved": "0.00000000", "staked": "2.00000000", "total": "1.50000000"},
          {"currency": "SOL", "available": "0.00000000", "reserved": "0.00000000", "total": "0.00000000"},
          {"currency": "DOGE", "available": "10.0", "reserved": "0", "total": "10.0"},
          {"currency": "PEPE", "available": "100000", "reserved": "0", "total": "100000"},
          {"currency": "USDC", "available": "250.5", "reserved": "0", "total": "250.5"},
          {"currency": "EUR", "available": "1000.00", "reserved": "0.00", "total": "1000.00"},
          {"currency": "USD", "available": "12.34", "reserved": "0.00", "total": "12.34"}
        ]
        """;

    public const string Currencies = """
        {
          "BTC": {"symbol": "BTC", "name": "Bitcoin", "scale": 8, "asset_type": "crypto", "status": "active"},
          "ETH": {"symbol": "ETH", "name": "Ethereum", "scale": 8, "asset_type": "crypto", "status": "active"},
          "SOL": {"symbol": "SOL", "name": "Solana", "scale": 8, "asset_type": "crypto", "status": "active"},
          "DOGE": {"symbol": "DOGE", "name": "Dogecoin", "scale": 8, "asset_type": "crypto", "status": "active"},
          "PEPE": {"symbol": "PEPE", "name": "Pepe", "scale": 2, "asset_type": "crypto", "status": "active"},
          "USDC": {"symbol": "USDC", "name": "USD Coin", "scale": 6, "asset_type": "crypto", "status": "active"},
          "EUR": {"symbol": "EUR", "name": "Euro", "scale": 2, "asset_type": "fiat", "status": "active"},
          "USD": {"symbol": "USD", "name": "US Dollar", "scale": 2, "asset_type": "fiat", "status": "active"}
        }
        """;

    // BTC has a USD pair; ETH only EUR; DOGE only a USDC pair with an empty last_price (mid used);
    // PEPE has no pair at all.
    public const string Tickers = """
        {
          "data": [
            {"symbol": "BTC/USD", "bid": "59990.00", "ask": "60010.00", "mid": "60000.00", "last_price": "60000.00", "low_24h": "1", "high_24h": "1", "price_change_24h": "1", "volume_24h": "1", "quote_volume_24h": ""},
            {"symbol": "BTC/EUR", "bid": "1", "ask": "1", "mid": "1", "last_price": "1", "low_24h": "1", "high_24h": "1", "price_change_24h": "1", "volume_24h": "1", "quote_volume_24h": ""},
            {"symbol": "ETH/EUR", "bid": "2999", "ask": "3001", "mid": "3000", "last_price": "3000.00", "low_24h": "1", "high_24h": "1", "price_change_24h": "1", "volume_24h": "1", "quote_volume_24h": ""},
            {"symbol": "DOGE/USDC", "bid": "0.1", "ask": "0.1", "mid": "0.10", "last_price": "", "low_24h": "", "high_24h": "", "price_change_24h": "", "volume_24h": "", "quote_volume_24h": ""}
          ],
          "metadata": {"timestamp": 1770201294631}
        }
        """;

    public const string Unauthorized = """
        {"error_id": "7d85b5e7-d0f0-4696-b7b5-a300d0d03a5e", "message": "API key can only be used for authentication from whitelisted IP", "timestamp": 3318215482991}
        """;
}
