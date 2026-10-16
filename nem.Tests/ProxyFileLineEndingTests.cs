using Xunit;

namespace nem.Tests;

/// <summary>
/// Convention: the proxy templates nem ships keep the line endings their shell
/// needs, whatever the checkout's core.autocrlf (enforced by .gitattributes).
/// Checked on the copies in the build output, which are the files that get packed.
/// </summary>
public class ProxyFileLineEndingTests
{
    static string ShippedProxy(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ProxyFiles", name));

    [Fact]
    public void The_Unix_proxy_has_no_carriage_returns()
    {
        // A CRLF shebang makes Linux look for "bash\r" and refuse to run the proxy.
        Assert.DoesNotContain('\r', ShippedProxy("NAME"));
    }

    [Fact]
    public void The_batch_proxy_uses_crlf_line_endings()
    {
        string bat = ShippedProxy("NAME.bat");

        Assert.Equal(bat.Split('\n').Length - 1, bat.Split("\r\n").Length - 1);
    }
}
