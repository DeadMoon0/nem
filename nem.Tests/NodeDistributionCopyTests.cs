using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// Copying the extracted Node distribution into the env must keep it working:
/// the Unix tarball's bin/npm is a relative symlink, and npm only starts when it
/// stays one (its require('../lib/cli.js') resolves from the link's real path).
/// </summary>
public class NodeDistributionCopyTests
{
    [Fact]
    public void Npm_in_the_env_stays_a_link_to_the_npm_package_it_ships_with()
    {
        using var temp = new TempDir();
        string dist = Path.Combine(temp.FullName, "node-v22-linux-x64");
        string npmCli = Path.Combine(dist, "lib", "node_modules", "npm", "bin", "npm-cli.js");
        Directory.CreateDirectory(Path.GetDirectoryName(npmCli)!);
        File.WriteAllText(npmCli, "require('../lib/cli.js')(process)");
        Directory.CreateDirectory(Path.Combine(dist, "bin"));
        string relativeTarget = Path.Combine("..", "lib", "node_modules", "npm", "bin", "npm-cli.js");
        if (!TryCreateSymbolicLink(Path.Combine(dist, "bin", "npm"), relativeTarget))
            return; // Windows without Developer Mode cannot create links; Node's zip ships none there.

        string env = Path.Combine(temp.FullName, ".nenv");
        NodeDownloadingService.CopyFilesRecursively(dist, env);

        var npm = new FileInfo(Path.Combine(env, "bin", "npm"));
        Assert.Equal(relativeTarget, npm.LinkTarget);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(env, "lib", "node_modules", "npm", "bin", "npm-cli.js")),
            npm.ResolveLinkTarget(returnFinalTarget: true)!.FullName);
    }

    [Fact]
    public void Regular_files_are_copied_with_their_content()
    {
        using var temp = new TempDir();
        string dist = Path.Combine(temp.FullName, "node-v22-win-x64");
        Directory.CreateDirectory(Path.Combine(dist, "node_modules", "npm"));
        File.WriteAllText(Path.Combine(dist, "node_modules", "npm", "package.json"), "{}");
        File.WriteAllText(Path.Combine(dist, "install_tools.bat"), "legacy");

        string env = Path.Combine(temp.FullName, ".nenv");
        NodeDownloadingService.CopyFilesRecursively(dist, env);

        Assert.Equal("{}", File.ReadAllText(Path.Combine(env, "node_modules", "npm", "package.json")));
        Assert.False(File.Exists(Path.Combine(env, "install_tools.bat")));
    }

    static bool TryCreateSymbolicLink(string path, string target)
    {
        try
        {
            File.CreateSymbolicLink(path, target);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
