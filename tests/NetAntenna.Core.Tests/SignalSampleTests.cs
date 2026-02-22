using FluentAssertions;
using NetAntenna.Core.Models;

namespace NetAntenna.Core.Tests;

public class SignalSampleTests
{
    [Fact]
    public void FromTunerStatus_MapsAllFields()
    {
        // Arrange
        var status = TunerStatus.Parse("""
            ch=8vsb:7
            lock=8vsb
            ss=85
            snq=92
            seq=100
            bps=19392712
            pps=2412
            """);

        // Act
        var sample = SignalSample.FromTunerStatus("ABCD1234", 0, status);

        // Assert
        sample.DeviceId.Should().Be("ABCD1234");
        sample.TunerIndex.Should().Be(0);
        sample.Channel.Should().Be("8vsb:7");
        sample.LockType.Should().Be("8vsb");
        sample.Ss.Should().Be(85);
        sample.Snq.Should().Be(92);
        sample.Seq.Should().Be(100);
        sample.Bps.Should().Be(19392712);
        sample.Pps.Should().Be(2412);
        sample.TimestampUnixMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public void FromTunerStatus_SetsTimestampToNow()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var status = new TunerStatus();
        var sample = SignalSample.FromTunerStatus("DEV1", 0, status);
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        sample.TimestampUnixMs.Should().BeInRange(before, after);
    }
}
