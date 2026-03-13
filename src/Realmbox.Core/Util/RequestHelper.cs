using System.Net;
using System.Xml.Linq;
using Realmbox.Core.AccessToken;
using Realmbox.Core.Exceptions;

namespace Realmbox.Core.Util
{
    /// <summary>
    /// Helper class for making requests to the Realm of the Mad God API.
    /// </summary>
    public class RequestHelper
    {
        /// <summary>
        /// Requests an access token from the Realm of the Mad God API.
        /// Optionally routes the request through a SOCKS5 proxy.
        /// </summary>
        public static async Task<AccessTokenResponse> RequestAccessToken(
            AccessTokenRequest accessTokenRequest,
            string? proxyHost = null,
            int? proxyPort = null,
            string? proxyUsername = null,
            string? proxyPassword = null)
        {
            bool isSteam = false;
            string steamId = "";

            if (accessTokenRequest.Guid!.Contains("steamworks:"))
            {
                isSteam = true;
                steamId = accessTokenRequest.Guid.Split(":")[1];
            }

            HttpClient client = CreateHttpClient(proxyHost, proxyPort, proxyUsername, proxyPassword);

            using (client)
            {
                List<KeyValuePair<string, string>> content =
                [
                    new("clientToken", accessTokenRequest.DeviceToken!),
                    new("guid", accessTokenRequest.Guid),
                    new("game_net", isSteam ? "Unity_steam" : "Unity"),
                    new("play_platform", isSteam ? "Unity_steam" : "Unity"),
                    new("game_net_user_id", steamId),
                ];

                if (isSteam)
                {
                    content.Add(new("steamid", steamId));
                    content.Add(new("secret", accessTokenRequest.Password!));
                }
                else
                {
                    content.Add(new("password", accessTokenRequest.Password!));
                }

                HttpRequestMessage requestMessage = new()
                {
                    RequestUri = new Uri(Constants.RealmApiVerifyEndpoint),
                    Method = HttpMethod.Post,
                    Content = new FormUrlEncodedContent(content!),
                };

                HttpResponseMessage response = await client.SendAsync(requestMessage).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        string c = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        XElement xml = XElement.Parse(c);

                        return new AccessTokenResponse
                        {
                            AccessToken = xml.Descendants().First(node => node.Name == "AccessToken").Value,
                            AccessTokenExpiration = Convert.ToInt32(xml.Descendants().First(node => node.Name == "AccessTokenExpiration").Value),
                            AccessTokenTimestamp = xml.Descendants().First(node => node.Name == "AccessTokenTimestamp").Value,
                        };
                    }
                    catch (Exception)
                    {
                        throw new AccessTokenParseFailedException();
                    }
                }
                throw new AccessTokenRetrievalFailedException();
            }
        }

        /// <summary>
        /// Creates an HttpClient, optionally configured to use a SOCKS5 proxy.
        /// </summary>
        private static HttpClient CreateHttpClient(
            string? proxyHost,
            int? proxyPort,
            string? proxyUsername,
            string? proxyPassword)
        {
            if (string.IsNullOrWhiteSpace(proxyHost) || !proxyPort.HasValue)
            {
                return new HttpClient();
            }

            // Build SOCKS5 proxy URI: socks5://host:port
            string proxyUri = $"socks5://{proxyHost}:{proxyPort}";

            SocketsHttpHandler handler = new()
            {
                Proxy = string.IsNullOrWhiteSpace(proxyUsername)
                    ? new WebProxy(proxyUri)
                    : new WebProxy(proxyUri)
                      {
                          Credentials = new NetworkCredential(proxyUsername, proxyPassword)
                      },
                UseProxy = true,
            };

            return new HttpClient(handler);
        }
    }
}
