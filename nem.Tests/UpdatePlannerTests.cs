using Xunit;
using nem.Common.Models;
using nem.Services;

namespace nem.Tests;

public class UpdatePlannerTests
{
    private static NemConfig Config(string? nodeVersion, (string Name, string Version)[] tools) =>
        new()
        {
            NodeVersion = nodeVersion,
            Tools = tools.Select(t => new NemToolConfig { ToolName = t.Name, ToolVersion = t.Version }).ToList(),
        };

    [Fact]
    public async Task Node_Update_Is_Reported_Only_When_The_Latest_Differs()
    {
        var planner = new UpdatePlanner(
            new FakeNodeSource { Latest = "26.8.1", Installed = "22.23.2" },
            new FakeToolSource());

        UpdatePlan plan = await planner.CreateAsync(Config("22.23.2", []), "/env");

        Assert.Equal("22.23.2", plan.DeclaredNodeVersion);
        Assert.Equal("22.23.2", plan.InstalledNodeVersion);
        Assert.Equal("26.8.1", plan.LatestNodeVersion);
        Assert.True(plan.HasNodeUpdate);
    }

    [Fact]
    public async Task Node_Update_Is_Suppressed_When_Online_Latest_Equals_Declared()
    {
        var planner = new UpdatePlanner(
            new FakeNodeSource { Latest = "22.23.2", Installed = "22.23.2" },
            new FakeToolSource());

        UpdatePlan plan = await planner.CreateAsync(Config("22.23.2", []), "/env");

        Assert.False(plan.HasNodeUpdate);
    }

    [Fact]
    public async Task Missing_Latest_Node_Is_Reported_But_Not_As_An_Update()
    {
        var planner = new UpdatePlanner(
            new FakeNodeSource { Latest = null, Installed = "22.23.2" },
            new FakeToolSource());

        UpdatePlan plan = await planner.CreateAsync(Config("22.23.2", []), "/env");

        Assert.Null(plan.LatestNodeVersion);
        Assert.False(plan.HasNodeUpdate);
    }

    [Fact]
    public async Task Tool_Entries_Reflect_The_Version_Source()
    {
        var tools = new FakeToolSource
        {
            Latest = new Dictionary<string, string?> { ["typescript"] = "5.9.2" },
            Installed = new Dictionary<string, string?> { ["typescript"] = "5.6.3" },
        };
        var planner = new UpdatePlanner(
            new FakeNodeSource { Latest = null },
            tools);

        UpdatePlan plan = await planner.CreateAsync(
            Config("22.23.2", [("typescript", "5.6.3"), ("prettier", "3.4.2")]), "/env");

        ToolUpdateEntry ts = plan.Tools.Single(t => t.Name == "typescript");
        Assert.Equal("5.6.3", ts.DeclaredVersion);
        Assert.Equal("5.6.3", ts.InstalledVersion);
        Assert.Equal("5.9.2", ts.LatestVersion);
        Assert.False(ts.IsUpToDate);
        Assert.True(ts.HasUpdate);
        Assert.Null(ts.Error);

        ToolUpdateEntry prettier = plan.Tools.Single(t => t.Name == "prettier");
        Assert.Null(prettier.LatestVersion);
        Assert.True(prettier.IsUpToDate);
        Assert.False(prettier.HasUpdate);
    }

    [Fact]
    public async Task Resolver_Results_Are_Against_The_Declared_Node_By_Default()
    {
        var tools = new FakeToolSource { Latest = new() { ["typescript"] = "5.9.2" } };
        var planner = new UpdatePlanner(new FakeNodeSource(), tools);

        await planner.CreateAsync(Config("22.23.2", [("typescript", "5.6.3")]), "/env");

        Assert.All(tools.ResolvedWith, v => Assert.Equal("22.23.2", v));
    }

    [Fact]
    public async Task Resolver_Results_Are_Against_A_Provided_Node_Reference()
    {
        var tools = new FakeToolSource { Latest = new() { ["typescript"] = "5.9.2" } };
        var planner = new UpdatePlanner(new FakeNodeSource(), tools);

        await planner.CreateAsync(
            Config("22.23.2", [("typescript", "5.6.3")]), "/env", nodeReference: "26.8.1");

        Assert.All(tools.ResolvedWith, v => Assert.Equal("26.8.1", v));
    }

    [Fact]
    public async Task Resolver_Errors_And_Exceptions_Become_Entry_Errors()
    {
        var tools = new FakeToolSource
        {
            Errors = new Dictionary<string, string?> { ["typescript"] = "package not found" },
            Exceptions = new Dictionary<string, Exception> { ["prettier"] = new InvalidOperationException("net down") },
        };
        var planner = new UpdatePlanner(new FakeNodeSource(), tools);

        UpdatePlan plan = await planner.CreateAsync(
            Config("22.23.2", [("typescript", "5.6.3"), ("prettier", "3.4.2")]), "/env");

        Assert.Equal("package not found", plan.Tools.Single(t => t.Name == "typescript").Error);
        Assert.Equal("net down", plan.Tools.Single(t => t.Name == "prettier").Error);
        Assert.All(plan.Tools, t => Assert.True(t.IsUpToDate));
    }

    [Fact]
    public async Task Empty_Node_Declaration_Becomes_An_Empty_String()
    {
        var planner = new UpdatePlanner(new FakeNodeSource(), new FakeToolSource());

        UpdatePlan plan = await planner.CreateAsync(Config(null, []), "/env");

        Assert.Equal("", plan.DeclaredNodeVersion);
    }

    [Fact]
    public void Update_Plan_Flags_Follow_Direct_Comparison()
    {
        var upToDate = new UpdatePlan
        {
            DeclaredNodeVersion = "22.23.2",
            InstalledNodeVersion = "22.23.2",
            LatestNodeVersion = "22.23.2",
            Tools = [],
        };
        Assert.False(upToDate.HasNodeUpdate);

        var behind = new UpdatePlan
        {
            DeclaredNodeVersion = "22.23.2",
            LatestNodeVersion = "26.8.1",
            Tools = [],
        };
        Assert.True(behind.HasNodeUpdate);

        var toolUpToDate = new ToolUpdateEntry { Name = "t", DeclaredVersion = "5.6.3", LatestVersion = "5.6.3" };
        Assert.True(toolUpToDate.IsUpToDate);
        Assert.False(toolUpToDate.HasUpdate);

        var toolBehind = new ToolUpdateEntry { Name = "t", DeclaredVersion = "5.6.3", LatestVersion = "5.9.2" };
        Assert.False(toolBehind.IsUpToDate);
        Assert.True(toolBehind.HasUpdate);
    }

    private sealed class FakeNodeSource : INodeVersionSource
    {
        public string? Installed { get; init; }
        public string? Latest { get; init; }

        public string? GetInstalledNodeVersion(string envDir) => Installed;

        public Task<string?> GetLatestStableNodeVersionAsync() => Task.FromResult(Latest);
    }

    private sealed class FakeToolSource : IToolVersionSource
    {
        public Dictionary<string, string?> Latest { get; init; } = new();
        public Dictionary<string, string?> Installed { get; init; } = new();
        public Dictionary<string, string?> Errors { get; init; } = new();
        public Dictionary<string, Exception> Exceptions { get; init; } = new();
        public List<string> ResolvedWith { get; } = new();

        public string? ResolveLatestVersion(string packageName, string nodeVersion, string envDir, out string? error)
        {
            ResolvedWith.Add(nodeVersion);
            if (Exceptions.TryGetValue(packageName, out Exception? exception))
                throw exception;
            if (Errors.TryGetValue(packageName, out string? resolvedError))
            {
                error = resolvedError;
                return null;
            }
            error = null;
            return Latest.TryGetValue(packageName, out string? version) ? version : null;
        }

        public string? GetInstalledVersion(string envDir, string packageName) =>
            Installed.TryGetValue(packageName, out string? version) ? version : null;
    }
}
