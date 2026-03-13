namespace Realmbox.Core.AccessToken
{
    public class AccessTokenRequest(string guid, string password, string deviceToken)
    {
        public string? DeviceToken { get; set; } = deviceToken;
        public string? Guid { get; set; } = guid;
        public string? Password { get; set; } = password;
    }
}
