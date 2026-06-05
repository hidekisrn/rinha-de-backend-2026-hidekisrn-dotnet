namespace Rinha.Api;

public static class Vectorizer
{

    public const int Dim = 14;
    public const float Sentinel = -1f;
    private static double Clamp01(double x) => x < 0d ? 0d : x > 1d ? 1d : x;

    public static void Vectorize(FraudScoreRequest req, Normalization n, Func<string, double> mccRisk, Span<float> dest)
    {
        var transaction = req.Transaction;
        var customer = req.Customer;
        var merchant = req.Merchant;
        var terminal = req.Terminal;
        var utc = transaction.RequestedAt.UtcDateTime;
        int dayMon0 = ((int)utc.DayOfWeek + 6) % 7;

        dest[0] = (float)Clamp01(transaction.Amount / n.MaxAmount);
        dest[1] = (float)Clamp01(transaction.Installments / n.MaxInstallments);
        dest[2] = (float)Clamp01(transaction.Amount / customer.AvgAmount / n.AmountVsAvgRatio);
        dest[3] = (float)(utc.Hour / 23d);
        dest[4] = (float)(dayMon0 / 6d);

        if (req.LastTransaction is { } last)
        {
            double minutes = (utc - last.Timestamp.UtcDateTime).TotalMinutes;
            dest[5] = (float)Clamp01(minutes / n.MaxMinutes);
            dest[6] = (float)Clamp01(last.KmFromCurrent / n.MaxKm);
        }
        else
        {
            dest[5] = Sentinel;
            dest[6] = Sentinel;
        }

        dest[7] = (float)Clamp01(terminal.KmFromHome / n.MaxKm);
        dest[8] = (float)Clamp01(customer.TxCount24h / n.MaxTxCount24h);
        dest[9] = terminal.IsOnline ? 1f : 0f;
        dest[10] = terminal.CardPresent ? 1f : 0f;
        dest[11] = Contains(customer.KnownMerchants, merchant.Id) ? 0f : 1f;
        dest[12] = (float)mccRisk(merchant.Mcc);
        dest[13] = (float)Clamp01(merchant.AvgAmount / n.MaxMerchantAvgAmount);
    }

    private static bool Contains(string[]? merchants, string id)
    {
        if (merchants is null) return false;
        foreach (var x in merchants)
            if (x == id) return true;
        return false;
    }
}
