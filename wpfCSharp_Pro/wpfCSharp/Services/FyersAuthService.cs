namespace WpfCSharp.Services;
public class FyersAuthService
{
    public string AccessToken { get; private set; } = string.Empty;
    public void SetAccessToken(string accessToken) => AccessToken = accessToken;
}
