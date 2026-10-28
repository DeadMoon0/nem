using Xunit;
using nem.Services.Diagnostics;

namespace nem.Tests;

/// <summary>Secrets leave the machine as ***; everything that helps to understand a problem stays.</summary>
public class RedactorTests
{
    [Theory]
    [InlineData("//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken=eyJ0eXAi.abc", "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken=***")]
    [InlineData("_auth = dXNlcjpwYXNz", "_auth = ***")]
    [InlineData("//feed/:_password=cGFzcw==", "//feed/:_password=***")]
    [InlineData("\"password\": \"hunter2\"", "\"password\": \"***\"")]
    [InlineData("https://alex:pa55@feed.example/npm/", "https://***@feed.example/npm/")]
    public void Secrets_are_masked(string text, string expected)
    {
        Assert.Equal(expected, Redactor.Redact(text));
    }

    [Theory]
    [InlineData(@"C:\Users\AlexanderLindenmayer\AppData\Roaming\nem\store\node\22.23.3")]
    [InlineData("https://registry.npmjs.org/")]
    [InlineData("--@qbq-nmd:registry=https://pkgs.dev.azure.com/quibiq/_packaging/feed/npm/registry/")]
    [InlineData("npm view --json @angular/cli@17.3.8 version")]
    public void Paths_registries_and_arguments_stay(string text)
    {
        Assert.Equal(text, Redactor.Redact(text));
    }
}
