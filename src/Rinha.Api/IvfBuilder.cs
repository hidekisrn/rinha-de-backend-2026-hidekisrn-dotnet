namespace Rinha.Api;

public static class IvfBuilder
{
    private const int Dim = Knn.Dim;       // 14
    private const int Stride = Knn.Stride; // 16

    public sealed record Result(
        byte[] CentroidsQ,
        int[] CellOffsets,
        byte[] Vectors,
        byte[] Labels,
        int K);

    public static Result Build(ReadOnlySpan<byte> vectors, ReadOnlySpan<byte> labels,
        int k, int maxIter, int seed, Action<string>? log = null)
    {
        int n = labels.Length;
        if (k > n) k = n;

        var cent = new float[k * Dim];
        var rng = new Random(seed);
        for (int c = 0; c < k; c++)
        {
            int src = (int)((long)c * n / k);
            for (int j = 0; j < Dim; j++)
                cent[c * Dim + j] = vectors[src * Stride + j];
        }

        var assign = new int[n];
        var vecArr = vectors.ToArray();
        var labArr = labels.ToArray();

        for (int iter = 0; iter < maxIter; iter++)
        {
            long changed = AssignAll(vecArr, cent, k, assign);
            RecomputeCentroids(vecArr, assign, cent, k, n, rng);
            log?.Invoke($"k-means iter {iter + 1}/{maxIter}: {changed:N0} reatribuições");
            if (changed <= n / 1000) break;
        }

        var counts = new int[k];
        for (int i = 0; i < n; i++) counts[assign[i]]++;
        var offsets = new int[k + 1];
        for (int c = 0; c < k; c++) offsets[c + 1] = offsets[c] + counts[c];

        var outVec = new byte[(long)n * Stride <= int.MaxValue ? n * Stride : throw new InvalidOperationException("blob > 2GB")];
        var outLab = new byte[n];
        var cursor = new int[k];
        Array.Copy(offsets, cursor, k);
        for (int i = 0; i < n; i++)
        {
            int c = assign[i];
            int pos = cursor[c]++;
            Array.Copy(vecArr, i * Stride, outVec, pos * Stride, Stride);
            outLab[pos] = labArr[i];
        }

        var centQ = new byte[k * Stride];
        for (int c = 0; c < k; c++)
            for (int j = 0; j < Dim; j++)
            {
                int v = (int)MathF.Round(cent[c * Dim + j]);
                centQ[c * Stride + j] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
            }

        return new Result(centQ, offsets, outVec, outLab, k);
    }

    private static long AssignAll(byte[] vec, float[] cent, int k, int[] assign)
    {
        long changed = 0;
        Parallel.For(0, assign.Length,
            () => 0L,
            (i, _, local) =>
            {
                int off = i * Stride;
                int best = 0;
                float bestD = float.MaxValue;
                for (int c = 0; c < k; c++)
                {
                    int cb = c * Dim;
                    float s = 0f;
                    for (int j = 0; j < Dim; j++)
                    {
                        float diff = vec[off + j] - cent[cb + j];
                        s += diff * diff;
                    }
                    if (s < bestD) { bestD = s; best = c; }
                }
                if (assign[i] != best) { assign[i] = best; local++; }
                return local;
            },
            local => Interlocked.Add(ref changed, local));
        return changed;
    }

    private static void RecomputeCentroids(byte[] vec, int[] assign, float[] cent, int k, int n, Random rng)
    {
        var sum = new double[k * Dim];
        var cnt = new int[k];
        for (int i = 0; i < n; i++)
        {
            int c = assign[i];
            int off = i * Stride;
            int cb = c * Dim;
            for (int j = 0; j < Dim; j++) sum[cb + j] += vec[off + j];
            cnt[c]++;
        }
        for (int c = 0; c < k; c++)
        {
            if (cnt[c] == 0)
            {
                int src = rng.Next(n);
                for (int j = 0; j < Dim; j++) cent[c * Dim + j] = vec[src * Stride + j];
            }
            else
            {
                int cb = c * Dim;
                for (int j = 0; j < Dim; j++) cent[cb + j] = (float)(sum[cb + j] / cnt[c]);
            }
        }
    }
}
