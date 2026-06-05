using Rinha.Api;

namespace Rinha.Tests;

public class VectorizerTests
{
    private static readonly Normalization Norm = new(
        MaxAmount: 10000,
        MaxInstallments: 12,
        AmountVsAvgRatio: 10,
        MaxMinutes: 1440,
        MaxKm: 1000,
        MaxTxCount24h: 20,
        MaxMerchantAvgAmount: 10000);

    private static readonly Dictionary<string, double> Mcc = new()
    {
        ["5411"] = 0.15,
        ["5812"] = 0.30,
        ["5912"] = 0.20,
        ["5944"] = 0.45,
        ["7801"] = 0.80,
        ["7802"] = 0.75,
        ["7995"] = 0.85,
        ["4511"] = 0.35,
        ["5311"] = 0.25,
        ["5999"] = 0.50,
    };

    private static double Risk(string mcc) => Mcc.TryGetValue(mcc, out var r) ? r : 0.5;

    private static float[] Vectorize(FraudScoreRequest req)
    {
        var dest = new float[Vectorizer.Dim];
        Vectorizer.Vectorize(req, Norm, Risk, dest);
        return dest;
    }

    [Fact]
    public void Exemplo1_TransacaoLegitima_ProduzVetorEsperado()
    {
        var req = new FraudScoreRequest(
            Id: "tx-1329056812",
            Transaction: new TransactionInfo(41.12, 2, DateTimeOffset.Parse("2026-03-11T18:45:53Z")),
            Customer: new CustomerInfo(82.24, 3, ["MERC-003", "MERC-016"]),
            Merchant: new MerchantInfo("MERC-016", "5411", 60.25),
            Terminal: new TerminalInfo(IsOnline: false, CardPresent: true, KmFromHome: 29.2331036248),
            LastTransaction: null);

        float[] expected = [0.0041f, 0.1667f, 0.05f, 0.7826f, 0.3333f, -1f, -1f, 0.0292f, 0.15f, 0f, 1f, 0f, 0.15f, 0.006f];

        AssertVector(expected, Vectorize(req));
    }

    [Fact]
    public void Exemplo2_TransacaoFraudulenta_ProduzVetorEsperado()
    {
        var req = new FraudScoreRequest(
            Id: "tx-3330991687",
            Transaction: new TransactionInfo(9505.97, 10, DateTimeOffset.Parse("2026-03-14T05:15:12Z")),
            Customer: new CustomerInfo(81.28, 20, ["MERC-008", "MERC-007", "MERC-005"]),
            Merchant: new MerchantInfo("MERC-068", "7802", 54.86),
            Terminal: new TerminalInfo(IsOnline: false, CardPresent: true, KmFromHome: 952.27),
            LastTransaction: null);

        float[] expected = [0.9506f, 0.8333f, 1.0f, 0.2174f, 0.8333f, -1f, -1f, 0.9523f, 1.0f, 0f, 1f, 1f, 0.75f, 0.0055f];

        AssertVector(expected, Vectorize(req));
    }

    [Fact]
    public void MccDesconhecido_UsaPadrao_0_5()
    {
        var req = new FraudScoreRequest(
            Id: "tx-x",
            Transaction: new TransactionInfo(100, 1, DateTimeOffset.Parse("2026-01-01T00:00:00Z")),
            Customer: new CustomerInfo(100, 0, []),
            Merchant: new MerchantInfo("MERC-999", "9999", 100),
            Terminal: new TerminalInfo(false, true, 0),
            LastTransaction: null);

        Assert.Equal(0.5f, Vectorize(req)[12], precision: 4);
    }

    private static void AssertVector(float[] expected, float[] actual)
    {
        Assert.Equal(Vectorizer.Dim, actual.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.True(MathF.Abs(expected[i] - actual[i]) < 1e-3f,
                $"dim {i}: esperado {expected[i]}, obtido {actual[i]}");
    }
}
