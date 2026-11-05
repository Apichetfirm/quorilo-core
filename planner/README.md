# Quorilo Reset Planner — private release candidate

**MIT-licensed private source candidate; not publicly published. Independent
audit and final owner authorization of the exact source commit are pending.**

The [MIT license](LICENSE) applies to this selected component only. It does not
license the rest of Quorilo or grant rights to its trademarks, accounts or assets.

A small offline quota-planning component: enter a remaining amount and a future
reset time, and get a same-unit daily budget. It reuses Quorilo Reset's pure
planning logic. It does not read provider accounts or local agent logs, validate
actual availability, send notifications, perform automation, or convert tokens
between providers. A self-entered balance is not a fresh quota observation.

## Run from a clean checkout

Requires Node.js24 or later; no paid account, private services, keys or packages.

```sh
npm ci --ignore-scripts --no-audit
npm test
npm run build
npm run example
```

The example returns a budget of10 credits/day from50 credits over5 days, with
`availabilityVerified:false`. No package install scripts or external dependencies.
`npm run build` verifies executable syntax; Node's native TypeScript stripping
runs the copied pure module. No bundled executable or transpiled build is claimed.

For your own plan, pipe a JSON object into `node cli.mjs`. Fields: `remaining`,
`unit` (credits/tokens/requests/percent), `resetAt` (explicit UTC endingZ), and
optional `daily`. Input is bounded to16KiB; malformed/past/ambiguous timestamps,
invalid balances and unknown fields fail. `node cli.mjs --help` prints usage.
Use `--now=2026-11-02T00:00:00Z` for a reproducible example; otherwise the machine
clock is used. This is arithmetic, not a financial estimate or future quota claim.

Import `validateQuotaPlan`/`calculateDailyBudget` from `src/quota-plan.ts` on Node24
for the original typed library. CLI validation is stricter: it requires known
units/fields and explicit canonical UTC. The library's existing API is preserved.

## Platforms and scope

Source targets Node24 on Linux/macOS/Windows. Actual tested platforms are recorded
in the owner-side acceptance receipt; support is not inferred from target syntax.
No GUI, provider adapter, account/Trial/Pro rights, Stripe, signing, hosted runtime,
private operations, fonts, logos or other assets are included. This first component
is not the complete Quorilo Monitor Community Edition or a free hosted entitlement.
See [privacy](PRIVACY.md), [security](SECURITY.md), [support](SUPPORT.md),
[contributing](CONTRIBUTING.md) and [release notes](CHANGELOG.md).
