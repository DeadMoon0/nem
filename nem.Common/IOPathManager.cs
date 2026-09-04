namespace nem.Common;

    public static class IOPathManager
    {
        public static IOPathManagerSystem System { get; } = new IOPathManagerSystem();

        public static IOPathManagerLocal Local(string path)
        {
            return new IOPathManagerLocal(path);
        }

        public class IOPathManagerLocal(string path)
        {
            public string[] ConfigFileNames { get; } = ["nem.jsonc", "nem.json"];
            public string PreferredConfigFileName => ConfigFileNames[0];
            public string ConfigFileName => PreferredConfigFileName;
            public string ConfigFilePath { get => Path.Combine(path, PreferredConfigFileName); }
            public string? FindExistingConfigPath
            {
                get
                {
                    foreach (var name in ConfigFileNames)
                    {
                        var filePath = Path.Combine(path, name);
                        if (File.Exists(filePath)) return filePath;
                    }
                    return null;
                }
            }

            public string EnvDirName { get; } = ".nenv";
            public string EnvDirPath { get => Path.Combine(path, EnvDirName); }
            public string EnsureEnvDirPath() { if (!Directory.Exists(EnvDirPath)) Directory.CreateDirectory(EnvDirPath); return EnvDirPath; }
        }

    public class IOPathManagerSystem
    {
        public string SystemDirName { get; } = "nem";
        public string DirPath { get => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), SystemDirName); }

        public string DownloadCacheDirName { get; } = "download";
        public string DownloadCacheDirPath { get => Path.Combine(DirPath, DownloadCacheDirName); }
        public string ExtractCacheDirName { get; } = "extract";
        public string ExtractCacheDirPath { get => Path.Combine(DirPath, ExtractCacheDirName); }
        public string ProxyDirName { get; } = "proxy";
        public string ProxyDirPath { get => Path.Combine(DirPath, ProxyDirName); }
    }
}