using System.Xml.Serialization;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;

/// <summary>
/// The envelope both Flex Web Service steps can return: the <c>SendRequest</c> acknowledgement
/// (success + <see cref="ReferenceCode"/>, or a failure), and any <c>GetStatement</c> response
/// that is not yet the statement itself — including the "not yet generated" (error code 1019)
/// response the client retries on.
/// </summary>
[XmlRoot("FlexStatementResponse")]
public sealed class FlexStatementResponseXml
{
    [XmlElement("Status")]
    public string Status { get; set; } = string.Empty;

    [XmlElement("ReferenceCode")]
    public string? ReferenceCode { get; set; }

    [XmlElement("Url")]
    public string? Url { get; set; }

    [XmlElement("ErrorCode")]
    public string? ErrorCode { get; set; }

    [XmlElement("ErrorMessage")]
    public string? ErrorMessage { get; set; }
}

/// <summary>The generated Activity Flex Query statement, returned by a ready <c>GetStatement</c> call.</summary>
[XmlRoot("FlexQueryResponse")]
public sealed class FlexQueryResponseXml
{
    [XmlAttribute("queryName")]
    public string? QueryName { get; set; }

    [XmlAttribute("type")]
    public string? Type { get; set; }

    [XmlElement("FlexStatements")]
    public FlexStatementsXml FlexStatements { get; set; } = new();
}

public sealed class FlexStatementsXml
{
    [XmlAttribute("count")]
    public int Count { get; set; }

    [XmlElement("FlexStatement")]
    public List<FlexStatementXml> Items { get; set; } = [];
}

public sealed class FlexStatementXml
{
    [XmlAttribute("accountId")]
    public string AccountId { get; set; } = string.Empty;

    [XmlAttribute("fromDate")]
    public string FromDate { get; set; } = string.Empty;

    [XmlAttribute("toDate")]
    public string ToDate { get; set; } = string.Empty;

    [XmlAttribute("period")]
    public string? Period { get; set; }

    [XmlAttribute("whenGenerated")]
    public string? WhenGenerated { get; set; }

    [XmlArray("Trades")]
    [XmlArrayItem("Trade")]
    public List<FlexTradeXml> Trades { get; set; } = [];

    [XmlArray("CashTransactions")]
    [XmlArrayItem("CashTransaction")]
    public List<FlexCashTransactionXml> CashTransactions { get; set; } = [];

    [XmlArray("FinancialInstrumentInformation")]
    [XmlArrayItem("FinancialInstrument")]
    public List<FlexFinancialInstrumentXml> FinancialInstruments { get; set; } = [];
}

/// <summary>
/// One row of the Trades section (report §4.3). Every field is kept as the raw wire string —
/// IBKR sends empty strings for several numeric fields on certain transaction types, and
/// converting/persisting them is PR 2's job (trade/lot persistence), not this deserialization step.
/// </summary>
public sealed class FlexTradeXml
{
    [XmlAttribute("accountId")] public string? AccountId { get; set; }
    [XmlAttribute("currency")] public string? Currency { get; set; }
    [XmlAttribute("fxRateToBase")] public string? FxRateToBase { get; set; }
    [XmlAttribute("assetCategory")] public string? AssetCategory { get; set; }
    [XmlAttribute("symbol")] public string? Symbol { get; set; }
    [XmlAttribute("description")] public string? Description { get; set; }
    [XmlAttribute("conid")] public string? Conid { get; set; }
    [XmlAttribute("securityID")] public string? SecurityId { get; set; }
    [XmlAttribute("cusip")] public string? Cusip { get; set; }
    [XmlAttribute("isin")] public string? Isin { get; set; }
    [XmlAttribute("figi")] public string? Figi { get; set; }
    [XmlAttribute("listingExchange")] public string? ListingExchange { get; set; }
    [XmlAttribute("tradeID")] public string? TradeId { get; set; }
    [XmlAttribute("reportDate")] public string? ReportDate { get; set; }
    [XmlAttribute("tradeDate")] public string? TradeDate { get; set; }
    [XmlAttribute("tradeTime")] public string? TradeTime { get; set; }
    [XmlAttribute("settleDateTarget")] public string? SettleDateTarget { get; set; }
    [XmlAttribute("transactionType")] public string? TransactionType { get; set; }
    [XmlAttribute("quantity")] public string? Quantity { get; set; }
    [XmlAttribute("tradePrice")] public string? TradePrice { get; set; }
    [XmlAttribute("tradeMoney")] public string? TradeMoney { get; set; }
    [XmlAttribute("proceeds")] public string? Proceeds { get; set; }
    [XmlAttribute("taxes")] public string? Taxes { get; set; }
    [XmlAttribute("ibCommission")] public string? IbCommission { get; set; }
    [XmlAttribute("ibCommissionCurrency")] public string? IbCommissionCurrency { get; set; }
    [XmlAttribute("closePrice")] public string? ClosePrice { get; set; }
    [XmlAttribute("openCloseIndicator")] public string? OpenCloseIndicator { get; set; }
    [XmlAttribute("notes")] public string? Notes { get; set; }
    [XmlAttribute("cost")] public string? CostBasis { get; set; }
    [XmlAttribute("fifoPnlRealized")] public string? RealizedPnl { get; set; }
    [XmlAttribute("mtmPnl")] public string? MtmPnl { get; set; }
    [XmlAttribute("buySell")] public string? BuySell { get; set; }
    [XmlAttribute("ibOrderID")] public string? IbOrderId { get; set; }
    [XmlAttribute("ibExecID")] public string? IbExecutionId { get; set; }
    [XmlAttribute("openDateTime")] public string? OpenDateTime { get; set; }
    [XmlAttribute("holdingPeriodDateTime")] public string? HoldingPeriodDateTime { get; set; }
    [XmlAttribute("netCash")] public string? NetCash { get; set; }
    [XmlAttribute("levelOfDetail")] public string? LevelOfDetail { get; set; }
}

/// <summary>One row of the Cash Transactions section (report §4.3 / §6 — sizes S8 dividends).</summary>
public sealed class FlexCashTransactionXml
{
    [XmlAttribute("accountId")] public string? AccountId { get; set; }
    [XmlAttribute("currency")] public string? Currency { get; set; }
    [XmlAttribute("fxRateToBase")] public string? FxRateToBase { get; set; }
    [XmlAttribute("assetCategory")] public string? AssetCategory { get; set; }
    [XmlAttribute("symbol")] public string? Symbol { get; set; }
    [XmlAttribute("description")] public string? Description { get; set; }
    [XmlAttribute("conid")] public string? Conid { get; set; }
    [XmlAttribute("securityID")] public string? SecurityId { get; set; }
    [XmlAttribute("cusip")] public string? Cusip { get; set; }
    [XmlAttribute("isin")] public string? Isin { get; set; }
    [XmlAttribute("figi")] public string? Figi { get; set; }
    [XmlAttribute("dateTime")] public string? DateTime { get; set; }
    [XmlAttribute("amount")] public string? Amount { get; set; }
    [XmlAttribute("type")] public string? Type { get; set; }
    [XmlAttribute("tradeID")] public string? TradeId { get; set; }
    [XmlAttribute("code")] public string? Code { get; set; }

    /// <summary>The "871(m) Withholding" field (report §6) — named per the Activity Flex Query
    /// reference; verify the exact attribute name against a real statement before PR 2 relies on it.</summary>
    [XmlAttribute("section871m")] public string? Section871mWithholding { get; set; }
}

/// <summary>One row of the Financial Instrument Information section (report §4.3).</summary>
public sealed class FlexFinancialInstrumentXml
{
    [XmlAttribute("assetCategory")] public string? AssetCategory { get; set; }
    [XmlAttribute("symbol")] public string? Symbol { get; set; }
    [XmlAttribute("description")] public string? Description { get; set; }
    [XmlAttribute("conid")] public string? Conid { get; set; }
    [XmlAttribute("securityID")] public string? SecurityId { get; set; }
    [XmlAttribute("cusip")] public string? Cusip { get; set; }
    [XmlAttribute("isin")] public string? Isin { get; set; }
    [XmlAttribute("figi")] public string? Figi { get; set; }
    [XmlAttribute("listingExchange")] public string? ListingExchange { get; set; }
    [XmlAttribute("subCategory")] public string? Subcategory { get; set; }
    [XmlAttribute("multiplier")] public string? Multiplier { get; set; }
    [XmlAttribute("expiry")] public string? Expiry { get; set; }
    [XmlAttribute("strike")] public string? Strike { get; set; }
    [XmlAttribute("issuer")] public string? Issuer { get; set; }
    [XmlAttribute("maturity")] public string? Maturity { get; set; }
    [XmlAttribute("issueDate")] public string? IssueDate { get; set; }
    [XmlAttribute("code")] public string? Code { get; set; }
    [XmlAttribute("type")] public string? Type { get; set; }
}
