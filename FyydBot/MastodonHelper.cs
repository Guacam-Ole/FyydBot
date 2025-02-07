using System.Net;
using System.Text.RegularExpressions;
using Mastonet.Entities;

namespace FyydBot;

public static class MastodonHelper
{
    public static string GetUserHandle(this Account userAccount)
    {
        return "@" + userAccount.AccountName + " ";
    }

    public static string GetUserHandle(this Status status)
    {
        return GetUserHandle(status.Account);
    }
    
    public static string StripHtml(this string content)
    {
        content = content.Replace("</p>", " \n\n");
        content = content.Replace("<br />", " \n");
        content = Regex.Replace(content, "<[a-zA-Z/].*?>", string.Empty);
        content = WebUtility.HtmlDecode(content);
        return content;
    }

    public static string Escape(this string queryParameter)
    {
        return Uri.EscapeDataString(queryParameter);
    }
}