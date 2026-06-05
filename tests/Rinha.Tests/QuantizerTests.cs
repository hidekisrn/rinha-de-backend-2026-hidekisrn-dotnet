using Rinha.Api;

namespace Rinha.Tests;

public class QuantizerTests
{
    [Fact]
    public void Sentinela_e_Extremos()
    {
        Assert.Equal(0, Quantizer.Quantize(-1f));
        Assert.Equal(255, Quantizer.Quantize(1f));
        Assert.Equal(128, Quantizer.Quantize(0f));
    }

    [Fact]
    public void Clampa_ForaDoIntervalo()
    {
        Assert.Equal(0, Quantizer.Quantize(-5f));
        Assert.Equal(255, Quantizer.Quantize(5f));
    }

    [Fact]
    public void Monotonico_PreservaOrdem()
    {
        byte prev = Quantizer.Quantize(-1f);
        for (float x = -1f; x <= 1f; x += 0.01f)
        {
            byte q = Quantizer.Quantize(x);
            Assert.True(q >= prev, $"quebra de monotonicidade em x={x}: {q} < {prev}");
            prev = q;
        }
    }
}
