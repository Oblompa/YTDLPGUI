# Windows application end-to-end test

`Run-ApplicationE2E.ps1` drives the published WPF app through Windows UI Automation. It starts the app, installs or verifies yt-dlp, Deno (for YouTube's JavaScript challenges), and FFmpeg, searches for a track, queues the first result, downloads it as MP3, and verifies that the app reports success and the output file is non-empty.

Run from the repository root after publishing the portable app:

```powershell
dotnet publish .\src\YtDlpAudio.UI\YtDlpAudio.UI.csproj --configuration Portable --runtime win-x64 --self-contained true
.\tests\e2e\Run-ApplicationE2E.ps1 -Query "your search phrase" -ConfirmLicensedContent
```

Only use a query whose first result is content you are authorized to download. `-ConfirmLicensedContent` is an explicit acknowledgement of that requirement. The test saves the MP3 in a unique folder under the Windows temporary directory and prints its path. The application is closed when the test ends.

The test requires Windows PowerShell or PowerShell on Windows, an interactive desktop session, network access, and permission to install the app-managed dependencies. Use `-AppPath` to test a published executable in another location. Optional timeout parameters can be increased for slower connections.
