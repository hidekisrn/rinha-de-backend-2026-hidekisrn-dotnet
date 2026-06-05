using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rinha.Api;
using Xunit.Abstractions;

namespace Rinha.Tests;

public class QuantizationFidelityMeasurement(ITestOutputHelper output)
{
    private const int Dim = 14;

    [Fact]
    public async Task Medir_Float_vs_Int8_vs_Gabarito()
    {
        if (Environment.GetEnvironmentVariable("RUN_MEASUREMENT") != "1")
            return;

        int n = int.TryParse(Environment.GetEnvironmentVariable("MEASURE_N"), out var v) ? v : 1000;
        var spec = Environment.GetEnvironmentVariable("SPEC_DIR") ?? "/Users/sergio/Projects/rinha-de-backend-2026";
        var gzPath = Environment.GetEnvironmentVariable("GZ_PATH") ?? Path.Combine(spec, "resources", "references.json.gz");
        var testDataPath = Environment.GetEnvironmentVariable("TESTDATA_PATH") ?? Path.Combine(spec, "test", "test-data.json");
        var normPath = Path.Combine(spec, "resources", "normalization.json");
        var mccPath = Path.Combine(spec, "resources", "mcc_risk.json");

        foreach (var p in new[] { gzPath, testDataPath, normPath, mccPath })
            if (!File.Exists(p)) { output.WriteLine($"[skip] arquivo ausente: {p}"); return; }

        output.WriteLine("Carregando dataset float do .gz...");
        var floatList = new List<float>(3_000_000 * Dim);
        var labelList = new List<bool>(3_000_000);
        await using (var gz = File.OpenRead(gzPath))
        await using (var raw = new GZipStream(gz, CompressionMode.Decompress))
        {
            await foreach (var e in JsonSerializer.DeserializeAsyncEnumerable(raw, AppJsonContext.Default.ReferenceEntry))
            {
                floatList.AddRange(e!.Vector);
                labelList.Add(e.Label == "fraud");
            }
        }
        var floatVecs = floatList.ToArray();
        var boolLabels = labelList.ToArray();
        var byteVecs = new byte[floatVecs.Length];
        for (int i = 0; i < floatVecs.Length; i++) byteVecs[i] = Quantizer.Quantize(floatVecs[i]);
        var byteLabels = new byte[boolLabels.Length];
        for (int i = 0; i < boolLabels.Length; i++) byteLabels[i] = boolLabels[i] ? (byte)1 : (byte)0;
        output.WriteLine($"Dataset: {boolLabels.Length:N0} vetores. float={floatVecs.Length * 4 / (1024 * 1024)}MB int8={byteVecs.Length / (1024 * 1024)}MB");

        var norm = JsonSerializer.Deserialize(File.ReadAllText(normPath), AppJsonContext.Default.Normalization)!;
        var mcc = JsonSerializer.Deserialize(File.ReadAllText(mccPath), AppJsonContext.Default.DictionaryStringDouble)!;
        double Risk(string m) => mcc.TryGetValue(m, out var r) ? r : 0.5;

        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var testFile = JsonSerializer.Deserialize<TestFile>(File.ReadAllText(testDataPath), opts)!;
        var sample = testFile.Entries.Take(n).ToArray();
        output.WriteLine($"Amostra: {sample.Length:N0} requisições de {testFile.Entries.Length:N0}.");

        long floatCorrect = 0, int8Correct = 0, int8VsFloatDiffer = 0, scoreDiffer = 0;
        long floatFP = 0, floatFN = 0, int8FP = 0, int8FN = 0;
        long floatScoreExact = 0, int8ScoreExact = 0;

        Parallel.For(0, sample.Length, i =>
        {
            var entry = sample[i];
            Span<float> qf = stackalloc float[Dim];
            Vectorizer.Vectorize(entry.Request, norm, Risk, qf);
            Span<byte> qb = stackalloc byte[Dim];
            Quantizer.Quantize(qf, qb);

            int fFraud = Knn.CountFraudInTop5(qf, floatVecs, boolLabels);
            int qFraud = Knn.CountFraudInTop5(qb, byteVecs, byteLabels);

            bool fApprove = fFraud / 5.0 < 0.6;
            bool qApprove = qFraud / 5.0 < 0.6;
            bool exp = entry.ExpectedApproved;

            if (fApprove == exp) Interlocked.Increment(ref floatCorrect);
            else if (exp && !fApprove) Interlocked.Increment(ref floatFP);
            else Interlocked.Increment(ref floatFN);

            if (qApprove == exp) Interlocked.Increment(ref int8Correct);
            else if (exp && !qApprove) Interlocked.Increment(ref int8FP);
            else Interlocked.Increment(ref int8FN);

            if (qApprove != fApprove) Interlocked.Increment(ref int8VsFloatDiffer);
            if (qFraud != fFraud) Interlocked.Increment(ref scoreDiffer);

            if (Math.Abs(fFraud / 5.0 - entry.ExpectedFraudScore) < 1e-6) Interlocked.Increment(ref floatScoreExact);
            if (Math.Abs(qFraud / 5.0 - entry.ExpectedFraudScore) < 1e-6) Interlocked.Increment(ref int8ScoreExact);
        });

        int total = sample.Length;
        var lines = new[]
        {
            $"=== RESULTADO (N={total}) ===",
            $"float32 vs gabarito : acertos={floatCorrect} ({100.0 * floatCorrect / total:F2}%)  FP={floatFP}  FN={floatFN}",
            $"int8    vs gabarito : acertos={int8Correct} ({100.0 * int8Correct / total:F2}%)  FP={int8FP}  FN={int8FN}",
            $"int8 vs float32     : decisões diferentes={int8VsFloatDiffer} ({100.0 * int8VsFloatDiffer / total:F2}%)  fraud_score diferente={scoreDiffer} ({100.0 * scoreDiffer / total:F2}%)",
            $"fraud_score EXATO   : float={floatScoreExact} ({100.0 * floatScoreExact / total:F2}%)  int8={int8ScoreExact} ({100.0 * int8ScoreExact / total:F2}%)  vs expected_fraud_score",
        };
        foreach (var l in lines) output.WriteLine(l);
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "m2-fidelity.txt"), lines);
    }

    private sealed record TestFile([property: JsonPropertyName("entries")] TestEntry[] Entries);

    private sealed record TestEntry(
        [property: JsonPropertyName("request")] FraudScoreRequest Request,
        [property: JsonPropertyName("expected_approved")] bool ExpectedApproved,
        [property: JsonPropertyName("expected_fraud_score")] double ExpectedFraudScore);
}
