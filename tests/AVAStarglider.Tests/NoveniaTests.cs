using AVAStarglider.Engine;

namespace AVAStarglider.Tests;

public class NoveniaTests
{
    [Fact]
    public void Power_lines_run_between_pairs_of_pylons_and_the_silos_are_well_spread()
    {
        var n = NoveniaBook.Build(0);
        Assert.Equal(6, n.Lines.Count);
        foreach (var l in n.Lines) Assert.Equal(NoveniaBook.PylonGap, NoveniaDef.Dist(l.X0, l.Y0, l.X1, l.Y1), 1);
        Assert.Equal(4, n.Silos.Count);
        foreach (var a in n.Silos)
            foreach (var b in n.Silos.Where(b => b != a))
                Assert.True(NoveniaDef.Dist(a.X, a.Y, b.X, b.Y) >= 1800);
    }

    [Fact]
    public void Each_wave_brings_more_egrons_and_more_to_bring_down()
    {
        for (int k = 1; k < 5; k++)
        {
            var a = NoveniaBook.Build(k - 1);
            var b = NoveniaBook.Build(k);
            Assert.True(b.Egrons >= a.Egrons);
            Assert.True(b.Quota > a.Quota);
            Assert.True(b.Aggression > a.Aggression);
        }
    }

    [Fact]
    public void The_surface_wraps_round()
    {
        Assert.Equal(20, NoveniaDef.Delta(Field.Size - 10, 10), 6);
        Assert.Equal(-20, NoveniaDef.Delta(10, Field.Size - 10), 6);
        Assert.Equal(20, NoveniaDef.Dist(Field.Size - 10, 0, 10, 0), 6);
        Assert.Equal(5, NoveniaDef.Wrap(Field.Size + 5), 6);
    }
}
