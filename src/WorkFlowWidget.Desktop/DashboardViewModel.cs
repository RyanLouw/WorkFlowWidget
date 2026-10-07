using System.Collections.ObjectModel;
using System.ComponentModel;
using WorkFlowWidget.Core;

namespace WorkFlowWidget.Desktop;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private readonly ITicketService tickets;
    private readonly IEmailService emails;
    private readonly ITriageStore store;
    private readonly EmailTriageService triage;
    private bool busy;
    private IReadOnlyList<InboxEmail> inbox = [];
    public LoginViewModel Login { get; }
    public ObservableCollection<ProjectTickets> Projects { get; } = [];
    public ObservableCollection<InboxEmail> Unread { get; } = [];
    public ObservableCollection<TrackedEmail> NewBugs { get; } = [];
    public ObservableCollection<TrackedEmail> NewTickets { get; } = [];
    public ObservableCollection<TrackedEmail> Helpdesk { get; } = [];
    public ObservableCollection<TrackedEmail> NeedReply { get; } = [];
    public bool CanUse => Login.User is not null && !busy && Login.CanAct;
    public bool CanAuthenticate => !busy && Login.CanAct;
    public string? TicketError { get; private set; }
    public string? EmailError { get; private set; }
    public string WorkStatus => Login.User is null ? "Sign in to load your email and tickets." : busy ? "Updating…" : "Refresh to check for new work.";
    public string TicketSummary { get; private set; } = "Tickets have not been loaded.";
    public string EmailSummary { get; private set; } = "Email has not been loaded.";
    public event PropertyChangedEventHandler? PropertyChanged;

    public DashboardViewModel(LoginViewModel login, ITicketService tickets, IEmailService emails, ITriageStore store)
    {
        Login = login;
        this.tickets = tickets;
        this.emails = emails;
        this.store = store;
        triage = new EmailTriageService(emails, store);
        Login.PropertyChanged += (_, _) => Notify();
    }

    public async Task SessionChangedAsync(CancellationToken cancellationToken)
    {
        Clear();
        if (Login.User is not null) await RefreshAsync(cancellationToken);
        Notify();
    }

    public Task RefreshAsync(CancellationToken cancellationToken) => RunAsync(async () =>
    {
        TicketError = null;
        EmailError = null;
        // Sequential token requests prevent overlapping broker consent dialogs.
        try
        {
            var groups = await tickets.GetAssignedActiveAsync(cancellationToken);
            Replace(Projects, groups);
            TicketSummary = $"{groups.Sum(group => group.Tickets.Count)} active tickets across {groups.Count} projects."
                + (groups.Any(group => group.Error is not null) ? " Some projects could not be loaded." : "");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { TicketError = "Could not load tickets. Check AzureDevOps settings, access and consent; then refresh. Previous results may be out of date."; }
        // Local reminders still load if Graph is unavailable.
        try { await ReloadLocalAsync(cancellationToken); }
        catch (Exception) { EmailError = "Could not read local reminders. Check access to your local database."; }
        try
        {
            inbox = await emails.GetUnreadAsync(cancellationToken);
            await ReloadLocalAsync(cancellationToken);
            EmailSummary = $"{Unread.Count} unread emails awaiting triage; {NeedReply.Count} need a reply.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { EmailError = "Could not load email. Check Mail.ReadWrite consent and your connection; then refresh. Previous results may be out of date."; }
    });

    public Task CategorizeAsync(InboxEmail email, EmailCategory category, CancellationToken cancellationToken) => RunAsync(async () =>
    {
        EmailError = null;
        try { await triage.CategorizeAsync(Login.User!.Id, email, category, cancellationToken); }
        catch (Exception) { EmailError = "Category could not sync with Outlook. If saved locally, it is marked Sync pending; use Retry sync."; }
        await ReloadLocalAsync(cancellationToken);
    });

    public Task RetrySyncAsync(TrackedEmail email, CancellationToken cancellationToken) => RunAsync(async () =>
    {
        EmailError = null;
        try { await triage.RetrySyncAsync(email, cancellationToken); }
        catch (Exception) { EmailError = "Outlook category sync failed. Check connectivity and permissions; a deleted message cannot be synced."; }
        await ReloadLocalAsync(cancellationToken);
    });

    public Task SetRepliedAsync(TrackedEmail email, bool replied, CancellationToken cancellationToken) => RunAsync(async () =>
    {
        EmailError = null;
        await store.SetRepliedAsync(Login.User!.Id, email.Email.Id, replied, cancellationToken);
        await ReloadLocalAsync(cancellationToken);
    });

    public Task CloseTicketAsync(WorkTicket ticket, CancellationToken cancellationToken) => RunAsync(async () =>
    {
        TicketError = null;
        try
        {
            await tickets.CloseAsync(ticket, cancellationToken);
            // Remove only after the server confirms the update. Other results stay visible.
            Replace(Projects, Projects.Select(group => group.Project == ticket.Project
                ? group with { Tickets = group.Tickets.Where(item => item.Id != ticket.Id).ToArray() } : group).ToArray());
            TicketSummary = $"{Projects.Sum(group => group.Tickets.Count)} active tickets across {Projects.Count} projects.";
        }
        catch (Exception) { TicketError = "Could not close the ticket. Refresh to check for changes, and verify the project allows this state transition."; }
    });

    private async Task ReloadLocalAsync(CancellationToken cancellationToken)
    {
        var records = await store.ListAsync(Login.User!.Id, cancellationToken);
        Replace(NewBugs, records.Where(email => email.Category == EmailCategory.NewBug));
        Replace(NewTickets, records.Where(email => email.Category == EmailCategory.NewTicket));
        Replace(Helpdesk, records.Where(email => email.Category == EmailCategory.HelpdeskQuery));
        Replace(NeedReply, records.Where(email => !email.Replied));
        var trackedIds = records.Select(email => email.Email.Id).ToHashSet();
        Replace(Unread, inbox.Where(email => !trackedIds.Contains(email.Id)));
        EmailSummary = $"{Unread.Count} unread emails awaiting triage; {NeedReply.Count} need a reply.";
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (!CanUse) return;
        busy = true;
        Notify();
        try { await action(); }
        catch (OperationCanceledException) { EmailError = "Update cancelled. Refresh to try again."; }
        catch (Exception) { EmailError = "Could not update local work data. Check your local database and try again."; }
        finally { busy = false; Notify(); }
    }

    private void Clear()
    {
        Projects.Clear(); Unread.Clear(); NewBugs.Clear(); NewTickets.Clear(); Helpdesk.Clear(); NeedReply.Clear();
        inbox = [];
        TicketError = null; EmailError = null;
        TicketSummary = "Tickets have not been loaded."; EmailSummary = "Email has not been loaded.";
    }
    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        var snapshot = items.ToArray();
        collection.Clear();
        foreach (var item in snapshot) collection.Add(item);
    }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
