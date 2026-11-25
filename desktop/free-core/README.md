# Free Core source build

This is an explicit local Free Core edition, separate from historical Account,
Trial, private QA and Owner builds. Local features do not require Quorilo login,
expiry checks or Stripe. Connecting an AI provider still requires that provider's
own supported account and locally installed tools.

On Windows x64 with .NET Framework 4.8/WPF and Windows PowerShell, run:

```powershell
powershell -NoProfile -File .\build_free_core.ps1
```

Open `build-free-core/Unified/CreditMonitor.exe`. `build_unified.ps1 -FreeCore`
is the private monorepo convenience entry. The clean export needs only the files
listed in `source-manifest.json`; real Account/QA/Companion clients and commerce
configuration are excluded. No account, private key, NuGet download, obfuscator,
installer or signing operation is required. Provider logos and application icon
files are optional and are omitted from this build. Provider names remain text.

Dependencies: Windows built-in `winsqlite3.dll` for Insights; the included Noto
Sans Thai fonts/OFL; the included WebView2 SDK DLL/loader and notices. Gemini Apps
also needs Microsoft's WebView2 Runtime. Codex/Claude require their user-installed
CLI. Free Gemini CLI uses only an already valid session from its own cache; it
never bundles an OAuth client secret, refreshes/acquires tokens or rewrites that
cache. When the session expires, reconnect with the provider CLI. This limited
path is not claimed as accepted live-provider integration. Gemini Apps retains
its separate WebView2 path. The build compiles WPF views and its HLSL shader using Windows tools.

Settings, reader profiles and Insights keep existing storage paths. Account and
Owner vaults are neither read nor rewritten. Free quota history and optional API
credentials use a separate local `free-core` scope; historical scoped files remain
unchanged. Multi-account support covers the already implemented isolated profiles;
it does not promise provider APIs or quotas that are unavailable to that account.

Cloud pairing/sync and account services stay unavailable in this Free source build.
Free rights grant no owner tools, server permissions, remote approval or dispatch.
The verified update/signature checking code remains intact; this source build does
not configure or automatically download/install updates.

The selected Quorilo Free Core desktop source uses `AGPL-3.0-only`. See
`FREE-CORE-NOTICE.txt` and the root `LICENSE` in the clean source export.
Compilation and fixture checks are separate from Windows and provider acceptance.
