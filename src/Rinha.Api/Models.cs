using System.Text.Json.Serialization;

namespace Rinha.Api;

public sealed record FraudScoreRequest(
    string Id,
    TransactionInfo Transaction,
    CustomerInfo Customer,
    MerchantInfo Merchant,
    TerminalInfo Terminal,
    LastTransactionInfo? LastTransaction);

public sealed record TransactionInfo(
    double Amount,
    int Installments,
    DateTimeOffset RequestedAt);

public sealed record CustomerInfo(
    double AvgAmount,
    [property: JsonPropertyName("tx_count_24h")] int TxCount24h,
    string[] KnownMerchants);

public sealed record MerchantInfo(
    string Id,
    string Mcc,
    double AvgAmount);

public sealed record TerminalInfo(
    bool IsOnline,
    bool CardPresent,
    double KmFromHome);

public sealed record LastTransactionInfo(
    DateTimeOffset Timestamp,
    double KmFromCurrent);

public sealed record FraudScoreResponse(
    bool Approved,
    double FraudScore);

public sealed record ReferenceEntry(
    float[] Vector,
    string Label);

public sealed record Normalization(
    [property: JsonPropertyName("max_amount")] double MaxAmount,
    [property: JsonPropertyName("max_installments")] double MaxInstallments,
    [property: JsonPropertyName("amount_vs_avg_ratio")] double AmountVsAvgRatio,
    [property: JsonPropertyName("max_minutes")] double MaxMinutes,
    [property: JsonPropertyName("max_km")] double MaxKm,
    [property: JsonPropertyName("max_tx_count_24h")] double MaxTxCount24h,
    [property: JsonPropertyName("max_merchant_avg_amount")] double MaxMerchantAvgAmount);


[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(FraudScoreRequest))]
[JsonSerializable(typeof(FraudScoreResponse))]
[JsonSerializable(typeof(Normalization))]
[JsonSerializable(typeof(Dictionary<string, double>))]
[JsonSerializable(typeof(ReferenceEntry))]
public partial class AppJsonContext : JsonSerializerContext;
