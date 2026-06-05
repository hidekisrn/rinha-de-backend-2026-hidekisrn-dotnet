using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Rinha.Api;

public static class Knn
{
    public const int Dim = Vectorizer.Dim;
    public const int Stride = 16;
    private const int K = 5;

    public static int CountFraudInTop5Simd(ReadOnlySpan<byte> query, ReadOnlySpan<byte> vectors, ReadOnlySpan<byte> labels)
    {
        int n = labels.Length;
        ref byte vbase = ref MemoryMarshal.GetReference(vectors);
        var q = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(query));

        Span<int> bestDist = stackalloc int[K];
        Span<bool> bestFraud = stackalloc bool[K];
        bestDist.Fill(int.MaxValue);
        int worst = 0;

        for (int i = 0; i < n; i++)
        {
            var r = Vector128.LoadUnsafe(ref vbase, (nuint)i * Stride);

            var d = Vector128.Max(q, r) - Vector128.Min(q, r);
            (var dlo, var dhi) = Vector128.Widen(d);
            var slo = dlo * dlo;
            var shi = dhi * dhi;
            (var a0, var a1) = Vector128.Widen(slo);
            (var a2, var a3) = Vector128.Widen(shi);
            int s = (int)Vector128.Sum(a0 + a1 + a2 + a3);

            if (s < bestDist[worst])
            {
                bestDist[worst] = s;
                bestFraud[worst] = labels[i] != 0;
                worst = 0;
                for (int kk = 1; kk < K; kk++)
                    if (bestDist[kk] > bestDist[worst]) worst = kk;
            }
        }

        return CountTrue(bestFraud);
    }

    public static int CountFraudInTop5(ReadOnlySpan<byte> query, ReadOnlySpan<byte> vectors, ReadOnlySpan<byte> labels)
    {
        int n = labels.Length;
        int stride = n == 0 ? Dim : vectors.Length / n;

        Span<int> bestDist = stackalloc int[K];
        Span<bool> bestFraud = stackalloc bool[K];
        bestDist.Fill(int.MaxValue);
        int worst = 0;

        for (int i = 0; i < n; i++)
        {
            int off = i * stride;
            int s = 0;
            for (int j = 0; j < Dim; j++)
            {
                int diff = query[j] - vectors[off + j];
                s += diff * diff;
            }

            if (s < bestDist[worst])
            {
                bestDist[worst] = s;
                bestFraud[worst] = labels[i] != 0;
                worst = 0;
                for (int kk = 1; kk < K; kk++)
                    if (bestDist[kk] > bestDist[worst]) worst = kk;
            }
        }

        return CountTrue(bestFraud);
    }

    public static int CountFraudInTop5(ReadOnlySpan<float> query, ReadOnlySpan<float> vectors, ReadOnlySpan<bool> labels)
    {
        int n = labels.Length;
        Span<float> bestDist = stackalloc float[K];
        Span<bool> bestFraud = stackalloc bool[K];
        bestDist.Fill(float.MaxValue);
        int worst = 0;

        for (int i = 0; i < n; i++)
        {
            int off = i * Dim;
            float s = 0f;
            for (int j = 0; j < Dim; j++)
            {
                float diff = query[j] - vectors[off + j];
                s += diff * diff;
            }

            if (s < bestDist[worst])
            {
                bestDist[worst] = s;
                bestFraud[worst] = labels[i];
                worst = 0;
                for (int kk = 1; kk < K; kk++)
                    if (bestDist[kk] > bestDist[worst]) worst = kk;
            }
        }

        return CountTrue(bestFraud);
    }

    private static int CountTrue(ReadOnlySpan<bool> flags)
    {
        int c = 0;
        for (int k = 0; k < flags.Length; k++)
            if (flags[k]) c++;
        return c;
    }
}
