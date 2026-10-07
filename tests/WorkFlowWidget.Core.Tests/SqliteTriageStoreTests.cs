using WorkFlowWidget.Core;
using WorkFlowWidget.Infrastructure;
using Xunit;

namespace WorkFlowWidget.Core.Tests;

public sealed class SqliteTriageStoreTests
{
    [Fact]
    public async Task PersistsCategoriesAndReplyStatusAcrossInstancesAndIsolatesAccounts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "workflow-tests-" + Guid.NewGuid());
        var path = Path.Combine(directory, "workflow.db");
        try
        {
            var email = new InboxEmail("same-id", "Question", "Taylor", DateTimeOffset.UtcNow, "thread", null);
            var store = new SqliteTriageStore(path);
            await store.SavePendingAsync("account-a", email, EmailCategory.NewBug, CancellationToken.None);
            await store.SavePendingAsync("account-b", email, EmailCategory.HelpdeskQuery, CancellationToken.None);
            await store.SetSyncedAsync("account-a", email.Id, CancellationToken.None);
            await store.SetRepliedAsync("account-a", email.Id, true, CancellationToken.None);
            var reopened = new SqliteTriageStore(path);
            var a = Assert.Single(await reopened.ListAsync("account-a", CancellationToken.None));
            var b = Assert.Single(await reopened.ListAsync("account-b", CancellationToken.None));
            Assert.Equal(EmailCategory.NewBug, a.Category);
            Assert.True(a.Replied);
            Assert.False(a.SyncPending);
            Assert.Equal(EmailCategory.HelpdeskQuery, b.Category);
            Assert.False(b.Replied);
            Assert.True(b.SyncPending);
            Assert.Empty(await reopened.ListAsync("account-c", CancellationToken.None));
            await reopened.SetRepliedAsync("account-a", email.Id, false, CancellationToken.None);
            Assert.False(Assert.Single(await reopened.ListAsync("account-a", CancellationToken.None)).Replied);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task FailedOutlookUpdateLeavesDurablePendingReminderAndRetryCompletesSync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "workflow-tests-" + Guid.NewGuid());
        var path = Path.Combine(directory, "workflow.db");
        try
        {
            var email = new InboxEmail("message", "Question", "Taylor", DateTimeOffset.UtcNow, null, null);
            var remote = new FailingEmailService();
            var store = new SqliteTriageStore(path);
            var service = new EmailTriageService(remote, store);
            await Assert.ThrowsAsync<HttpRequestException>(() => service.CategorizeAsync("account", email, EmailCategory.NewTicket, CancellationToken.None));
            var pending = Assert.Single(await new SqliteTriageStore(path).ListAsync("account", CancellationToken.None));
            Assert.True(pending.SyncPending);
            Assert.False(pending.Replied);
            remote.Fail = false;
            await service.RetrySyncAsync(pending, CancellationToken.None);
            Assert.False(Assert.Single(await store.ListAsync("account", CancellationToken.None)).SyncPending);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class FailingEmailService : IEmailService
    {
        public bool Fail { get; set; } = true;
        public Task<IReadOnlyList<InboxEmail>> GetUnreadAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<InboxEmail>>([]);
        public Task SetCategoryAsync(string messageId, EmailCategory category, CancellationToken cancellationToken) => Fail
            ? Task.FromException(new HttpRequestException("Offline")) : Task.CompletedTask;
    }
}
