namespace TeamsAgent.Core;
public sealed class Moderator(Guid tenant, Func<string, CancellationToken, Task<string>> ai)
{
    public async Task<string?> HandleAsync(Guid actualTenant, string botId, string activityType, string text, IReadOnlyList<Mention> mentions, CancellationToken ct)
    {
        if (activityType != "message" || !MentionRouter.IsInvoked(botId, mentions)) return null;
        TenantGuard.Require(tenant, actualTenant);
        if (string.IsNullOrWhiteSpace(text)) return "Hva ønsker du hjelp med?";
        if (text.Length > 12000) return "Meldingen er for lang. Send et kortere utdrag.";
        return await ai(text, ct);
    }
}
