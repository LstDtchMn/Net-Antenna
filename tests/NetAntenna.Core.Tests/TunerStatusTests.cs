using FluentAssertions;
using NetAntenna.Core.Models;

namespace NetAntenna.Core.Tests;

public class TunerStatusTests
{
    [Fact]
    public void Parse_ValidStatus_ReturnsPopulatedStatus()
    {
        // Arrange — exact format from HDHomeRun /tuner0/status
        var input = """
            ch=8vsb:7
            lock=8vsb
            ss=85
            snq=92
            seq=100
            bps=19392712
            pps=2412
            """;

        // Act
        var status = TunerStatus.Parse(input);

        // Assert
        status.Channel.Should().Be("8vsb:7");
        status.Lock.Should().Be("8vsb");
        status.SignalStrength.Should().Be(85);
        status.SignalToNoiseQuality.Should().Be(92);
        status.SymbolQuality.Should().Be(100);
        status.BitsPerSecond.Should().Be(19392712);
        status.PacketsPerSecond.Should().Be(2412);
        status.IsLocked.Should().BeTrue();
    }

    [Fact]
    public void Parse_NoLock_ReturnsUnlockedStatus()
    {
        var input = """
            ch=none
            lock=none
            ss=0
            snq=0
            seq=0
            bps=0
            pps=0
            """;

        var status = TunerStatus.Parse(input);

        status.Lock.Should().Be("none");
        status.IsLocked.Should().BeFalse();
        status.SignalStrength.Should().Be(0);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsDefaults()
    {
        var status = TunerStatus.Parse("");

        status.Channel.Should().BeEmpty();
        status.Lock.Should().BeEmpty();
        status.SignalStrength.Should().Be(0);
        status.IsLocked.Should().BeFalse();
    }

    [Fact]
    public void Parse_NullString_ReturnsDefaults()
    {
        var status = TunerStatus.Parse(null!);

        status.Channel.Should().BeEmpty();
        status.SignalStrength.Should().Be(0);
    }

    [Fact]
    public void Parse_MalformedLines_SkipsGracefully()
    {
        var input = """
            ch=8vsb:7
            garbage_no_equals
            =orphan_value
            ss=85
            unknown_key=42
            """;

        var status = TunerStatus.Parse(input);

        status.Channel.Should().Be("8vsb:7");
        status.SignalStrength.Should().Be(85);
        // Unknown keys are silently ignored
    }

    [Fact]
    public void Parse_QamModulation_ParsesCorrectly()
    {
        var input = """
            ch=qam256:51
            lock=qam256
            ss=75
            snq=88
            seq=95
            bps=38000000
            pps=4700
            """;

        var status = TunerStatus.Parse(input);

        status.Channel.Should().Be("qam256:51");
        status.Lock.Should().Be("qam256");
        status.SignalStrength.Should().Be(75);
        status.IsLocked.Should().BeTrue();
    }

    [Fact]
    public void Parse_WindowsLineEndings_ParsesCorrectly()
    {
        var input = "ch=8vsb:7\r\nlock=8vsb\r\nss=85\r\nsnq=92\r\nseq=100\r\nbps=19392712\r\npps=2412";

        var status = TunerStatus.Parse(input);

        status.Channel.Should().Be("8vsb:7");
        status.SignalStrength.Should().Be(85);
        status.SymbolQuality.Should().Be(100);
    }
}
