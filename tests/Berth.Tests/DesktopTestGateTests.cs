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
        using var data = new TestData();
        using var helper = HelperProcess.Start(data.Root, "mutex", name);
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
        using var data = new TestData();
        using var helper = HelperProcess.Start(data.Root, "mutex", name);
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
        using var data = new TestData();

        // Windows destroys a named mutex when its last handle closes, so this handle keeps the mutex alive across
        // the kill: without it the gate would open a fresh mutex and never see the abandonment.
        using var keepAlive = new Mutex(initiallyOwned: false, name);
        using (var crashed = HelperProcess.Start(data.Root, "mutex", name))
        {
            Assert.Equal("acquired", crashed.Send("try"));
            crashed.Kill();
        }

        using (var gate = new DesktopTestGate(name, TimeSpan.FromSeconds(5)))
        {
            Assert.Equal(name, gate.MutexName);
        }

        using var after = HelperProcess.Start(data.Root, "mutex", name);
        Assert.Equal("acquired", after.Send("try"));
        Assert.Equal("released", after.Send("release"));
    }

    [Fact]
    public void HelperNeverTakesItsMutexTwice()
    {
        string name = UniqueName();
        using var data = new TestData();
        using var first = HelperProcess.Start(data.Root, "mutex", name);
        using var second = HelperProcess.Start(data.Root, "mutex", name);

        Assert.Equal("acquired", first.Send("try"));
        Assert.Equal("already held", first.Send("try"));
        Assert.Equal("released", first.Send("release"));
        Assert.Equal("acquired", second.Send("try"));
        Assert.Equal("released", second.Send("release"));
    }

    [Fact]
    public void HelperGetsItsOwnDataRootAndNoOtherBerthVariable()
    {
        using var data = new TestData();
        using var helper = HelperProcess.Start(data.Root, "mutex", UniqueName());

        Assert.Equal(data.Root, helper.Send("data"));
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
        using var data = new TestData();
        using var helper = HelperProcess.Start(data.Root, "mutex", DesktopTestGate.DefaultMutexName);
        Assert.Equal("busy", helper.Send("try"));
    }
}
