using System;
using System.IO;
using System.Text.Json;

namespace VirtualStreamPlayer.Config
{
    public class AppConfig
    {
        public string StreamUrl { get; set; } = "https://cast.zuperdns.net/8006/stream";
        public bool AutoReconnect { get; set; } = true;
        public bool ConnectOnStartup { get; set; } = false;
        public string PipeName { get; set; } = "VirtualStreamPlayer_Audio";
        public int ApiPort { get; set; } = 8770;

        /// <summary>Max backoff between reconnect attempts, in seconds.</summary>
        public int MaxReconnectDelaySeconds { get; set; } = 30;
    }

    public static class ConfigManager
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VirtualStreamPlayer");

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

        public static AppConfig Load()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                    if (cfg != null) return cfg;
                }
            }
            catch (Exception ex)
            {
                Logging.Logger.Error("No se pudo cargar config.json, usando valores por defecto", ex);
            }
            return new AppConfig();
        }

        public static void Save(AppConfig config)
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                Logging.Logger.Error("No se pudo guardar config.json", ex);
            }
        }
    }
}
