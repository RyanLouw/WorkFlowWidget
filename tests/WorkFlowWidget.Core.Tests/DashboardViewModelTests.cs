using WorkFlowWidget.Core;
using WorkFlowWidget.Desktop;
using Xunit;

namespace WorkFlowWidget.Core.Tests;

public sealed class DashboardViewModelTests
{
    [Fact]
    public async Task FailedTicketsDoNotPreventEmailAndRemindersLoadingAndSignOutClearsData()
    {
        var login = new LoginViewModel(new Auth());
        await login.AuthenticateAsync(CancellationToken.None);
        var remote = new Emails();
        var tracked = new TrackedEmail("account", remote.Items[0], EmailCategory.NewBug, false, false);
        var dashboard = new DashboardViewModel(login, new Tickets { Fail = true }, remote, new Store { Records = [tracked] });
        await dashboard.SessionChangedAsync(CancellationToken.None);
        Assert.NotNull(dashboard.TicketError);
        Assert.Null(dashboard.EmailError);
        Assert.Single(dashboard.NewBugs);
        Assert.Single(dashboard.NeedReply);
        Assert.Single(dashboard.Unread);
        Assert.Equal("untracked", dashboard.Unread[0].Id);
        await login.AuthenticateAsync(CancellationToken.None);
        await dashboard.SessionChangedAsync(CancellationToken.None);
        Assert.Empty(dashboard.NewBugs);
        Assert.Empty(dashboard.NeedReply);
        Assert.Empty(dashboard.Unread);
        Assert.False(dashboard.CanUse);
    }

    [Fact]
    public async Task FailedEmailStillLoadsTicketsAndLocalReplyReminders()
    {
        var login = new LoginViewModel(new Auth());
        await login.AuthenticateAsync(CancellationToken.None);
        var remote = new Emails { Fail = true };
        var dashboard = new DashboardViewModel(login, new Tickets(), remote,
            new Store { Records = [new TrackedEmail("account", remote.Items[0], EmailCategory.HelpdeskQuery, false, true)] });
        await dashboard.SessionChangedAsync(CancellationToken.None);
        Assert.Null(dashboard.TicketError);
        Assert.NotNull(dashboard.EmailError);
        Assert.Single(dashboard.Projects);
        Assert.Single(dashboard.NeedReply);
        Assert.Single(dashboard.Helpdesk);
    }

    private sealed class Auth : IAuthenticationService
    {
        public Task<SignedInUser?> RestoreSessionAsync(CancellationToken cancellationToken) => Task.FromResult<SignedInUser?>(null);
        public Task<SignedInUser> SignInAsync(CancellationToken cancellationToken) => Task.FromResult(new SignedInUser("account", "Taylor", null));
        public Task SignOutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class Tickets : ITicketService
    {
        public bool Fail { get; init; }
        public Task<IReadOnlyList<ProjectTickets>> GetAssignedActiveAsync(CancellationToken cancellationToken) => Fail
            ? Task.FromException<IReadOnlyList<ProjectTickets>>(new HttpRequestException("No access"))
            : Task.FromResult<IReadOnlyList<ProjectTickets>>([new ProjectTickets("Portal", [])]);
        public Task CloseAsync(WorkTicket ticket, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class Emails : IEmailService
    {
        public bool Fail { get; init; }
        public InboxEmail[] Items { get; } = [new("tracked", "Subject", "Sender", DateTimeOffset.UtcNow, null, null), new("untracked", "Other", "Sender", DateTimeOffset.UtcNow, null, null)];
        public Task<IReadOnlyList<InboxEmail>> GetUnreadAsync(CancellationToken cancellationToken) => Fail
            ? Task.FromException<IReadOnlyList<InboxEmail>>(new HttpRequestException("Offline")) : Task.FromResult<IReadOnlyList<InboxEmail>>(Items);
        public Task SetCategoryAsync(string messageId, EmailCategory category, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class Store : ITriageStore
    {
        public IReadOnlyList<TrackedEmail> Records { get; init; } = [];
        public Task<IReadOnlyList<TrackedEmail>> ListAsync(string accountId, CancellationToken cancellationToken) => Task.FromResult(Records);
        public Task SavePendingAsync(string accountId, InboxEmail email, EmailCategory category, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetSyncedAsync(string accountId, string messageId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetRepliedAsync(string accountId, string messageId, bool replied, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
