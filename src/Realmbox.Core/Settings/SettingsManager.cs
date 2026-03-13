using System.Text.Json;

namespace Realmbox.Core.Settings
{
    public class SettingsManager<T> where T : class
    {
        private readonly string _filePath;

        public SettingsManager(string fileName) => _filePath = GetLocalFilePath(fileName);

        public T? LoadSettings() => File.Exists(_filePath) ? JsonSerializer.Deserialize<T>(File.ReadAllText(_filePath)) : null;

        public void SaveSettings(T settings)
        {
            string json = JsonSerializer.Serialize(settings);
            File.WriteAllText(_filePath, json);
        }

        private static string GetLocalFilePath(string fileName)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string realmboxDirectory = $"{appData}\\Realmbox";

            if (!Directory.Exists(realmboxDirectory))
            {
                // create sub directory within appdata folder
                Directory.CreateDirectory(realmboxDirectory);
            }
            return Path.Combine(realmboxDirectory, fileName);
        }
    }
}
