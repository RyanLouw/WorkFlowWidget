# WorkFlowWidget

A Windows-only, always-on-top WPF widget. This first feature provides Microsoft work-account login, verified identity and widget sign-out. Mail, tickets, planner and SQLite are future features.

## Projects

- `WorkFlowWidget.Desktop`: WPF window and login view model; no Graph or database requests.
- `WorkFlowWidget.Core`: authentication interface and account model.
- `WorkFlowWidget.Infrastructure`: MSAL Windows broker, configuration and Microsoft Graph verification.
- `WorkFlowWidget.Core.Tests`: portable tests for login state and Graph verification. Platform-independent production files are linked so these tests run on Linux.

All application projects run in one desktop process. There is no web API server. Normal builds include supporting DLLs; single-file publishing is available below.

## Configure Microsoft login

1. In Microsoft Entra admin center, open **App registrations → New registration**. Choose accounts in your organizational directory only (single tenant).
2. Copy the **Application (client) ID** and **Directory (tenant) ID** from Overview.
3. Under **Authentication**, add a **Mobile and desktop applications** platform with broker redirect URI `ms-appx-web://Microsoft.AAD.BrokerPlugin/YOUR-CLIENT-ID` (replace the last part with your actual client ID). Also register `http://localhost` for system-browser fallback. Enable **Allow public client flows**. Do not create a client secret for this desktop app.
4. Under **API permissions**, add Microsoft Graph's **delegated** `User.Read`. Get consent according to your organization's policy; an administrator may need to approve it. Mail and Azure DevOps permissions are not needed yet.
5. Replace the placeholders in `src/WorkFlowWidget.Desktop/appsettings.json`:

   ```json
   {
     "Authentication": {
       "ClientId": "YOUR-APPLICATION-CLIENT-ID",
       "TenantId": "YOUR-DIRECTORY-TENANT-ID"
     }
   }
   ```

   Alternatively, put this JSON in `%LocalAppData%\WorkFlowWidget\appsettings.json` to keep your configuration outside Git and preserve it across updates. This file takes precedence over the copy beside the executable. IDs are public identifiers; never put passwords, access tokens or client secrets here. Restart after changes.

MSAL handles token acquisition through the Windows broker. The app shows **Signed in · verified** only after an authenticated Graph `/me` request returns a valid profile. Tokens are not logged or saved in appsettings. Startup attempts silent login when MSAL returns exactly one cached account; otherwise use interactive account selection. Broker account availability can vary between Windows sessions. Sign-out removes this app's MSAL account cache; it does not sign you out of Windows or Outlook.

## Build and run

Install .NET 8 SDK. On Windows:

```powershell
dotnet build WorkFlowWidget.sln
dotnet test tests/WorkFlowWidget.Core.Tests/WorkFlowWidget.Core.Tests.csproj
dotnet run --project src/WorkFlowWidget.Desktop
```

The widget is borderless, resizable and always on top. Drag its header to move it and use × to close. Placeholder settings show **Setup required**, rather than a fake login. Successful sign-in shows the verified account's name and email.

Linux can compile the Windows projects using `EnableWindowsTargeting` (already set). Portable tests run on Linux, but WPF and interactive Windows broker login need Windows. Initialize this cloud environment's shell first:

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

Distribute the publish folder, including `appsettings.json`. Native dependencies may extract at runtime. Configuration stays outside the EXE so users can supply their IDs. This self-contained build does not need a separate .NET installation.

## Windows acceptance checks

1. Launch with placeholder IDs: expect **Setup required**.
2. Configure the registration, restart and sign in: expect Microsoft's account flow, then your name, email and **Signed in · verified**.
3. Cancel login: expect signed-out state with retry available. Rejected Graph access or an unavailable connection must not show success.
4. Sign out: expect account details to disappear. Sign in again to check the flow still works.
5. Restart: if MSAL returns one cached account, silent login must still verify it through Graph.

These interactive checks require Windows and your registration. Compilation and mocked HTTP tests do not verify tenant consent, broker setup or live Microsoft sign-in.
