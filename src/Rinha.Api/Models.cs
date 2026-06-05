using System.Text.Json.Serialization;

namespace Rinha.Api;

public sealed record FraudScoreRequest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("transaction")] TransactionInfo Transaction,
    [property: JsonPropertyName("customer")] CustomerInfo Customer,
    [property: JsonPropertyName("merchant")] MerchantInfo Merchant,
    [property: JsonPropertyName("terminal")] TerminalInfo Terminal,
    [property: JsonPropertyName("last_transaction")] LastTransactionInfo? LastTransaction);

public sealed record TransactionInfo(
    [property: JsonPropertyName("amount")] double Amount,
    [property: JsonPropertyName("installments")] int Installments,
    [property: JsonPropertyName("requested_at")] DateTimeOffset RequestedAt);

public sealed record CustomerInfo(
    [property: JsonPropertyName("avg_amount")] double AvgAmount,
    [property: JsonPropertyName("tx_count_24h")] int TxCount24h,
    [property: JsonPropertyName("known_merchants")] string[] KnownMerchants);

public sealed record MerchantInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("mcc")] string Mcc,
    [property: JsonPropertyName("avg_amount")] double AvgAmount);

public sealed record TerminalInfo(
    [property: JsonPropertyName("is_online")] bool IsOnline,
    [property: JsonPropertyName("card_present")] bool CardPresent,
    [property: JsonPropertyName("km_from_home")] double KmFromHome);

public sealed record LastTransactionInfo(
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("km_from_current")] double KmFromCurrent);

public sealed record FraudScoreResponse(
    [property: JsonPropertyName("approved")] bool Approved,
    [property: JsonPropertyName("fraud_score")] double FraudScore);


public sealed record ReferenceEntry(
    [property: JsonPropertyName("vector")] float[] Vector,
    [property: JsonPropertyName("label")] string Label);

public sealed record Normalization(
    [property: JsonPropertyName("max_amount")] double MaxAmount,
    [property: JsonPropertyName("max_installments")] double MaxInstallments,
    [property: JsonPropertyName("amount_vs_avg_ratio")] double AmountVsAvgRatio,
    [property: JsonPropertyName("max_minutes")] double MaxMinutes,
    [property: JsonPropertyName("max_km")] double MaxKm,
    [property: JsonPropertyName("max_tx_count_24h")] double MaxTxCount24h,
    [property: JsonPropertyName("max_merchant_avg_amount")] double MaxMerchantAvgAmount);

[JsonSerializable(typeof(FraudScoreRequest))]
[JsonSerializable(typeof(FraudScoreResponse))]
[JsonSerializable(typeof(Normalization))]
[JsonSerializable(typeof(Dictionary<string, double>))]
[JsonSerializable(typeof(ReferenceEntry))]
public partial class AppJsonContext : JsonSerializerContext;
