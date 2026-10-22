using System.Diagnostics.CodeAnalysis;

namespace nem.Common;

public static class IOPathManager
{
    // Lazy, so a missing config folder fails the commands that need it with a
    // message, not every use of IOPathManager with a type-initializer error.
    static readonly Lazy<IOPathManagerSystem> _system = new(() =>
        Environment.GetEnvironmentVariable(HomeVariable) is { Length: > 0 } home
            ? IOPathManagerSystem.AtNemDir(home)
            : new IOPathManagerSystem(UserConfigDir()));

    public static IOPathManagerSystem System => _system.Value;

    /// <summary>Moves nem's whole folder (store, proxies) elsewhere, e.g. for a test run.</summary>
    public const string HomeVariable = "NEM_HOME";

    /// <summary>
    /// The per-user config folder (%APPDATA% on Windows, $XDG_CONFIG_HOME or
    /// ~/.config on Unix). DoNotVerify: on Unix the default option returns ""
    /// while ~/.config does not exist yet, which on a fresh account made every
    /// nem path relative to the current directory.
    /// </summary>
    static string UserConfigDir() =>
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);

    /// <summary>
    /// The config file of an env. JSONC, because the file is hand-edited and may
    /// carry comments.
    /// </summary>
    public const string ConfigFileName = "nem.jsonc";

    /// <summary>
    /// The nem paths of a folder, whether or not it holds an env yet. Use this to
    /// create one ('nem init'); use <see cref="TryGetEnv"/> or <see cref="TryFindEnv"/>
    /// to work with an existing one.
    /// </summary>
    public static IOPathManagerLocal Local(string path)
    {
        return new IOPathManagerLocal(path);
    }

    /// <summary>
    /// The env <paramref name="dirPath"/> itself holds, if any (relative paths
    /// resolve against the current directory).
    /// </summary>
    public static bool TryGetEnv(string dirPath, [NotNullWhen(true)] out IOPathManagerEnv? env)
    {
        string fullDirPath = Path.GetFullPath(dirPath);
        env = File.Exists(Path.Combine(fullDirPath, ConfigFileName)) ? new IOPathManagerEnv(fullDirPath) : null;
        return env != null;
    }

    /// <summary>
    /// Walks up from <paramref name="startDirPath"/> (relative paths resolve
    /// against the current directory) until a config file turns up. The closest
    /// one wins, so a nested env shadows the one above it.
    /// This is the only way to obtain an <see cref="IOPathManagerEnv"/>, so every
    /// command that uses an env finds it the same way.
    /// </summary>
    public static bool TryFindEnv(string startDirPath, [NotNullWhen(true)] out IOPathManagerEnv? env)
    {
        for (string? dir = Path.GetFullPath(startDirPath); dir != null; dir = Path.GetDirectoryName(dir))
        {
            if (TryGetEnv(dir, out env))
                return true;
        }

        env = null;
        return false;
    }

    public class IOPathManagerLocal(string path)
    {
        public string DirPath { get => path; }

        public string ConfigFilePath { get => Path.Combine(path, ConfigFileName); }
    }

    /// <summary>
    /// The paths of an env that was actually found. Only <see cref="TryGetEnv"/> and
    /// <see cref="TryFindEnv"/> create one, so holding an instance is proof that the
    /// config file was there - no caller has to re-check, and no command can invent
    /// its own idea of where the env lives.
    /// </summary>
    public class IOPathManagerEnv : IOPathManagerLocal
    {
        internal IOPathManagerEnv(string dirPath) : base(dirPath) { }
    }

    /// <summary>
    /// The machine-wide nem folders (caches, proxies) below the user's config folder.
    /// </summary>
    public class IOPathManagerSystem
    {
        /// <param name="userConfigDir">
        /// The user's config folder; nem's folder is 'nem' inside it. It must be an
        /// absolute path: a relative one would put the proxy dir (and the PATH entry
        /// 'nem setup' writes) relative to wherever nem happens to run.
        /// </param>
        public IOPathManagerSystem(string userConfigDir)
        {
            if (!Path.IsPathFullyQualified(userConfigDir))
                throw new InvalidOperationException(
                    $"Cannot determine the user config folder (got '{userConfigDir}'). Set HOME or XDG_CONFIG_HOME to an absolute path.");
            DirPath = Path.Combine(userConfigDir, "nem");
        }

        IOPathManagerSystem(string nemDir, bool isNemDir) => DirPath = nemDir;

        /// <summary>nem's folder set directly (<see cref="HomeVariable"/>), e.g. to keep a test run apart.</summary>
        public static IOPathManagerSystem AtNemDir(string nemDir) =>
            Path.IsPathFullyQualified(nemDir)
                ? new IOPathManagerSystem(nemDir, true)
                : throw new InvalidOperationException($"{HomeVariable} must be an absolute path (got '{nemDir}').");

        public string DirPath { get; }

        /// <summary>Node versions and tools, shared by every env of this user.</summary>
        public string StoreDirPath { get => Path.Combine(DirPath, "store"); }
        /// <summary>Where 'npm install -g' inside an env goes, one folder per Node major version.</summary>
        public string GlobalsDirPath { get => Path.Combine(DirPath, "globals"); }

        public string ProxyDirName { get; } = "proxy";
        public string ProxyDirPath { get => Path.Combine(DirPath, ProxyDirName); }
    }
}
