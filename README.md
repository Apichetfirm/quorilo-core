# Quorilo Free Core

Understand your AI usage. Stay in control.

Quorilo Monitor Free Core is a local-first Windows application. Local monitoring,
settings, notifications, history and supported separate provider profiles do not
require a Quorilo account, subscription, donation, trial timer or Stripe connection.
Your AI provider may still require its own account, authorization or paid service.
No software can create provider quota or guarantee that a service exposes it.

This source candidate contains the actual desktop application and selected local
Core, plus the existing offline Reset Planner. It has no private monorepo history,
production infrastructure, payment backend, deployment credentials or customer data.
A customer installer and other-platform availability are not implied by this source.
Actual Free Windows launch/provider/installer acceptance is a separate release gate.

## Build on Windows

Use an isolated licensed Windows x64 environment with Windows PowerShell5.1 or
PowerShell7, .NET Framework4.8/WPF build tools and normal Windows SDK support.
Run from this clean source checkout:

```powershell
pwsh -File desktop/build_free_core.ps1 -OutputDirectory "$env:TEMP/Quorilo-Free-Core"
```

Windows PowerShell may be used instead of pwsh. The build reads only the explicit
`desktop/free-core/source-manifest.json` inputs. No private Commerce.json, account
signing key, obfuscator, Stripe account, Git credential or package feed is needed.
Gemini Apps requires the separately installed Microsoft Edge WebView2 Runtime.
The included SDKDLL/loader and legal files match the pinned official package in
DEPENDENCY-PROVENANCE.json; runtime installation is not performed silently.

Build output is a local candidate, not an approved signed installer. Keep legal
notices with runtime files. Do not install over personal data merely to test.
Public customer distribution requires independent exact-package integrity/security,
platform smoke and an acceptable signing/channel decision. See desktop/free-core.

## Offline planner

Node22.13+ is required. This component has no third-party dependencies:

```sh
cd planner
npm ci --offline --ignore-scripts
npm test
npm run build
npm run example
```

It plans from user input; sample values are not live provider readings.

## Boundaries and limitations

Provider implementations cover supported Codex sources, Claude Code statusLine and
Gemini Apps/CLI channels where available; each has different windows, freshness and
error states. Inspect observation times. Unsupported or stale data is not fresh quota.
Free source does not imply that every provider/channel is runtime-qualified.
Free Gemini CLI reads only an existing, unexpired session owned by the provider's
CLI; it never acquires or refreshes OAuth credentials. Reconnect through that CLI
when its session expires. Gemini Apps uses its separate normal WebView2 session.
Optional experimental numerical forecasts remain explicit experiments/default-off.
Auto Resume, remote approvals and automated power actions are excluded.

Optional accounts and hosted synchronization require separate authentication and
resource authorization. Free local access grants no hosted rights. Quorilo Cloud
is a future candidate, not for sale. Existing financial records, subscription/refund
obligations and legacy account rights are not cancelled or reset by using Free.

## Support and contribute

Donations are voluntary and never unlock local monitoring. No donation provider,
payment address, QR code or fundraising total is configured in this candidate.
Contributions, reproducible issues and documentation improvements are welcome.
Use CONTRIBUTING.md and SECURITY.md; do not submit private profiles, cookies or keys.
See ARCHITECTURE.md for separation and CHANGELOG.md for candidate release notes.

The proposed license is MIT for this selected distribution, subject to the
publisher's final approval before publication. Third-party SDK/font terms remain
in their corresponding notices; provider names do not imply affiliation.
