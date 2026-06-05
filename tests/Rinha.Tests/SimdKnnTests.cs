using Rinha.Api;

namespace Rinha.Tests;

public class SimdKnnTests
{
    private const int Stride = Knn.Stride; // 16
    private const int Dim = Knn.Dim;       // 14

    [Fact]
    public void Simd_BateComEscalar_CasoPequeno()
    {
        var query = new byte[Stride];
        for (int j = 0; j < Dim; j++) query[j] = 100;

        byte[] values = [100, 101, 102, 103, 104, 200, 210];
        var vectors = new byte[values.Length * Stride];
        for (int i = 0; i < values.Length; i++)
            for (int j = 0; j < Dim; j++)
                vectors[i * Stride + j] = values[i];
        byte[] labels = [0, 1, 1, 1, 0, 1, 1];

        int simd = Knn.CountFraudInTop5Simd(query, vectors, labels);
        int scalar = Knn.CountFraudInTop5(query, vectors, labels);

        Assert.Equal(3, simd);
        Assert.Equal(scalar, simd);
    }

    [Fact]
    public void Simd_BateComEscalar_Fuzz()
    {
        var rng = new Random(12345);
        const int n = 2000;

        for (int trial = 0; trial < 50; trial++)
        {
            var query = new byte[Stride];
            for (int j = 0; j < Dim; j++) query[j] = (byte)rng.Next(256);

            var vectors = new byte[n * Stride];
            var labels = new byte[n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < Dim; j++) vectors[i * Stride + j] = (byte)rng.Next(256);
                labels[i] = (byte)rng.Next(2);
            }

            int simd = Knn.CountFraudInTop5Simd(query, vectors, labels);
            int scalar = Knn.CountFraudInTop5(query, vectors, labels);

            Assert.Equal(scalar, simd);
        }
    }
}
