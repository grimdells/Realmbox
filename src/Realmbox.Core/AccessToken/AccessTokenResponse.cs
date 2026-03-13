namespace Realmbox.Core.AccessToken
{
    public class AccessTokenResponse
    {
        public required string AccessToken { get; set; }
        public required string AccessTokenTimestamp { get; set; }
        public int AccessTokenExpiration { get; set; }
    }
}
