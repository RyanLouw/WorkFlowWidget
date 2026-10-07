using Microsoft.EntityFrameworkCore;
using WorkFlowWidget.Core;

namespace WorkFlowWidget.Infrastructure;

public sealed class SqliteTriageStore(string databasePath) : ITriageStore
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkFlowWidget", "workflow.db");

    private async Task<TriageContext> OpenAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        var context = new TriageContext(new DbContextOptionsBuilder<TriageContext>().UseSqlite(connectionString).Options);
        try { await context.Database.EnsureCreatedAsync(cancellationToken); return context; }
        catch { await context.DisposeAsync(); throw; }
    }

    public async Task<IReadOnlyList<TrackedEmail>> ListAsync(string accountId, CancellationToken cancellationToken)
    {
        await using var context = await OpenAsync(cancellationToken);
        var rows = await context.Emails.AsNoTracking().Where(email => email.AccountId == accountId).ToListAsync(cancellationToken);
        return rows.Select(row => row.ToModel()).OrderByDescending(email => email.Email.ReceivedAt).ToArray();
    }

    public async Task SavePendingAsync(string accountId, InboxEmail email, EmailCategory category, CancellationToken cancellationToken)
    {
        await using var context = await OpenAsync(cancellationToken);
        var row = await context.Emails.FindAsync([accountId, email.Id], cancellationToken);
        if (row is null)
        {
            row = new EmailRow { AccountId = accountId, MessageId = email.Id };
            context.Emails.Add(row);
        }
        row.Subject = email.Subject;
        row.Sender = email.Sender;
        row.ReceivedAt = email.ReceivedAt;
        row.ConversationId = email.ConversationId;
        row.WebUrl = email.WebUrl;
        row.Category = category;
        row.SyncPending = true;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetSyncedAsync(string accountId, string messageId, CancellationToken cancellationToken)
    {
        await using var context = await OpenAsync(cancellationToken);
        var row = await context.Emails.FindAsync([accountId, messageId], cancellationToken)
            ?? throw new InvalidOperationException("Local email reminder was not found.");
        row.SyncPending = false;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetRepliedAsync(string accountId, string messageId, bool replied, CancellationToken cancellationToken)
    {
        await using var context = await OpenAsync(cancellationToken);
        var row = await context.Emails.FindAsync([accountId, messageId], cancellationToken)
            ?? throw new InvalidOperationException("Local email reminder was not found.");
        row.Replied = replied;
        row.RepliedAt = replied ? DateTimeOffset.UtcNow : null;
        await context.SaveChangesAsync(cancellationToken);
    }

    private sealed class TriageContext(DbContextOptions<TriageContext> options) : DbContext(options)
    {
        public DbSet<EmailRow> Emails => Set<EmailRow>();
        protected override void OnModelCreating(ModelBuilder builder) => builder.Entity<EmailRow>().HasKey(email => new { email.AccountId, email.MessageId });
    }

    private sealed class EmailRow
    {
        public string AccountId { get; set; } = "";
        public string MessageId { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Sender { get; set; } = "";
        public DateTimeOffset ReceivedAt { get; set; }
        public string? ConversationId { get; set; }
        public string? WebUrl { get; set; }
        public EmailCategory Category { get; set; }
        public bool Replied { get; set; }
        public DateTimeOffset? RepliedAt { get; set; }
        public bool SyncPending { get; set; }
        public TrackedEmail ToModel() => new(AccountId, new InboxEmail(MessageId, Subject, Sender, ReceivedAt, ConversationId, WebUrl), Category, Replied, SyncPending);
    }
}
