using Spectre.Console;
using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// Every severity colour is rendered as Spectre markup, so a name Spectre does
/// not know crashes 'nem audit' the moment an advisory of that severity shows up.
/// </summary>
public class AuditSeverityStyleTests
{
    [Fact]
    public void Every_severity_color_is_a_style_spectre_can_render()
    {
        foreach (var (severity, (_, color)) in AuditService.Severities)
            Assert.True(Style.TryParse(color, out _), $"'{color}' (severity '{severity}') is not a Spectre style");
    }
}
