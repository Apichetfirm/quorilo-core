# Contributing

Contributions to the selected Quorilo Free Core desktop source are licensed under
[AGPL-3.0-only](LICENSE). The offline Reset Planner has its own
[contribution terms](planner/CONTRIBUTING.md).

Open a focused issue describing the supported platform/channel, expected behavior,
minimal steps and redacted observations. Discuss material changes before broad
refactoring. Keep a change in one focused pull request with relevant checks.
Run the documented clean build and focused tests once for changed behavior.
Preserve provider authorization, local-account isolation, safe paths and signature
checks. Never weaken security tests, upload private profiles/secrets, or treat
sample quota as live data. Do not reuse a frozen TEST installer as a Free release.
Hosted integrations and donated funds are not controlled by local contributors.
