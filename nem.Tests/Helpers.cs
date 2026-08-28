using Xunit;
// Several tests change the process working directory or rely on the OS temp
// layout, so the whole assembly runs single-threaded to avoid cross-test races.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace nem.Tests;

/// <summary>
/// A unique subdirectory under the system temp folder, deleted on dispose.
/// </summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        FullName = Path.Combine(Path.GetTempPath(), "nem-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FullName);
    }

    public string FullName { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(FullName, recursive: true);
        }
        catch
        {
            // Best effort; a leaked temp directory is harmless.
        }
    }
}
