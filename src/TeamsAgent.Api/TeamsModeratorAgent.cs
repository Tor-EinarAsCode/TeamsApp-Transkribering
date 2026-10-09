using System.Text.Json;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Core.Models;
using TeamsAgent.Core;
using TeamsAgent.Persistence;
namespace TeamsAgent.Api;
public sealed class TeamsModeratorAgent : AgentApplication
{
    public TeamsModeratorAgent(AgentApplicationOptions options, SqlStore store, Moderator moderator, IConfiguration config) : base(options)
    {
        async Task Capture(ITurnContext ctx, CancellationToken ct)
        {
            if (ctx.Activity.ChannelId != "msteams") return;
            var actual = Tenant(ctx.Activity); TenantGuard.Require(Guid.Parse(config["TenantId"]!), actual);
            await store.SaveConversationAsync(ctx.Activity.Conversation.Id, JsonSerializer.Serialize(ctx.Activity.GetConversationReference()), ct);
        }
        OnActivity(ActivityTypes.ConversationUpdate, async (ctx, state, ct) => await Capture(ctx, ct));
        OnActivity(ActivityTypes.InstallationUpdate, async (ctx, state, ct) => await Capture(ctx, ct));
        OnActivity(ActivityTypes.Message, async (ctx, state, ct) =>
        {
            if (ctx.Activity.ChannelId != "msteams") return;
            var tenant = Tenant(ctx.Activity); TenantGuard.Require(Guid.Parse(config["TenantId"]!), tenant);
            await Capture(ctx, ct);
            var mentions = ctx.Activity.GetMentions().Select(m => new TeamsAgent.Core.Mention(m.Mentioned.Id)).ToArray();
            var reply = await moderator.HandleAsync(tenant, ctx.Activity.Recipient.Id, "message", ctx.Activity.Text ?? "", mentions, ct);
            if (reply is not null) await ctx.SendActivityAsync(MessageFactory.Text(reply), ct);
        });
    }
    static Guid Tenant(IActivity activity) { using var doc = JsonDocument.Parse(JsonSerializer.Serialize(activity.ChannelData)); return doc.RootElement.TryGetProperty("tenant", out var t) && t.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var tenant) ? tenant : Guid.Empty; }
}
