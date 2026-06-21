using System.Text;

namespace TarkovTracker.Services;

public static class TarkovDevLinks
{
    public static string BuildTaskUrlFromSlug(string slug)
    {
        return string.IsNullOrWhiteSpace(slug)
            ? "https://tarkov.dev/tasks"
            : $"https://tarkov.dev/task/{slug}";
    }

    public static string BuildTaskUrl(string questName)
    {
        return BuildTaskUrlFromSlug(ToTaskSlug(questName));
    }

    public static string BuildWikiUrl(string questName)
    {
        if (string.IsNullOrWhiteSpace(questName))
            return "https://escapefromtarkov.fandom.com/wiki/Quests";

        string wikiTitle = questName.Replace(' ', '_');
        return $"https://escapefromtarkov.fandom.com/wiki/{wikiTitle}";
    }

    public static string ToTaskSlug(string questName)
    {
        if (string.IsNullOrWhiteSpace(questName))
            return string.Empty;

        var sb = new StringBuilder();
        bool lastWasHyphen = false;

        foreach (char c in questName.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                sb.Append('-');
                lastWasHyphen = true;
            }
        }

        return sb.ToString().Trim('-');
    }
}
