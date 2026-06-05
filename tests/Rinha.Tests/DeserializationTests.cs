using System.Text.Json;
using Rinha.Api;

namespace Rinha.Tests;

public class DeserializationTests
{
    private const string FraudJson = """
        {
          "id":"tx-3330991687",
          "transaction":{"amount":9505.97,"installments":10,"requested_at":"2026-03-14T05:15:12Z"},
          "customer":{"avg_amount":81.28,"tx_count_24h":20,"known_merchants":["MERC-008","MERC-007","MERC-005"]},
          "merchant":{"id":"MERC-068","mcc":"7802","avg_amount":54.86},
          "terminal":{"is_online":false,"card_present":true,"km_from_home":952.27},
          "last_transaction":null
        }
        """;

    [Fact]
    public void Desserializa_ComoMinimalApi_WebDefaultsMaisContexto()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);

        var req = JsonSerializer.Deserialize<FraudScoreRequest>(FraudJson, options)!;

        Assert.Equal(20, req.Customer.TxCount24h);
        Assert.NotNull(req.Customer.KnownMerchants);
        Assert.Equal(3, req.Customer.KnownMerchants.Length);
        Assert.Equal(9505.97, req.Transaction.Amount);
    }

    [Fact]
    public void Desserializa_TodosOsCampos()
    {
        var req = JsonSerializer.Deserialize(FraudJson, AppJsonContext.Default.FraudScoreRequest)!;

        Assert.Equal("tx-3330991687", req.Id);
        Assert.Equal(9505.97, req.Transaction.Amount);
        Assert.Equal(10, req.Transaction.Installments);
        Assert.Equal(81.28, req.Customer.AvgAmount);
        Assert.Equal(20, req.Customer.TxCount24h);
        Assert.Equal(["MERC-008", "MERC-007", "MERC-005"], req.Customer.KnownMerchants);
        Assert.Equal("MERC-068", req.Merchant.Id);
        Assert.Equal("7802", req.Merchant.Mcc);
        Assert.False(req.Terminal.IsOnline);
        Assert.True(req.Terminal.CardPresent);
        Assert.Equal(952.27, req.Terminal.KmFromHome);
        Assert.Null(req.LastTransaction);
    }
}
