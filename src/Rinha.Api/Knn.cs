namespace Rinha.Api;

public static class Knn
{
    private const int Dim = Vectorizer.Dim;
    private const int K = 5;

    public static int CountFraudInTop5(ReadOnlySpan<byte> query, ReadOnlySpan<byte> vectors, ReadOnlySpan<byte> labels)
    {
        int labelsLength = labels.Length;
        Span<int> bestDist = stackalloc int[K];
        Span<bool> bestFraud = stackalloc bool[K];
        bestDist.Fill(int.MaxValue);
        int worst = 0;

        for (int i = 0; i < labelsLength; i++)
        {
            int off = i * Dim;
            int s = 0;
            for (int j = 0; j < Dim; j++)
            {
                int d = query[j] - vectors[off + j];
                s += d * d;
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

        int fraud = 0;
        for (int kk = 0; kk < K; kk++)
            if (bestFraud[kk]) fraud++;
        return fraud;
    }

    public static int CountFraudInTop5(ReadOnlySpan<float> query, ReadOnlySpan<float> vectors, ReadOnlySpan<bool> labels)
    {
        int labelsLength = labels.Length;
        Span<float> bestDist = stackalloc float[K];
        Span<bool> bestFraud = stackalloc bool[K];
        bestDist.Fill(float.MaxValue);
        int worst = 0;

        for (int i = 0; i < labelsLength; i++)
        {
            int off = i * Dim;
            float s = 0f;
            for (int j = 0; j < Dim; j++)
            {
                float d = query[j] - vectors[off + j];
                s += d * d;
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

        int fraud = 0;
        for (int kk = 0; kk < K; kk++)
            if (bestFraud[kk]) fraud++;
        return fraud;
    }
}
