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
        public string ConfigFileName { get; } = "nem.json";
        public string ConfigFilePath { get => Path.Combine(path, ConfigFileName); }

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