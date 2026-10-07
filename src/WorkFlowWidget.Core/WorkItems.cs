namespace WorkFlowWidget.Core;

public enum TokenResource { MicrosoftGraph, AzureDevOps }

public interface IAccessTokenProvider
{
    Task<string> GetTokenAsync(TokenResource resource, CancellationToken cancellationToken);
}

public sealed record WorkTicket(int Id, string Project, string Title, string State, string WorkItemType, string WebUrl, int Revision = 0);
public sealed record ProjectTickets(string Project, IReadOnlyList<WorkTicket> Tickets, string? Error = null);

public interface ITicketService
{
    Task<IReadOnlyList<ProjectTickets>> GetAssignedActiveAsync(CancellationToken cancellationToken);
    Task CloseAsync(WorkTicket ticket, CancellationToken cancellationToken);
}

public enum EmailCategory { NewBug, NewTicket, HelpdeskQuery }

public static class EmailCategories
{
    public static string Label(this EmailCategory category) => category switch
    {
        EmailCategory.NewBug => "New bug",
        EmailCategory.NewTicket => "New ticket",
        EmailCategory.HelpdeskQuery => "Helpdesk query",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };
}

public sealed record InboxEmail(string Id, string Subject, string Sender, DateTimeOffset ReceivedAt,
    string? ConversationId, string? WebUrl);
public sealed record TrackedEmail(string AccountId, InboxEmail Email, EmailCategory Category,
    bool Replied, bool SyncPending);

public interface IEmailService
{
    Task<IReadOnlyList<InboxEmail>> GetUnreadAsync(CancellationToken cancellationToken);
    Task SetCategoryAsync(string messageId, EmailCategory category, CancellationToken cancellationToken);
}

public interface ITriageStore
{
    Task<IReadOnlyList<TrackedEmail>> ListAsync(string accountId, CancellationToken cancellationToken);
    Task SavePendingAsync(string accountId, InboxEmail email, EmailCategory category, CancellationToken cancellationToken);
    Task SetSyncedAsync(string accountId, string messageId, CancellationToken cancellationToken);
    Task SetRepliedAsync(string accountId, string messageId, bool replied, CancellationToken cancellationToken);
}

public sealed class EmailTriageService(IEmailService emailService, ITriageStore store)
{
    public async Task CategorizeAsync(string accountId, InboxEmail email, EmailCategory category, CancellationToken cancellationToken)
    {
        // Preserve the reminder even if Outlook is temporarily unavailable.
        await store.SavePendingAsync(accountId, email, category, cancellationToken);
        await emailService.SetCategoryAsync(email.Id, category, cancellationToken);
        await store.SetSyncedAsync(accountId, email.Id, cancellationToken);
    }

    public async Task RetrySyncAsync(TrackedEmail email, CancellationToken cancellationToken)
    {
        await emailService.SetCategoryAsync(email.Email.Id, email.Category, cancellationToken);
        await store.SetSyncedAsync(email.AccountId, email.Email.Id, cancellationToken);
    }
}
