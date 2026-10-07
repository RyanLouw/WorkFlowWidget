# WorkFlowWidget

A Windows-only, borderless, always-on-top WPF widget for Microsoft work-account sign-in, Azure DevOps tickets and email triage. The desktop UI, logic contracts and data access live in separate projects but run in one application. No web API server or database server is required.

## Features

- Microsoft sign-in through MSAL and the Windows broker. Identity is verified with Graph `/me` before work data is loaded.
- Tickets assigned to you, grouped by Azure DevOps project. An empty project list discovers all projects you can access in the configured organization. Completed and Removed state categories are excluded using the project's work item type definitions, including custom state names.
- Open tickets in a browser, or close them after confirmation. Closing uses a completed state defined by the project and a revision check; process rules or concurrent changes can reject the action.
- Unread Inbox email, with New bug, New ticket and Helpdesk query classification buttons. These labels categorize email; they do not create Azure DevOps work items.
- Separate email category tabs and a Need to reply list. Categorized messages leave the widget's Unread triage list; categorization does not mark messages read in Outlook.
- Outlook category updates preserve unrelated categories and retry one concurrent change. Immutable Graph IDs let saved reminders survive moves within the mailbox.
- Persistent local SQLite reminders, scoped by verified account. A failed Outlook update remains **Sync pending** and can be retried.
- Open email in Outlook on the web. **Mark replied** records completion locally; **Needs reply** restores a reminder. These actions do not send mail or detect replies automatically.
- Manual refresh. Individual project errors are shown, and failure of one service does not block the other service's data.

Planner, automatic reply detection, rules and manager summaries are future features.

## Projects

| Project | Responsibility |
| --- | --- |
| `WorkFlowWidget.Desktop` | WPF widget and view models; no database queries or API requests |
| `WorkFlowWidget.Core` | Authentication, ticket and email contracts, models and triage orchestration |
| `WorkFlowWidget.Infrastructure` | MSAL, Graph, Azure DevOps, EF Core and SQLite |
| `WorkFlowWidget.Core.Tests` | Service tests with controlled HTTP responses, real SQLite persistence and portable view-model tests |

Infrastructure targets .NET 8 so its data services can be tested on Linux. Its broker authentication implementation is explicitly Windows-only. Tests reference the actual Core and Infrastructure projects; platform-independent Desktop view models are linked into the test assembly.

## Configure Microsoft sign-in

1. In Microsoft Entra admin center, open **App registrations → New registration**. Choose accounts in your organizational directory only (single tenant).
2. Copy the **Application (client) ID** and **Directory (tenant) ID** from Overview.
3. Under **Authentication**, add a **Mobile and desktop applications** platform with broker redirect URI `ms-appx-web://Microsoft.AAD.BrokerPlugin/YOUR-CLIENT-ID` (replace the final part with your actual client ID). Also register `http://localhost` for system-browser fallback. Enable **Allow public client flows**. Do not create a client secret for this desktop app.
4. Under **API permissions**, add Microsoft Graph's **delegated** `User.Read` and `Mail.ReadWrite`. For Azure DevOps, add its **delegated** `user_impersonation` permission. Get consent according to your organization's policy; an administrator may need to approve it. `Mail.Send` is not requested because this version does not send emails.
5. Replace the placeholders in `src/WorkFlowWidget.Desktop/appsettings.json`:

   ```json
   {
     "Authentication": {
       "ClientId": "YOUR-APPLICATION-CLIENT-ID",
       "TenantId": "YOUR-DIRECTORY-TENANT-ID"
     },
     "AzureDevOps": {
       "Organization": "YOUR-AZURE-DEVOPS-ORGANIZATION",
       "Projects": []
     }
   }
   ```

   `Organization` is the name from `https://dev.azure.com/NAME`, not the full URL. Leave `Projects` empty to load every accessible project, or specify exact names such as `["Customer Portal", "Internal Tools"]`. This version supports one organization per configuration.

   Alternatively, put the complete JSON in `%LocalAppData%\WorkFlowWidget\appsettings.json` to keep local settings outside Git and preserve them across updates. The local file takes precedence over the copy beside the executable; files are not merged. Restart after changing configuration. IDs are public identifiers; never put passwords, tokens or client secrets here.

Graph and Azure DevOps use separate token requests for the same signed-in account. Azure DevOps uses `499b84ac-1321-427f-aa17-267ca6975798/.default`. Loading tickets may prompt for consent the first time. The app refuses a token for a different account. Your existing Azure DevOps project permissions still apply; app consent does not grant access to projects or permission to close work items.

The app displays **Signed in · verified** only after Graph confirms a valid profile. MSAL manages tokens; they are not logged or saved in appsettings or SQLite. Startup attempts silent login when MSAL returns exactly one cached account. Sign-out removes this app's MSAL account cache and clears visible work data; it does not sign you out of Windows or Outlook. Saved local history remains available when you sign into the same account again.

## Local data

The app creates `%LocalAppData%\WorkFlowWidget\workflow.db` automatically. This SQLite file stores email identifiers, subjects, sender labels, received dates, categories, sync status and reply status/timestamps. It does not store mail bodies or access tokens. Data is stored locally without application-level database encryption; Windows profile permissions protect the file. Do not commit this database to Git. The database stays separate from the EXE so replacing the application preserves history.

Category tabs show history classified through this widget, not every previously categorized message in Outlook. Reply status is manual. Missing/deleted messages cannot be synced with Outlook but their local reminders remain. The widget does not automatically resend pending updates; use **Retry sync** after reviewing the reminder.

Projects and unread email are paginated. Work item details are fetched in batches of at most 200. Azure DevOps WIQL has a service result limit; queries exceeding it are reported as failed project loads rather than silently presented as complete. Loading all projects may take time; configure a project subset if appropriate. If a refresh fails, earlier results may remain visible with a warning that they may be out of date.

## Build and run

Install the .NET 8 SDK. On Windows:

```powershell
dotnet build WorkFlowWidget.sln
dotnet test tests/WorkFlowWidget.Core.Tests/WorkFlowWidget.Core.Tests.csproj
dotnet run --project src/WorkFlowWidget.Desktop
```

Drag the header to move the widget, resize from its edge, and use × to close it. Sign in to load tickets and email, switch tabs to triage, and use **Refresh** to check for new work. Placeholder settings show **Setup required** instead of a fake login.

Linux can compile WPF using `EnableWindowsTargeting` (already set), and run the portable tests, but the UI and Windows broker login require Windows. Initialize this cloud environment's shell first:

```bash
export DOTNET_ROOT=/workspace/.dotnet
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME=/workspace/.dotnet-home
export NUGET_PACKAGES=/workspace/.nuget/packages
```

## Publish for Windows

```powershell
dotnet publish src/WorkFlowWidget.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/win-x64
```

Distribute the publish folder, including `appsettings.json`. Supporting managed assemblies are bundled in the EXE; native dependencies can extract at runtime. Configuration and the user's database stay separate. This self-contained build does not require a separate .NET installation.

## Test the complete flow on Windows

1. Launch with placeholder IDs: expect **Setup required**.
2. Configure both services, restart and sign in. Approve Graph and Azure DevOps consent as permitted by your tenant. Confirm the header shows your correct identity.
3. Compare the Tickets tab against Azure DevOps work assigned to you across the configured projects. Confirm completed/removed items are absent and inaccessible projects show errors.
4. Open a ticket. For a disposable test work item, confirm **Close ticket** completes it. A stale revision or blocked process transition must show an error and keep it visible.
5. Confirm unread Inbox email appears across all pages. Classify a test message in each category. Check Outlook preserves its unrelated categories and adds the selected widget label. No work item should be created.
6. Check each category tab and Need to reply. Open a message, reply in Outlook, then mark it replied in the widget. Confirm it leaves Need to reply; **Needs reply** restores it.
7. Restart the widget and verify triage history survives. Test an Outlook sync failure and retry it; confirm the reminder remains locally while pending.
8. Sign out and confirm all visible work data clears. If you test another account, its local history must be separate.

Interactive checks require Windows and a real registration. Compiling and passing controlled HTTP/SQLite tests does not prove live consent, broker configuration or tenant-specific process rules work.
