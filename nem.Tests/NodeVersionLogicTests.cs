using System.Runtime.InteropServices;
using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// Pure Node version logic in NodeDownloadingService (no network, no disk):
/// normalization, partial-spec detection, version comparison and shasum
/// parsing.
/// </summary>
public class NodeVersionLogicTests
{
    [Theory]
    [InlineData("v22", "22")]
    [InlineData("V22", "22")]
    [InlineData(" 22 ", "22")]
    [InlineData("22.", "22")]
    [InlineData("22.0.1", "22.0.1")]
    public void NormalizeVersion_Strips_V_Prefix_Trailing_Dot_And_Whitespace(string input, string expected)
        => Assert.Equal(expected, NodeDownloadingService.NormalizeVersion(input));

    [Theory]
    [InlineData("22", true)]
    [InlineData("22.0", true)]
    [InlineData("22.", true)]
    [InlineData("22.0.0", false)]
    [InlineData("22.x", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void IsPartialVersionSpec_Classifies_Digit_Dot_Specs(string spec, bool expected)
        => Assert.Equal(expected, NodeDownloadingService.IsPartialVersionSpec(spec));

    [Theory]
    [InlineData("", "22.23.2", true)]
    [InlineData("22", "22.23.2", true)]
    [InlineData("22", "20.23.2", false)]
    [InlineData("22.0", "22.0.15", true)]
    [InlineData("22.0", "22.10.0", false)]
    [InlineData("22.0.15", "22.0.15", true)]
    [InlineData("22.0.15", "22.0.16", false)]
    public void VersionSpecMatches_Checks_Major_Minor_Prefixes(string spec, string version, bool expected)
        => Assert.Equal(expected, NodeDownloadingService.VersionSpecMatches(spec, version));

    [Theory]
    [InlineData("22", "22", 0)]
    [InlineData("22", "23", -1)]
    [InlineData("10.0.0", "9.0.0", 1)]
    [InlineData("22", "22.0.0", 0)]
    [InlineData("22.23.2", "22.23.10", -1)]
    public void CompareVersions_Comparers_Numeric_Segments(string a, string b, int expectedSign)
        => Assert.Equal(expectedSign, NodeDownloadingService.CompareVersions(a, b).CompareTo(0));

    [Fact]
    public void NewestStableVersion_Picks_The_Highest_Stable_Tag()
        => Assert.Equal("26.8.1", NodeDownloadingService.NewestStableVersion(
            ["v20.20.0", "v26.8.1", "v24.18.0"]));

    [Fact]
    public void NewestStableVersion_Honors_A_Prefix_Filter()
        => Assert.Equal("22.23.2", NodeDownloadingService.NewestStableVersion(
            ["v23.0.0", "v22.23.2", "v22.23.1"], "22"));

    [Fact]
    public void NewestStableVersion_Ignores_Pre_Releases_For_A_Prefix()
        => Assert.Null(NodeDownloadingService.NewestStableVersion(
            ["v22.0.0-nightly20260701", "v21.7.3"], "22"));

    [Fact]
    public void NewestStableVersion_Skips_Malformed_Tags()
        => Assert.Null(NodeDownloadingService.NewestStableVersion(["junk", ""]));

    [Fact]
    public void NewestStableVersion_Returns_Null_For_An_Empty_List()
        => Assert.Null(NodeDownloadingService.NewestStableVersion([]));

    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string NodeFile = "node-v22.0.0-linux-x64.tar.xz";

    [Fact]
    public void ParseSha256_Finds_The_Exact_File_Name()
        => Assert.Equal(Sha, NodeDownloadingService.ParseSha256(Sha + "  " + NodeFile, NodeFile));

    [Fact]
    public void ParseSha256_Finds_A_Star_Prefixed_File_Name()
        => Assert.Equal(Sha, NodeDownloadingService.ParseSha256(Sha + "  *" + NodeFile, NodeFile));

    [Fact]
    public void ParseSha256_Matches_File_Names_Case_Insensitively()
        => Assert.Equal(Sha, NodeDownloadingService.ParseSha256(Sha + "  NODE-V22.TAR.XZ", "node-v22.tar.xz"));

    [Fact]
    public void ParseSha256_Returns_Null_When_The_File_Is_Not_Listed()
        => Assert.Null(NodeDownloadingService.ParseSha256(Sha + "  other.tar.xz", NodeFile));

    [Fact]
    public void ParseSha256_Skips_Entries_With_Invalid_Hashes()
        => Assert.Null(NodeDownloadingService.ParseSha256("abc  " + NodeFile, NodeFile));

    [Fact]
    public void GetInstalledNodeVersion_Is_Null_Without_A_Node_Binary()
    {
        using var tmp = new TempDir();
        string envDir = Path.Combine(tmp.FullName, ".nenv");
        Directory.CreateDirectory(envDir);

        Assert.Null(NodeDownloadingService.GetInstalledNodeVersion(envDir));
    }

    [Fact]
    public void GetInstalledNodeVersion_Is_Null_When_The_Binary_Cannot_Run()
    {
        using var tmp = new TempDir();
        string binDir = Path.Combine(tmp.FullName, ".nenv", "bin");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(
            Path.Combine(binDir, OperatingSystem.IsWindows() ? "node.exe" : "node"),
            "not a node binary");

        Assert.Null(NodeDownloadingService.GetInstalledNodeVersion(Path.Combine(tmp.FullName, ".nenv")));
    }

    [Fact]
    public void GetPlatformPackage_Maps_Platform_Facts_To_The_Dist_Package()
    {
        Assert.Equal(("node-v26.8.1-linux-x64", "tar.xz"),
            NodeDownloadingService.GetPlatformPackage("26.8.1", Architecture.X64, isWindows: false, isLinux: true, isMacOS: false));
        Assert.Equal(("node-v26.8.1-linux-arm64", "tar.xz"),
            NodeDownloadingService.GetPlatformPackage("26.8.1", Architecture.Arm64, isWindows: false, isLinux: true, isMacOS: false));
        Assert.Equal(("node-v26.8.1-darwin-x64", "tar.xz"),
            NodeDownloadingService.GetPlatformPackage("26.8.1", Architecture.X64, isWindows: false, isLinux: false, isMacOS: true));
        Assert.Equal(("node-v26.8.1-darwin-arm64", "tar.xz"),
            NodeDownloadingService.GetPlatformPackage("26.8.1", Architecture.Arm64, isWindows: false, isLinux: false, isMacOS: true));

        string expectedWinArch = Environment.Is64BitProcess ? "x64" : "x86";
        Assert.Equal(("node-v26.8.1-win-" + expectedWinArch, "zip"),
            NodeDownloadingService.GetPlatformPackage("26.8.1", Environment.Is64BitProcess ? Architecture.X64 : Architecture.X86,
                isWindows: true, isLinux: false, isMacOS: false));
    }

    [Fact]
    public void GetPlatformPackage_Rejects_Unsupported_Platforms()
    {
        Assert.Throws<NotSupportedException>(() =>
            NodeDownloadingService.GetPlatformPackage("26.8.1", Architecture.Arm64, isWindows: true, isLinux: false, isMacOS: false));
        Assert.Throws<NotSupportedException>(() =>
            NodeDownloadingService.GetPlatformPackage("26.8.1", Architecture.X64, isWindows: false, isLinux: false, isMacOS: false));
    }
}
