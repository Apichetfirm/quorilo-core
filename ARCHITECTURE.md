# Architecture and community direction

`desktop/src` contains provider adapters, normalization/reset presentation, local
preferences/history/notifications/models and the WPF desktop application.
`desktop/free-core/source-manifest.json` is the exact source/build dependency map.
`desktop/src/FreeCoreServices.cs` is compiled only for the Free edition: it gives
local capabilities and fails closed for account/cloud/owner service operations.
Legacy license/account/Companion implementations and Commerce.json are excluded.
`desktop/build_free_core.ps1` builds the Free edition without commercial protection
or private configuration. Existing local files are retained; no trial date resets.

The hosted identity/payment/deployment system is outside this distribution.
Provider sign-in is distinct from a Quorilo account. Nothing in a local feature
flag can authorize another account's data, hosted sync, a payment or a deployment.
`planner` is the independent, dependency-free Reset Planner component.

Community direction: improve supported provider freshness/error handling, usable
local notifications/history, accessibility, reproducible builds and documentation.
Other platforms require genuine qualification before availability claims. Optional
Cloud services require a separate design, authorization and service-cost decision;
this document is not an offer or promised delivery date. Donations remain voluntary.
