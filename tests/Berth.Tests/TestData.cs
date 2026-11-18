namespace Berth.Tests;

/// <summary>
/// A unique temporary folder for one test, deleted with everything in it on dispose. It never changes the test
/// process's environment: in-process code gets <see cref="Root"/>, and a child process gets BERTH_DATA in its own
/// environment (AGENTS.md, "Tests never touch the owner's data or desktop").
/// </summary>
internal sealed class TestData : IDisposable
{
    public TestData()
    {
        Root = Path.Combine(Path.GetTempPath(), "Berth.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
