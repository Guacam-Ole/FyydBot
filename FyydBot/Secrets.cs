namespace FyydBot;

public class Secrets
{
    public MastodonSecrets Mastodon { get; set; }

    public class MastodonSecrets
    {
        public string Instance { get; set; }
        public string AccessToken { get; set; }
    }   
}