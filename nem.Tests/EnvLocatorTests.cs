using Xunit;
using nem.Commands;
using nem.Common;

namespace nem.Tests;

/// <summary>
/// 'nem install' and 'nem update' resolve their env through the locator, so they
/// accept a subfolder the same way 'nem run' and 'nem tool' do.
/// </summary>
public class EnvLocatorTests
{
    [Theory]
    [InlineData("", "nem.jsonc")]
    [InlineData("frontend", "nem.jsonc")]
    [InlineData("frontend/src/app", "nem.jsonc")]
    [InlineData("frontend/src/app", "nem.json")]
    public void TryLocate_Accepts_Any_Folder_Inside_The_Env(string sub, string configFileName)
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.FullName, configFileName), "{}");
        string start = Path.Combine(tmp.FullName, sub.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(start);

        Assert.True(EnvLocator.TryLocate(start, out IOPathManager.IOPathManagerEnv? env));
        Assert.Equal(tmp.FullName, env!.DirPath);
        Assert.Equal(Path.Combine(tmp.FullName, ".nenv"), env.EnvDirPath);
    }

    [Fact]
    public void TryLocate_Fails_When_No_Env_Exists_Above()
    {
        using var tmp = new TempDir();
        string start = Path.Combine(tmp.FullName, "frontend");
        Directory.CreateDirectory(start);

        Assert.False(EnvLocator.TryLocate(start, out IOPathManager.IOPathManagerEnv? env));
        Assert.Null(env);
    }
}
