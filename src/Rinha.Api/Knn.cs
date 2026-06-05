using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Rinha.Api;

public static class Knn
{
    public const int Dim = Vectorizer.Dim;
    public const int Stride = 16;
    public const int K = 5;

    public static int DistanceSimd(Vector128<byte> q, ref byte vbase, int i)
    {
        var r = Vector128.LoadUnsafe(ref vbase, (nuint)i * Stride);
        var d = Vector128.Max(q, r) - Vector128.Min(q, r);
        (var dlo, var dhi) = Vector128.Widen(d);
        var slo = dlo * dlo;
        var shi = dhi * dhi;
        (var a0, var a1) = Vector128.Widen(slo);
        (var a2, var a3) = Vector128.Widen(shi);
        return (int)Vector128.Sum(a0 + a1 + a2 + a3);
    }

    public static Vector128<byte> LoadQuery(ReadOnlySpan<byte> query) =>
        Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(query));

    public static void ScanBlock(
        Vector128<byte> q, ref byte vbase, ref byte lbase, int start, int len,
        Span<int> bestDist, Span<bool> bestFraud, ref int worst)
    {
        int end = start + len;
        for (int i = start; i < end; i++)
        {
            int s = DistanceSimd(q, ref vbase, i);
            if (s < bestDist[worst])
            {
                bestDist[worst] = s;
                bestFraud[worst] = Unsafe.Add(ref lbase, i) != 0;
                worst = 0;
                for (int kk = 1; kk < K; kk++)
                    if (bestDist[kk] > bestDist[worst]) worst = kk;
            }
        }
    }

    public static int CountFraudIvf(
        ReadOnlySpan<byte> query, ReadOnlySpan<byte> centroids, ReadOnlySpan<int> cellOffsets,
        ReadOnlySpan<byte> vectors, ReadOnlySpan<byte> labels, int nprobe)
    {
        int k = cellOffsets.Length - 1;
        if (nprobe > k) nprobe = k;
        if (nprobe < 1) nprobe = 1;
        var q = LoadQuery(query);

        ref byte cbase = ref MemoryMarshal.GetReference(centroids);
        Span<int> probeCell = stackalloc int[nprobe];
        Span<int> probeDist = stackalloc int[nprobe];
        probeCell.Fill(-1);
        probeDist.Fill(int.MaxValue);
        int pworst = 0;
        for (int c = 0; c < k; c++)
        {
            int dc = DistanceSimd(q, ref cbase, c);
            if (dc < probeDist[pworst])
            {
                probeDist[pworst] = dc;
                probeCell[pworst] = c;
                pworst = 0;
                for (int t = 1; t < nprobe; t++)
                    if (probeDist[t] > probeDist[pworst]) pworst = t;
            }
        }

        Span<int> bestDist = stackalloc int[K];
        Span<bool> bestFraud = stackalloc bool[K];
        bestDist.Fill(int.MaxValue);
        int worst = 0;
        ref byte vbase = ref MemoryMarshal.GetReference(vectors);
        ref byte lbase = ref MemoryMarshal.GetReference(labels);
        for (int p = 0; p < nprobe; p++)
        {
            int c = probeCell[p];
            if (c < 0) continue;
            int start = cellOffsets[c];
            int len = cellOffsets[c + 1] - start;
            ScanBlock(q, ref vbase, ref lbase, start, len, bestDist, bestFraud, ref worst);
        }
        return CountTrue(bestFraud);
    }

    public static int CountFraudInTop5Simd(ReadOnlySpan<byte> query, ReadOnlySpan<byte> vectors, ReadOnlySpan<byte> labels)
    {
        int n = labels.Length;
        Span<int> bestDist = stackalloc int[K];
        Span<bool> bestFraud = stackalloc bool[K];
        bestDist.Fill(int.MaxValue);
        int worst = 0;

        ScanBlock(LoadQuery(query), ref MemoryMarshal.GetReference(vectors),
            ref MemoryMarshal.GetReference(labels), 0, n, bestDist, bestFraud, ref worst);

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
