namespace Rinha.Api;

public static class Quantizer
{
    public const int SchemeVersion = 2;

    public static byte Quantize(float x)
    {
        float t = (x + 1f) * (255f / 2f);
        int q = (int)MathF.Round(t, MidpointRounding.AwayFromZero);
        if (q < 0) q = 0;
        else if (q > 255) q = 255;
        return (byte)q;
    }

    public static void Quantize(ReadOnlySpan<float> src, Span<byte> dest)
    {
        for (int i = 0; i < src.Length; i++)
            dest[i] = Quantize(src[i]);
    }
}
