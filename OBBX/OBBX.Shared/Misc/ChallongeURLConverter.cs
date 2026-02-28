namespace OBBX.Shared.Misc;

public class ChallongeURLConverter
{
    public static string? GetTournamentIdentifier(string url)
    {
        var urlSplit = url.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var lastSegment = urlSplit.Last();

        // Check for subdomain
        if (urlSplit.Length >= 3 && urlSplit[urlSplit.Length - 2].Contains('.'))
        {
            var domainParts = urlSplit[urlSplit.Length - 2].Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (domainParts.Length > 2)
            {
                var subdomain = domainParts[0];
                lastSegment = $"{subdomain}-{lastSegment}";
            }
        }
        return lastSegment;
    }
}