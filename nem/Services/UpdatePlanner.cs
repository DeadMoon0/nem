using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using nem.Common.Models;

namespace nem.Services;

/// <summary>
/// One tool's update status: what the nem config declares, what is installed, and the
/// newest version supported by the reference Node version.
/// </summary>
public sealed class ToolUpdateEntry
{
    public required string Name { get; init; }

    /// <summary>The version declared in the nem config (the resolved concrete one).</summary>
    public required string DeclaredVersion { get; init; }

    /// <summary>The declared version when the store holds it, or null.</summary>
    public string? InstalledVersion { get; init; }

    /// <summary>The newest supported version, or null when it could not be resolved.</summary>
    public string? LatestVersion { get; init; }

    public string? Error { get; init; }

    public bool IsUpToDate =>
        LatestVersion == null ||
        string.Equals(LatestVersion, DeclaredVersion, StringComparison.OrdinalIgnoreCase);

    public bool HasUpdate => LatestVersion != null && !IsUpToDate;
}

/// <summary>
/// The full picture of what 'nem update' could change. Produced by
/// <see cref="UpdatePlanner"/> and rendered/confirmed by the command.
/// </summary>
public sealed class UpdatePlan
{
    /// <summary>The Node version declared in the nem config ("" when unset).</summary>
    public required string DeclaredNodeVersion { get; init; }

    /// <summary>The declared Node version when the store holds it, or null.</summary>
    public string? InstalledNodeVersion { get; init; }

    /// <summary>The newest stable nodejs.org release, or null when it could not be determined.</summary>
    public string? LatestNodeVersion { get; init; }

    public required IReadOnlyList<ToolUpdateEntry> Tools { get; init; }

    public bool HasNodeUpdate =>
        LatestNodeVersion != null &&
        !string.Equals(LatestNodeVersion, DeclaredNodeVersion, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Node version facts for an env: whether the store holds its Node version and
/// what the newest stable nodejs.org release is.
/// </summary>
public interface INodeVersionSource
{
    /// <summary>The config's Node version when the store holds it, or null.</summary>
    string? GetInstalledNodeVersion(NemConfig config);

    /// <summary>The newest stable Node release, or null when it cannot be determined.</summary>
    Task<string?> GetLatestStableNodeVersionAsync();
}

/// <summary>
/// Tool version facts for an env: what newest version each tool can resolve to
/// and whether the store holds the declared one.
/// </summary>
public interface IToolVersionSource
{
    /// <summary>The newest resolvable version of a tool, or null (with <paramref name="error"/>).</summary>
    string? ResolveLatestVersion(string packageName, string nodeVersion, out string? error);

    /// <summary>The config's version of a tool when the store holds it, or null.</summary>
    string? GetInstalledVersion(NemConfig config, string packageName);
}

/// <summary>
/// <see cref="INodeVersionSource"/> backed by <see cref="NodeDownloadingService"/>.
/// Degrades to null (instead of throwing) so planning works offline.
/// </summary>
public sealed class NodeVersionSource : INodeVersionSource
{
    public string? GetInstalledNodeVersion(NemConfig config) =>
        EnvComposition.Compose(config, EnvStore.Default) is EnvComposition.Result.Ok ok && EnvStore.IsComplete(ok.Env.NodeDir)
            ? ok.Env.NodeVersion
            : null;

    public async Task<string?> GetLatestStableNodeVersionAsync()
    {
        try
        {
            return await NodeDownloadingService.GetLatestStableNodeVersionAsync();
        }
        catch (Exception)
        {
            return null; // offline or index unreachable: degrade to what is known
        }
    }
}

/// <summary>
/// <see cref="IToolVersionSource"/> backed by <see cref="ToolService"/>.
/// </summary>
public sealed class ToolVersionSource : IToolVersionSource
{
    public string? ResolveLatestVersion(string packageName, string nodeVersion, out string? error) =>
        ToolService.ResolveVersion(packageName, range: null, nodeVersion, out error);

    public string? GetInstalledVersion(NemConfig config, string packageName) =>
        EnvComposition.Compose(config, EnvStore.Default) is EnvComposition.Result.Ok ok
            && ok.Env.Tools.FirstOrDefault(t => t.Name == packageName) is { } tool && EnvStore.IsComplete(tool.Dir)
            ? tool.Version
            : null;
}

/// <summary>
/// Figures out what 'nem update' would change. This is pure read-only planning:
/// it resolves versions but never prints prompts, writes files, or installs.
/// </summary>
public sealed class UpdatePlanner
{
    private readonly INodeVersionSource _nodeVersions;
    private readonly IToolVersionSource _toolVersions;

    public UpdatePlanner(INodeVersionSource nodeVersions, IToolVersionSource toolVersions)
    {
        _nodeVersions = nodeVersions;
        _toolVersions = toolVersions;
    }

    /// <summary>A planner wired to the real Node and tool services.</summary>
    public static UpdatePlanner Create() => new(new NodeVersionSource(), new ToolVersionSource());

    /// <summary>
    /// Plans an update for an env.
    /// </summary>
    /// <param name="config">The project's nem config.</param>
    /// <param name="nodeReference">
    /// The Node version that "newest supported" tool versions are resolved
    /// against. Defaults to the declared one; pass the new Node version when the
    /// user already accepted a Node update so tool versions stay compatible.
    /// </param>
    public async Task<UpdatePlan> CreateAsync(NemConfig config, string? nodeReference = null)
    {
        string declaredNode = config.NodeVersion ?? "";
        string? latestNode = await _nodeVersions.GetLatestStableNodeVersionAsync();

        string reference = nodeReference ?? declaredNode;
        var tools = new List<ToolUpdateEntry>();
        foreach (NemToolConfig tool in config.Tools)
        {
            string? latest = null;
            string? error = null;
            try
            {
                latest = _toolVersions.ResolveLatestVersion(tool.ToolName, reference, out error);
            }
            catch (Exception e)
            {
                error = e.Message;
            }

            tools.Add(new ToolUpdateEntry
            {
                Name = tool.ToolName,
                DeclaredVersion = tool.ToolVersion,
                InstalledVersion = _toolVersions.GetInstalledVersion(config, tool.ToolName),
                LatestVersion = latest,
                Error = error,
            });
        }

        return new UpdatePlan
        {
            DeclaredNodeVersion = declaredNode,
            InstalledNodeVersion = _nodeVersions.GetInstalledNodeVersion(config),
            LatestNodeVersion = latestNode,
            Tools = tools,
        };
    }
}
