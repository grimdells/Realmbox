namespace Realmbox.Core.Settings
{
    public class UserSettings
    {
        public string? ExaltPath { get; set; }
        public string? ClientPath { get; set; }
        public List<Account>? Accounts { get; set; }
        public string? DeviceToken { get; set; }
        public int GroupLaunchDelay { get; set; } = 10;
        public bool AutoInject { get; set; } = false;
        public int AutoInjectDelay { get; set; } = 10;
    }
}
