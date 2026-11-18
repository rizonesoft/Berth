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

        // "acquired", not "abandoned": the gate released the mutex itself on dispose.
        using var after = HelperProcess.Start(data.Root, "mutex", name);
        Assert.Equal("acquired", after.Send("try"));
        Assert.Equal("released", after.Send("release"));
    }

    [Fact]
    public void HelperTellsAnAbandonedMutexFromAReleasedOne()
    {
        string name = UniqueName();
        using var data = new TestData();
        using var keepAlive = new Mutex(initiallyOwned: false, name);
        using var observer = HelperProcess.Start(data.Root, "mutex", name);
        using (var crashed = HelperProcess.Start(data.Root, "mutex", name))
        {
            Assert.Equal("acquired", crashed.Send("try"));
            crashed.Kill();
        }

        Assert.Equal("abandoned", observer.Send("try"));
        Assert.Equal("released", observer.Send("release"));
    }

    [Fact]
    public void GateMutexAdmitsEveryAuthenticatedAccount()
    {
        string name = UniqueName();
        using var mutex = DesktopTestGate.OpenSharedMutex(name);
        var sid = new System.Security.Principal.SecurityIdentifier(
            System.Security.Principal.WellKnownSidType.AuthenticatedUserSid, null);

        var rules = mutex.GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(System.Security.Principal.SecurityIdentifier))
            .Cast<System.Security.AccessControl.MutexAccessRule>()
            .Where(r => r.IdentityReference.Equals(sid) && r.AccessControlType == System.Security.AccessControl.AccessControlType.Allow);

        var rights = System.Security.AccessControl.MutexRights.Synchronize | System.Security.AccessControl.MutexRights.Modify;
        Assert.Contains(rules, r => (r.MutexRights & rights) == rights);
    }

    [Fact]
    public void HelperThatFloodsStandardErrorStillAnswers()
    {
        using var data = new TestData();
        var trace = new Dictionary<string, string> { ["DOTNET_HOST_TRACE"] = "1", ["DOTNET_HOST_TRACE_VERBOSITY"] = "4" };
        using var helper = HelperProcess.Start(data.Root, trace, "mutex", UniqueName());

        Assert.StartsWith(data.Root + "|", helper.Send("env"), StringComparison.Ordinal);
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
        var inherited = new Dictionary<string, string>
        {
            ["BERTH_DATA"] = @"C:\owner-data-must-not-reach-a-helper",
            ["BERTH_TEST_ICON_PREFIX"] = "sentinel",
            ["berth_test_hold_ms"] = "1",
        };
        using var helper = HelperProcess.Start(data.Root, inherited, "mutex", UniqueName());

        Assert.Equal(data.Root + "|", helper.Send("env"));
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
