using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// nem notices when a packaged app redirects its writes, and names the app - but
/// only when it really runs inside it, not merely because such a copy exists.
/// </summary>
public class PackageRedirectionTests
{
    const string Probe = ".nem-probe-test";

    sealed class Profile : IDisposable
    {
        readonly TempDir _tmp = new();
        public string Roaming => Path.Combine(_tmp.FullName, "Roaming");
        public string Packages => Path.Combine(_tmp.FullName, "Local", "Packages");
        public string NemDir => Path.Combine(Roaming, "nem");

        public Profile() => Directory.CreateDirectory(NemDir);

        /// <summary>An app that once had nem write under its redirection.</summary>
        public string PackageCopy(string package) =>
            Directory.CreateDirectory(Path.Combine(Packages, package, "LocalCache", "Roaming", "nem")).FullName;

        public void Dispose() => _tmp.Dispose();
    }

    [Fact]
    public void Inside_a_packaged_app_the_app_and_where_nem_really_writes_are_named()
    {
        using var profile = new Profile();
        string copy = profile.PackageCopy("Claude_pzs8sxrjxfjjc");
        profile.PackageCopy("Other_123");
        // What Windows does inside the app: the probe nem writes lands in the app's copy.
        File.WriteAllText(Path.Combine(copy, Probe), "");

        PackageRedirection? redirection = PackageRedirection.Detect(profile.NemDir, profile.Roaming, profile.Packages, Probe);

        Assert.Equal(new PackageRedirection("Claude_pzs8sxrjxfjjc", copy), redirection);
    }

    [Fact]
    public void In_a_normal_terminal_an_old_package_copy_is_no_reason_to_warn()
    {
        using var profile = new Profile();
        profile.PackageCopy("Claude_pzs8sxrjxfjjc");

        Assert.Null(PackageRedirection.Detect(profile.NemDir, profile.Roaming, profile.Packages, Probe));
    }

    [Fact]
    public void The_probe_does_not_stay_behind()
    {
        using var profile = new Profile();
        profile.PackageCopy("Claude_pzs8sxrjxfjjc");

        PackageRedirection.Detect(profile.NemDir, profile.Roaming, profile.Packages, Probe);

        Assert.False(File.Exists(Path.Combine(profile.NemDir, Probe)));
    }

    [Fact]
    public void A_nem_folder_outside_appdata_is_never_redirected()
    {
        using var profile = new Profile();
        string elsewhere = Path.Combine(Path.GetDirectoryName(profile.Roaming)!, "nem-home");
        string copy = profile.PackageCopy("Claude_pzs8sxrjxfjjc");
        File.WriteAllText(Path.Combine(copy, Probe), "");

        Assert.Null(PackageRedirection.Detect(elsewhere, profile.Roaming, profile.Packages, Probe));
    }
}
