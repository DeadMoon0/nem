using System;
using System.Collections.Generic;

namespace nem.Services;

/// <summary>
/// What starting a tool takes: the file to run and the environment variables to
/// set for it (none outside an env). 'nem run' starts it itself; 'nem resolve'
/// hands it to the proxy script, which starts it, so nem is gone while the tool runs.
/// </summary>
public sealed record ToolLaunch(string ExecutablePath, IReadOnlyList<KeyValuePair<string, string>> Environment)
{
    /// <summary>
    /// The variables a tool in <paramref name="env"/> runs with: the env's commands
    /// ahead of <paramref name="existingPath"/>, the env's packages for require(),
    /// and the globals folder for 'npm install -g', which must never write into a
    /// store entry other envs share.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> EnvironmentFor(EnvComposition env, string? existingPath) =>
    [
        new("PATH", env.BuildPathVariable(existingPath)),
        new("NODE_PATH", env.NodePath),
        new("npm_config_prefix", env.GlobalsDir),
    ];
}
