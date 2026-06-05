using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Rinha.Api;

public sealed class ReferenceStore(IConfiguration configuration, ILogger<ReferenceStore> logger) : IDisposable
{
    private const int Dim = Vectorizer.Dim;
    private const int HeaderSize = 16;
    private const int Magic = 0x00385152; // "RQ8\0"
    private const int DefaultExpectedVectors = 3_000_000;
    private const string BlobFileName = "references.q8.bin";

    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private unsafe byte* _basePtr;
    private unsafe byte* _vecPtr;
    private unsafe byte* _labelPtr;

    public Normalization Normalization { get; private set; } = null!;
    public IReadOnlyDictionary<string, double> MccRisk { get; private set; } = null!;
    public long VectorCount { get; private set; }
    public long FraudCount { get; private set; }
    public long LegitCount { get; private set; }
    public bool IsReady { get; private set; }

    public double RiskFor(string mcc) => MccRisk.TryGetValue(mcc, out var r) ? r : 0.5;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var dir = ResolveResourcesPath();
        logger.LogInformation("Carregando recursos de {Dir}", dir);

        await using (var fs = File.OpenRead(Path.Combine(dir, "normalization.json")))
            Normalization = (await JsonSerializer.DeserializeAsync(fs, AppJsonContext.Default.Normalization, ct))!;

        await using (var fs = File.OpenRead(Path.Combine(dir, "mcc_risk.json")))
            MccRisk = (await JsonSerializer.DeserializeAsync(fs, AppJsonContext.Default.DictionaryStringDouble, ct))!;
        logger.LogInformation("Normalização e {Count} MCCs carregados (default 0.5).", MccRisk.Count);

        var blobPath = Path.Combine(dir, BlobFileName);
        if (!IsBlobValid(blobPath))
        {
            int hint = configuration.GetValue("EXPECTED_VECTORS", DefaultExpectedVectors);
            await BuildBlobAsync(Path.Combine(dir, "references.json.gz"), blobPath, hint, ct);
        }
        else
        {
            logger.LogInformation("Blob int8 válido encontrado em {Path}; reaproveitando.", blobPath);
        }

        OpenBlob(blobPath);
        IsReady = true;
        sw.Stop();

        logger.LogInformation(
            "Dataset pronto: {Total:N0} vetores ({Fraud:N0} fraude / {Legit:N0} legítimo) em {Elapsed}. Blob int8 ~{Mb} MB (mmap, compartilhável).",
            VectorCount, FraudCount, LegitCount, sw.Elapsed, (VectorCount * (Knn.Stride + 1) + HeaderSize) / (1024 * 1024));
    }

    public unsafe int CountFraudInTop5(ReadOnlySpan<byte> query)
    {
        var vectors = new ReadOnlySpan<byte>(_vecPtr, checked((int)(VectorCount * Knn.Stride)));
        var labels = new ReadOnlySpan<byte>(_labelPtr, checked((int)VectorCount));
        return Knn.CountFraudInTop5Simd(query, vectors, labels);
    }

    private async Task BuildBlobAsync(string gzPath, string blobPath, int expectedHint, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("Blob ausente/desatualizado: construindo {Path} a partir do .gz...", blobPath);

        var tmpPath = blobPath + ".tmp";
        long count = 0;
        var labels = new List<byte>(expectedHint);
        var qbuf = new byte[Knn.Stride];
        await using (var outFs = new FileStream(tmpPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            outFs.Write(new byte[HeaderSize]);
            await using var gz = File.OpenRead(gzPath);
            await using var raw = new GZipStream(gz, CompressionMode.Decompress);
            await foreach (var entry in JsonSerializer.DeserializeAsyncEnumerable(raw, AppJsonContext.Default.ReferenceEntry, ct))
            {
                if (entry is null || entry.Vector.Length != Dim)
                    throw new InvalidDataException($"Entrada de referência inválida (dimensão != {Dim}).");

                Quantizer.Quantize(entry.Vector, qbuf);
                outFs.Write(qbuf);
                labels.Add(entry.Label == "fraud" ? (byte)1 : (byte)0);
                count++;
            }

            outFs.Write(CollectionsMarshal.AsSpan(labels));

            Span<byte> header = stackalloc byte[HeaderSize];
            BinaryPrimitives.WriteInt32LittleEndian(header[..4], Magic);
            BinaryPrimitives.WriteInt32LittleEndian(header[4..8], Quantizer.SchemeVersion);
            BinaryPrimitives.WriteInt64LittleEndian(header[8..16], count);
            outFs.Position = 0;
            outFs.Write(header);
        }

        File.Move(tmpPath, blobPath, overwrite: true);
        sw.Stop();
        logger.LogInformation("Blob construído: {Count:N0} vetores em {Elapsed}.", count, sw.Elapsed);
    }

    private bool IsBlobValid(string blobPath)
    {
        if (!File.Exists(blobPath)) return false;
        try
        {
            using var fs = File.OpenRead(blobPath);
            Span<byte> header = stackalloc byte[HeaderSize];
            if (fs.Read(header) != HeaderSize) return false;

            int magic = BinaryPrimitives.ReadInt32LittleEndian(header[..4]);
            int version = BinaryPrimitives.ReadInt32LittleEndian(header[4..8]);
            long count = BinaryPrimitives.ReadInt64LittleEndian(header[8..16]);
            long expectedLen = HeaderSize + count * (Knn.Stride + 1);

            return magic == Magic && version == Quantizer.SchemeVersion && count > 0 && fs.Length == expectedLen;
        }
        catch
        {
            return false;
        }
    }

    private unsafe void OpenBlob(string blobPath)
    {
        long len = new FileInfo(blobPath).Length;
        _mmf = MemoryMappedFile.CreateFromFile(blobPath, FileMode.Open, mapName: null, 0, MemoryMappedFileAccess.Read);
        _accessor = _mmf.CreateViewAccessor(0, len, MemoryMappedFileAccess.Read);

        byte* ptr = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        _basePtr = ptr;

        long count = BinaryPrimitives.ReadInt64LittleEndian(new ReadOnlySpan<byte>(_basePtr + 8, 8));
        VectorCount = count;
        _vecPtr = _basePtr + HeaderSize;
        _labelPtr = _vecPtr + count * Knn.Stride;

        long fraud = 0;
        var labels = new ReadOnlySpan<byte>(_labelPtr, checked((int)count));
        for (int i = 0; i < labels.Length; i++)
            if (labels[i] != 0) fraud++;
        FraudCount = fraud;
        LegitCount = count - fraud;
    }

    private string ResolveResourcesPath()
    {
        var configured = configuration["RESOURCES_PATH"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "resources");
            if (File.Exists(Path.Combine(candidate, "normalization.json")))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Não encontrei a pasta 'resources' com normalization.json. " +
            "Defina RESOURCES_PATH ou rode a partir do repositório.");
    }

    public unsafe void Dispose()
    {
        if (_basePtr is not null && _accessor is not null)
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _basePtr = null;
        }
        _accessor?.Dispose();
        _mmf?.Dispose();
    }
}
