using System.Windows;
using Telemetry;

namespace Inflate.Tests;

public sealed class HostIntegrationTests
{
    [Fact]
    public void OutsideAWpfApplicationNoTelemetryIsStartedOrSent()
    {
        Assert.Null(Application.Current);

        InflateTelemetry.EnsureStartedOnce();
        InflateTelemetry.Report(new InvalidOperationException());

        Assert.Null(ProcessState.Read("DrainClaimed"));
        Assert.Null(ProcessState.Read("SentCount"));
    }

    [Fact]
    public void OutsideAWpfApplicationTheEffectCanStillBeCreated()
    {
        Assert.Null(Application.Current);

        var effect = new InflateEffect();

        Assert.Equal(Texts.Inflate, effect.Label);
        Assert.Null(ProcessState.Read("DrainClaimed"));
    }
}
