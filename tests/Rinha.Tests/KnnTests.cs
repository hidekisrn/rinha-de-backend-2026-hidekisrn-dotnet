using Rinha.Api;

namespace Rinha.Tests;

public class KnnTests
{
    private const int Dim = 14;

    private static byte[] FlatBytes(params byte[] values)
    {
        var v = new byte[values.Length * Dim];
        for (int i = 0; i < values.Length; i++)
            for (int j = 0; j < Dim; j++)
                v[i * Dim + j] = values[i];
        return v;
    }

    [Fact]
    public void Int8_SelecionaOs5MaisProximos_e_ContaFraudes()
    {
        var query = new byte[Dim];
        Array.Fill(query, (byte)100);

        var vectors = FlatBytes(100, 101, 102, 103, 104, 200, 210);
        var labels = new byte[] { 0, 1, 1, 1, 0, 1, 1 };

        int fraud = Knn.CountFraudInTop5(query, vectors, labels);

        Assert.Equal(3, fraud);
    }

    [Fact]
    public void Float_MesmaLogica()
    {
        var query = new float[Dim];
        Array.Fill(query, 0.5f);

        var values = new[] { 0.50f, 0.51f, 0.52f, 0.53f, 0.54f, 0.9f, 0.95f };
        var vectors = new float[values.Length * Dim];
        for (int i = 0; i < values.Length; i++)
            for (int j = 0; j < Dim; j++)
                vectors[i * Dim + j] = values[i];
        var labels = new[] { false, true, true, true, false, true, true };

        int fraud = Knn.CountFraudInTop5(query, vectors, labels);

        Assert.Equal(3, fraud);
    }
}
