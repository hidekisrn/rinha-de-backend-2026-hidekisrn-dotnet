using Rinha.Api;

namespace Rinha.Tests;

public class IvfTests
{
    private const int Stride = Knn.Stride;
    private const int Dim = Knn.Dim;
    private static (byte[] vec, byte[] lab) RandomDataset(Random rng, int n)
    {
        var vec = new byte[n * Stride];
        var lab = new byte[n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < Dim; j++) vec[i * Stride + j] = (byte)rng.Next(256);
            lab[i] = (byte)rng.Next(2);
        }
        return (vec, lab);
    }

    [Fact]
    public void Offsets_ParticionamTodosOsVetores()
    {
        var (vec, lab) = RandomDataset(new Random(7), 500);
        var ivf = IvfBuilder.Build(vec, lab, k: 8, maxIter: 10, seed: 1);

        Assert.Equal(0, ivf.CellOffsets[0]);
        Assert.Equal(500, ivf.CellOffsets[^1]);
        Assert.Equal(500 * Stride, ivf.Vectors.Length);
        Assert.Equal(500, ivf.Labels.Length);
    }

    [Fact]
    public void NprobeIgualK_ReproduzForcaBrutaExatamente()
    {
        var rng = new Random(99);
        const int n = 800, k = 16;
        var (vec, lab) = RandomDataset(rng, n);
        var ivf = IvfBuilder.Build(vec, lab, k, maxIter: 12, seed: 1);

        for (int trial = 0; trial < 30; trial++)
        {
            var query = new byte[Stride];
            for (int j = 0; j < Dim; j++) query[j] = (byte)rng.Next(256);

            int brute = Knn.CountFraudInTop5Simd(query, ivf.Vectors, ivf.Labels);
            int ivfAll = Knn.CountFraudIvf(query, ivf.CentroidsQ, ivf.CellOffsets, ivf.Vectors, ivf.Labels, nprobe: k);

            Assert.Equal(brute, ivfAll);
        }
    }
}
