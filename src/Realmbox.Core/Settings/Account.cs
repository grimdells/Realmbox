namespace Realmbox.Core.Settings
{
    public class Account
    {
        public required string Name { get; set; }
        public required string Base64EMail { get; set; }
        public required string Base64Password { get; set; }

        // Optional group name for batch launching
        public string? Group { get; set; }

        // Optional per-account SOCKS5 proxy settings
        public string? ProxyHost { get; set; }
        public int? ProxyPort { get; set; }
        public string? ProxyUsername { get; set; }
        public string? ProxyPassword { get; set; }

        /// <summary>True when this account has a SOCKS5 proxy configured.</summary>
        public bool HasProxy =>
            !string.IsNullOrWhiteSpace(ProxyHost) && ProxyPort.HasValue;

        /// <summary>Human-readable proxy label shown in the accounts grid.</summary>
        public string ProxyDisplay =>
            HasProxy
                ? (string.IsNullOrWhiteSpace(ProxyUsername)
                    ? $"{ProxyHost}:{ProxyPort}"
                    : $"{ProxyHost}:{ProxyPort} (auth)")
                : "None";
    }
}
