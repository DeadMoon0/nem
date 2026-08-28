using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using nem.Common.Models;

namespace nem.Services;

/// <summary>
/// One tool's update status: what nem.json declares, what is installed, and the
/// newest version supported by the reference Node version.
/// </summary>
public sealed class ToolUpdateEntry
{
    public required string Name { get; init; }

    /// <summary>The version declared in nem.json (the resolved concrete one).</summary>
    public required string DeclaredVersion { get; init; }

    /// <summary>The version actually installed in the env, or null.</summary>
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
    /// <summary>The Node version declared in nem.json ("" when unset).</summary>
    public required string DeclaredNodeVersion { get; init; }

    /// <summary>The Node version installed in the env, or null.</summary>
    public string? InstalledNodeVersion { get; init; }

    /// <summary>The newest stable nodejs.org release, or null when it could not be determined.</summary>
    public string? LatestNodeVersion { get; init; }

    public required IReadOnlyList<ToolUpdateEntry> Tools { get; init; }

    public bool HasNodeUpdate =>
        LatestNodeVersion != null &&
        !string.Equals(LatestNodeVersion, DeclaredNodeVersion, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Figures out what 'nem update' would change. This is pure read-only planning:
/// it resolves versions but never prints prompts, writes files, or installs.
/// </summary>
public static class UpdatePlanner
{
    /// <summary>
    /// Plans an update for the env at envDir.
    /// </summary>
    /// <param name="config">The project's nem.json.</param>
    /// <param name="envDir">The .nenv directory.</param>
    /// <param name="nodeReference">
    /// The Node version that "newest supported" tool versions are resolved
    /// against. Defaults to the declared one; pass the new Node version when the
    /// user already accepted a Node update so tool versions stay compatible.
    /// </param>
    public static async Task<UpdatePlan> CreateAsync(NemConfig config, string envDir, string? nodeReference = null)
    {
        string declaredNode = config.NodeVersion ?? "";
        string? latestNode = null;
        try
        {
            latestNode = await NodeDownloadingService.GetLatestStableNodeVersionAsync();
        }
        catch (Exception)
        {
            latestNode = null; // offline or index unreachable: degrade to what is known
        }

        string reference = nodeReference ?? declaredNode;
        var tools = new List<ToolUpdateEntry>();
        foreach (NemToolConfig tool in config.Tools)
        {
            string? latest = null;
            string? error = null;
            try
            {
                latest = ToolService.ResolveVersion(tool.ToolName, range: null, reference, envDir, out error);
            }
            catch (Exception e)
            {
                error = e.Message;
            }

            tools.Add(new ToolUpdateEntry
            {
                Name = tool.ToolName,
                DeclaredVersion = tool.ToolVersion,
                InstalledVersion = ToolService.GetInstalledToolVersion(envDir, tool.ToolName),
                LatestVersion = latest,
                Error = error,
            });
        }

        return new UpdatePlan
        {
            DeclaredNodeVersion = declaredNode,
            InstalledNodeVersion = NodeDownloadingService.GetInstalledNodeVersion(envDir),
            LatestNodeVersion = latestNode,
            Tools = tools,
        };
    }
}
