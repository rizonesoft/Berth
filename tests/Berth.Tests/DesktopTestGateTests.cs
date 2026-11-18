namespace Berth.Tests;

/// <summary>
/// Proves the Desktop collection's gate across processes. Each test uses a mutex name of its own, so these tests
/// never contend with the real gate or with each other.
/// </summary>
public sealed class DesktopTestGateTests
{
    [Fact]
    public void HeldGateBlocksAnotherProcessUntilDisposed()
    {
        string name = UniqueName();
        using var helper = HelperProcess.Start("mutex", name);
        var gate = new DesktopTestGate(name, TimeSpan.FromSeconds(5));
        try
        {
            Assert.Equal("busy", helper.Send("try"));
        }
        finally
        {
            gate.Dispose();
        }

        Assert.Equal("acquired", helper.Send("try"));
        Assert.Equal("released", helper.Send("release"));
    }

    [Fact]
    public void GateThatCannotTakeTheMutexInTimeThrowsAndGuardsNothing()
    {
        string name = UniqueName();
        using var helper = HelperProcess.Start("mutex", name);
        Assert.Equal("acquired", helper.Send("try"));
        bool bodyRan = false;

        var error = Assert.Throws<TimeoutException>(() =>
        {
            using var gate = new DesktopTestGate(name, TimeSpan.FromMilliseconds(300));
            bodyRan = true;
        });

        Assert.Equal($"another Berth interface test run holds {name}", error.Message);
        Assert.False(bodyRan);
        Assert.Equal("released", helper.Send("release"));
    }

    [Fact]
    public void MutexAbandonedByAKilledHolderIsTakenAndReleased()
    {
        string name = UniqueName();
        using (var crashed = HelperProcess.Start("mutex", name))
        {
            Assert.Equal("acquired", crashed.Send("try"));
            crashed.Kill();
        }

        using (var gate = new DesktopTestGate(name, TimeSpan.FromSeconds(5)))
        {
            Assert.Equal(name, gate.MutexName);
        }

        using var after = HelperProcess.Start("mutex", name);
        Assert.Equal("acquired", after.Send("try"));
        Assert.Equal("released", after.Send("release"));
    }

    [Fact]
    public void DefaultGateUsesTheMachineWideName()
    {
        Assert.Equal(@"Global\Berth.Tests.Desktop", DesktopTestGate.DefaultMutexName);
    }

    private static string UniqueName() => @"Global\Berth.Tests.GateSelfTest." + Guid.NewGuid().ToString("N");
}

/// <summary>A member of the Desktop collection: while it runs, the collection's gate holds the machine-wide mutex.</summary>
[Collection(DesktopCollectionDefinition.Name)]
public sealed class DesktopCollectionMemberTests(DesktopTestGate gate)
{
    [Fact]
    public void CollectionFixtureHoldsTheMachineWideMutexDuringATest()
    {
        Assert.Equal(DesktopTestGate.DefaultMutexName, gate.MutexName);
        using var helper = HelperProcess.Start("mutex", DesktopTestGate.DefaultMutexName);
        Assert.Equal("busy", helper.Send("try"));
    }
}