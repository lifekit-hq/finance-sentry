# Changelog

All notable changes to Finance Sentry are documented here. The format follows
[Conventional Commits](https://www.conventionalcommits.org/) and versions follow
[Semantic Versioning](https://semver.org/). Entries from v0.12.0 onward are
generated automatically by [release-please](https://github.com/googleapis/release-please).

## [1.8.0](https://github.com/lifekit-hq/finance-sentry/compare/v1.7.0...v1.8.0) (2026-09-05)


### Features

* **044:** classify counterparty flows per direction, drop netting ([#547](https://github.com/lifekit-hq/finance-sentry/issues/547)) ([69b811d](https://github.com/lifekit-hq/finance-sentry/commit/69b811dc785a47732bd28c9ff360108d35b153af))
* **432:** US2 cash-sweep proposal + US3 one-tap acknowledgement ([#549](https://github.com/lifekit-hq/finance-sentry/issues/549)) ([0cefaf2](https://github.com/lifekit-hq/finance-sentry/commit/0cefaf2b48f9c611fc4537ae67e54dd7815175b3))
* **576:** flow-breakdown drill-down — click a tile to audit its transactions ([#577](https://github.com/lifekit-hq/finance-sentry/issues/577)) ([5ceacae](https://github.com/lifekit-hq/finance-sentry/commit/5ceacae026af7e9b53a2418d86e75072f59d591a))
* Merge origin/main into goal/issue-414 weekly performance brief ([#545](https://github.com/lifekit-hq/finance-sentry/issues/545)) ([28211c3](https://github.com/lifekit-hq/finance-sentry/commit/28211c3e5f4a0092691a02cf9db2bf4808471c73)), closes [#414](https://github.com/lifekit-hq/finance-sentry/issues/414)


### Bug Fixes

* **044:** align sentinel queries and thresholds to spec ([#548](https://github.com/lifekit-hq/finance-sentry/issues/548)) ([4de1810](https://github.com/lifekit-hq/finance-sentry/commit/4de1810594de147f33a99c60b00d90f5b79a6cac))
* **044:** count the mortgage as household spending and cover Liza's transliteration variants ([#575](https://github.com/lifekit-hq/finance-sentry/issues/575)) ([b9623ad](https://github.com/lifekit-hq/finance-sentry/commit/b9623adfc60cabbffca5bb7763d803a198df6ffb)), closes [#573](https://github.com/lifekit-hq/finance-sentry/issues/573)
* **044:** cover Latin-script family transfers and Revolut X in counterparty rules ([#572](https://github.com/lifekit-hq/finance-sentry/issues/572)) ([4d9aebd](https://github.com/lifekit-hq/finance-sentry/commit/4d9aebd12d1814556817a1640ffdb346db9d3542)), closes [#571](https://github.com/lifekit-hq/finance-sentry/issues/571)
* **044:** give M011/M012 data ops explicit column types so their SQL generates ([#566](https://github.com/lifekit-hq/finance-sentry/issues/566)) ([e9dd5ac](https://github.com/lifekit-hq/finance-sentry/commit/e9dd5ac6b78889ee7a260cc1428ee49813f4b57e)), closes [#565](https://github.com/lifekit-hq/finance-sentry/issues/565)
* **044:** make M011's FAMILY_SUPPORT category seed idempotent ([#568](https://github.com/lifekit-hq/finance-sentry/issues/568)) ([97320fc](https://github.com/lifekit-hq/finance-sentry/commit/97320fcf1f1e3c391bf91fc94e41e90b45ee1b6a)), closes [#567](https://github.com/lifekit-hq/finance-sentry/issues/567)
* **044:** reconcile BankSync snapshot with the model so M011/M012 apply ([#564](https://github.com/lifekit-hq/finance-sentry/issues/564)) ([63d8ddd](https://github.com/lifekit-hq/finance-sentry/commit/63d8dddbd2041ff6cb20cca4e4d1555e8748d405)), closes [#563](https://github.com/lifekit-hq/finance-sentry/issues/563)
* **406:** add postinstall shim to patch @lifekit-hq/ui@0.2.0 stacked… ([#542](https://github.com/lifekit-hq/finance-sentry/issues/542)) ([996057b](https://github.com/lifekit-hq/finance-sentry/commit/996057b535ae5a793a4148478a3b35209921f938))
* **421:** dossier no-data state and a stale tag that fired before first… ([#546](https://github.com/lifekit-hq/finance-sentry/issues/546)) ([6d3172d](https://github.com/lifekit-hq/finance-sentry/commit/6d3172d8f92ab4f471921d652ea951d513ed18d7))
* **alerts:** make the M003 acknowledgement migration discoverable by EF ([#555](https://github.com/lifekit-hq/finance-sentry/issues/555)) ([7f2c519](https://github.com/lifekit-hq/finance-sentry/commit/7f2c519dc90ec846912d3083338ab8cd8a629044))
* **ci:** close the gate holes that let unvalidated PRs merge ([#562](https://github.com/lifekit-hq/finance-sentry/issues/562)) ([57a691b](https://github.com/lifekit-hq/finance-sentry/commit/57a691b30222a1128d480dc284e9529044554072)), closes [#561](https://github.com/lifekit-hq/finance-sentry/issues/561)
* **research:** self-heal the TrendForce source off its stale seeded URL ([#552](https://github.com/lifekit-hq/finance-sentry/issues/552)) ([8beec79](https://github.com/lifekit-hq/finance-sentry/commit/8beec790d440f4a403f0e96e45d510c6ff8c0c20)), closes [#318](https://github.com/lifekit-hq/finance-sentry/issues/318)
* **specify:** untrack the per-checkout feature.json pointer ([#556](https://github.com/lifekit-hq/finance-sentry/issues/556)) ([59676a6](https://github.com/lifekit-hq/finance-sentry/commit/59676a601a0daeab2fda271952f376b3550ac444))


### Documentation

* **538:** repoint M004's rounding reference after the recognizer move ([#550](https://github.com/lifekit-hq/finance-sentry/issues/550)) ([718a399](https://github.com/lifekit-hq/finance-sentry/commit/718a399d49ae95c051011dd2a45723f3031a022b))
* **agents:** correct two stale sandbox frontend setup steps ([#551](https://github.com/lifekit-hq/finance-sentry/issues/551)) ([e9de3ed](https://github.com/lifekit-hq/finance-sentry/commit/e9de3ed82e601a99f2462772b8cb2ed52ba8f03d))
* squash PR titles must carry the branch's highest-ranked commit type ([#570](https://github.com/lifekit-hq/finance-sentry/issues/570)) ([f6e705d](https://github.com/lifekit-hq/finance-sentry/commit/f6e705da7eca0a0766485029ef14cd4f16615823))

## [1.7.0](https://github.com/lifekit-hq/finance-sentry/compare/v1.6.0...v1.7.0) (2026-08-31)


### Features

* **ci:** dependency updates, vuln gates, and SSH.NET CVE fix ([#510](https://github.com/lifekit-hq/finance-sentry/issues/510)) ([#514](https://github.com/lifekit-hq/finance-sentry/issues/514)) ([30c22a0](https://github.com/lifekit-hq/finance-sentry/commit/30c22a09921cc8cc5aa0930fc8921303d8925152))
* **dashboard:** one shared time range drives every widget, default 3M ([#533](https://github.com/lifekit-hq/finance-sentry/issues/533)) ([63f135d](https://github.com/lifekit-hq/finance-sentry/commit/63f135d40d864f61e9b5c1605075056be3b0e279))
* **deploy:** host-level uptime probe with Telegram alerting ([#511](https://github.com/lifekit-hq/finance-sentry/issues/511)) ([#512](https://github.com/lifekit-hq/finance-sentry/issues/512)) ([7bddc01](https://github.com/lifekit-hq/finance-sentry/commit/7bddc01ca00d7dc0816b8c060eaf05180a88ca50))
* **e2e:** live smoke suite against the deployed VPS stack ([#503](https://github.com/lifekit-hq/finance-sentry/issues/503)) ([#504](https://github.com/lifekit-hq/finance-sentry/issues/504)) ([9fcff5c](https://github.com/lifekit-hq/finance-sentry/commit/9fcff5c9ae5edc55b43d38fd7099c4dbd5194019))


### Bug Fixes

* **bank-sync:** actually publish AccountSyncCompletedEvent after a successful sync ([#536](https://github.com/lifekit-hq/finance-sentry/issues/536)) ([e0bcce9](https://github.com/lifekit-hq/finance-sentry/commit/e0bcce9c03bcc65b87083bca375c0da35f541f0b))
* **bank-sync:** auto-discover TrueLayer credit cards during scheduled sync ([#532](https://github.com/lifekit-hq/finance-sentry/issues/532)) ([f215523](https://github.com/lifekit-hq/finance-sentry/commit/f215523ed192cb1ad1d6037c8f2e4d6cc2f20ac6))
* **bank-sync:** credit-card accounting, pending outflow, live net-worth snapshots ([#531](https://github.com/lifekit-hq/finance-sentry/issues/531)) ([7c85cea](https://github.com/lifekit-hq/finance-sentry/commit/7c85cea24b64e171225684d0155f94f640ea1074))
* **bank-sync:** re-observe settled transactions and pair pending transfer legs ([#535](https://github.com/lifekit-hq/finance-sentry/issues/535)) ([0fd4b32](https://github.com/lifekit-hq/finance-sentry/commit/0fd4b32a03b55da61e0d7b01c89e8cec5b72a893))
* **ci:** backend coverage was never collected; run all suites + ratchet gate ([#509](https://github.com/lifekit-hq/finance-sentry/issues/509)) ([#521](https://github.com/lifekit-hq/finance-sentry/issues/521)) ([ce1ec0a](https://github.com/lifekit-hq/finance-sentry/commit/ce1ec0a0e863eb9ddf8b1468ff9c13d361d4a2d2))
* **core:** treat credit-card balances as liabilities in net-total aggregations ([#498](https://github.com/lifekit-hq/finance-sentry/issues/498)) ([#499](https://github.com/lifekit-hq/finance-sentry/issues/499)) ([99e8f15](https://github.com/lifekit-hq/finance-sentry/commit/99e8f1591206c634d19ab92fd90ca736c09b1cad))
* **dashboard:** chart complete months only, retire the Income page ([#537](https://github.com/lifekit-hq/finance-sentry/issues/537)) ([27d935f](https://github.com/lifekit-hq/finance-sentry/commit/27d935f82112a7d52cb4d2da75ae61ab6d9278f0))
* **deploy:** probe install no longer trips errexit on an empty crontab ([#511](https://github.com/lifekit-hq/finance-sentry/issues/511)) ([#513](https://github.com/lifekit-hq/finance-sentry/issues/513)) ([3b9a915](https://github.com/lifekit-hq/finance-sentry/commit/3b9a9151bb150c74c70526e59581bbe8028b95a2))
* **docker:** apk upgrade runtime stage to clear Trivy HIGH CVEs ([#530](https://github.com/lifekit-hq/finance-sentry/issues/530)) ([3101b98](https://github.com/lifekit-hq/finance-sentry/commit/3101b983a9d776964b1149cdfd37955ae3635141))
* **docker:** serialize the shared NuGet cache mount (sharing=locked) ([#529](https://github.com/lifekit-hq/finance-sentry/issues/529)) ([bbd13f9](https://github.com/lifekit-hq/finance-sentry/commit/bbd13f9f8e7aec45e440c50186c598a67fc7d123))
* **e2e:** match the dashboard aggregated mock with its new ?months param ([#534](https://github.com/lifekit-hq/finance-sentry/issues/534)) ([72adf79](https://github.com/lifekit-hq/finance-sentry/commit/72adf7937808125af162622f5af0bb6d59ea9be7))


### Refactoring

* **budgets:** decouple from BankSync via Core read port + module-boundary arch test ([#501](https://github.com/lifekit-hq/finance-sentry/issues/501)) ([1d50f98](https://github.com/lifekit-hq/finance-sentry/commit/1d50f9810111c1cda5e025fb89a1d22a876c54ed))


### Documentation

* devclaw work-item issue template (spec 024 — the issue is the contract) ([#490](https://github.com/lifekit-hq/finance-sentry/issues/490)) ([fad5771](https://github.com/lifekit-hq/finance-sentry/commit/fad5771b7345c2dc364af25b9b9f0428d1131f47))
* infrastructure-catalog rule — infra changes update lifekit-dashboard's infra.json ([#526](https://github.com/lifekit-hq/finance-sentry/issues/526)) ([35486a3](https://github.com/lifekit-hq/finance-sentry/commit/35486a378fa20a5fb5aa81595d11889aee5e43fa))
* **specs:** flip 021 + 039 Status to Implemented ([#497](https://github.com/lifekit-hq/finance-sentry/issues/497)) ([609a6be](https://github.com/lifekit-hq/finance-sentry/commit/609a6be34693af411d5089619db71f8e51681bb8))

## [1.6.0](https://github.com/lifekit-hq/finance-sentry/compare/v1.5.0...v1.6.0) (2026-08-28)


### Features

* **462:** dashboard drill-downs and Income page ([#476](https://github.com/lifekit-hq/finance-sentry/issues/476)) ([9f1d343](https://github.com/lifekit-hq/finance-sentry/commit/9f1d34335097cd51732c2ee4486df6e42151c3bf))
* **frontend:** consume @lifekit-hq/* from GitHub Packages — remove the in-repo dsdevq-common library ([#471](https://github.com/lifekit-hq/finance-sentry/issues/471)) ([d4fd182](https://github.com/lifekit-hq/finance-sentry/commit/d4fd182bc06e5a43470140db0dc6bf2463be9aa0)), closes [#469](https://github.com/lifekit-hq/finance-sentry/issues/469)
* **fx:** historical exchange rates from NBU and ECB ([#488](https://github.com/lifekit-hq/finance-sentry/issues/488)) ([654c671](https://github.com/lifekit-hq/finance-sentry/commit/654c671c519deeed2203de30925febe0fec1e9d0)), closes [#487](https://github.com/lifekit-hq/finance-sentry/issues/487)
* **subscriptions:** count installments in spend totals, annualized by remaining payments ([#486](https://github.com/lifekit-hq/finance-sentry/issues/486)) ([e8d8e39](https://github.com/lifekit-hq/finance-sentry/commit/e8d8e393bb9dc6f5db0c91869eb2e3c392032337)), closes [#485](https://github.com/lifekit-hq/finance-sentry/issues/485)
* **subscriptions:** show what exchange-rate movement does to installment cost ([#489](https://github.com/lifekit-hq/finance-sentry/issues/489)) ([5662f31](https://github.com/lifekit-hq/finance-sentry/commit/5662f31bc470c1ea4d7d6d5a3f6d0b9bbfd56dc1)), closes [#487](https://github.com/lifekit-hq/finance-sentry/issues/487)


### Bug Fixes

* **dashboard:** exclude the in-progress month from the savings-rate chart ([#458](https://github.com/lifekit-hq/finance-sentry/issues/458)) ([7c00716](https://github.com/lifekit-hq/finance-sentry/commit/7c00716438fbfdf10c9f5e1ad97247c3ad3d2da0)), closes [#457](https://github.com/lifekit-hq/finance-sentry/issues/457)
* **e2e:** drop the sandbox libXfixes hack from playwright.config.ts ([#478](https://github.com/lifekit-hq/finance-sentry/issues/478)) ([8956345](https://github.com/lifekit-hq/finance-sentry/commit/895634553951be1616715d805d32ca106e3c97f8))
* **frontend:** repair [@dsdevq-common](https://github.com/dsdevq-common) imports left by the stale fs[#476](https://github.com/lifekit-hq/finance-sentry/issues/476) goal branch ([#477](https://github.com/lifekit-hq/finance-sentry/issues/477)) ([ae8f6cd](https://github.com/lifekit-hq/finance-sentry/commit/ae8f6cddce1f5e64ed5531e2fd36e4b9f5d2965d))
* **subscriptions:** per-plan installment identity, amount clustering, detection on sync ([#483](https://github.com/lifekit-hq/finance-sentry/issues/483)) ([ee685e3](https://github.com/lifekit-hq/finance-sentry/commit/ee685e337870c0745329db837d8b8534dc8086d6)), closes [#482](https://github.com/lifekit-hq/finance-sentry/issues/482)
* **thesis-monitor:** anchor price_drawdown to entry price; clear orphaned breaks on trigger removal ([#475](https://github.com/lifekit-hq/finance-sentry/issues/475)) ([939d7be](https://github.com/lifekit-hq/finance-sentry/commit/939d7becad6075c27bc68e829e072fc50ba7c60d)), closes [#474](https://github.com/lifekit-hq/finance-sentry/issues/474)

## [1.5.0](https://github.com/lifekit-hq/finance-sentry/compare/v1.4.0...v1.5.0) (2026-08-24)


### Features

* **liquidity:** 30-day cash-flow projection + shortfall sentinel (041) ([185efed](https://github.com/lifekit-hq/finance-sentry/commit/185efedb67284ba12db8a927c50780dffa86faf7))

## [1.4.0](https://github.com/lifekit-hq/finance-sentry/compare/v1.3.0...v1.4.0) (2026-08-22)


### Features

* Implement finance-sentry [#412](https://github.com/lifekit-hq/finance-sentry/issues/412) — book-vs-benchmark TWR scoreboard… ([#451](https://github.com/lifekit-hq/finance-sentry/issues/451)) ([6e51a78](https://github.com/lifekit-hq/finance-sentry/commit/6e51a78ca22f4937688edad0d8dd17aac329d408))

## [1.3.0](https://github.com/lifekit-hq/finance-sentry/compare/v1.2.0...v1.3.0) (2026-08-19)


### Features

* Implement lifekit-hq/finance-sentry issue [#411](https://github.com/lifekit-hq/finance-sentry/issues/411): a canonical… ([#447](https://github.com/lifekit-hq/finance-sentry/issues/447)) ([2cdc8b6](https://github.com/lifekit-hq/finance-sentry/commit/2cdc8b69e534353edabe85c83b860624809724d2))

## [1.2.0](https://github.com/lifekit-hq/finance-sentry/compare/v1.1.0...v1.2.0) (2026-08-12)


### Features

* **ci:** weekly automated releases — merge the Release Please PR on a Monday cron ([#408](https://github.com/lifekit-hq/finance-sentry/issues/408)) ([47114f6](https://github.com/lifekit-hq/finance-sentry/commit/47114f6631968dd4e89420e2b31b7cc2ff8f14d2))


### Bug Fixes

* **monobank:** window incremental statement fetches to 31 days ([#410](https://github.com/lifekit-hq/finance-sentry/issues/410)) ([7e51b72](https://github.com/lifekit-hq/finance-sentry/commit/7e51b728db4928b0b20e800fb2bfb10e3c6a7cf3))


### Documentation

* **032:** spec — agent-as-code (Ledger definition lives in the repo) ([#441](https://github.com/lifekit-hq/finance-sentry/issues/441)) ([a3fabb9](https://github.com/lifekit-hq/finance-sentry/commit/a3fabb9229cec4c2aa75cead46925b16aa67f320))
* adopt naming & planning conventions (branches, issues, milestones, project fields) ([#427](https://github.com/lifekit-hq/finance-sentry/issues/427)) ([078991f](https://github.com/lifekit-hq/finance-sentry/commit/078991f93dbb88bff1a36aceeba86fa3aad7acf1))
* CLAUDE.md diet — 424→101 lines; split into docs/claude/, stop speckit appends ([#436](https://github.com/lifekit-hq/finance-sentry/issues/436)) ([80ceb7d](https://github.com/lifekit-hq/finance-sentry/commit/80ceb7d5dd0914e62290edcf131121496356ee90))
* land draft specs 034 (research-rag) + 038 (earnings-stance-engine) on main ([#442](https://github.com/lifekit-hq/finance-sentry/issues/442)) ([ccddc74](https://github.com/lifekit-hq/finance-sentry/commit/ccddc7449db97e3f1222585ee8719fd9e90710bc))

## [1.1.0](https://github.com/lifekit-hq/finance-sentry/compare/v1.0.0...v1.1.0) (2026-08-12)


### Features

* **021:** market regime scanner — VIX + FRED curve, orthogonal axes, get_market_regime(), 019 coupling ([#386](https://github.com/lifekit-hq/finance-sentry/issues/386)) ([e795895](https://github.com/lifekit-hq/finance-sentry/commit/e795895560439ba85c95074499de4104d045be04))
* **024:** data retention & verified off-host backups ([#367](https://github.com/lifekit-hq/finance-sentry/issues/367)) ([84922b1](https://github.com/lifekit-hq/finance-sentry/commit/84922b176ea2b0a06518cc9189f9a76a2492873c))
* **025:** edge gateway — YARP reverse proxy (additive, no cutover) ([#383](https://github.com/lifekit-hq/finance-sentry/issues/383)) ([64b2dee](https://github.com/lifekit-hq/finance-sentry/commit/64b2dee1a9b55d090ae6a7515843ab849b51e4c1))
* **039:** IPS/Risk boundary cleanup — single home per policy concept ([#382](https://github.com/lifekit-hq/finance-sentry/issues/382)) ([190fb31](https://github.com/lifekit-hq/finance-sentry/commit/190fb3144fb29ea9416bd2685210c781c7979732))
* **040:** adopt Deep Chat for the Ledger chat UI (cmn-chat) ([#392](https://github.com/lifekit-hq/finance-sentry/issues/392)) ([f724200](https://github.com/lifekit-hq/finance-sentry/commit/f7242006c4c33e2fbf43e06f67ab4277c4746ead))
* **040:** in-app finance agent — browser Ledger (US2/US3) over MCP tool surface ([#389](https://github.com/lifekit-hq/finance-sentry/issues/389)) ([dc97790](https://github.com/lifekit-hq/finance-sentry/commit/dc977901ed09ec939f6198d1f8a11dc0a406c89e))
* **040:** OpenClaw-backed Ledger brain + floating chat widget ([#390](https://github.com/lifekit-hq/finance-sentry/issues/390)) ([2590eb6](https://github.com/lifekit-hq/finance-sentry/commit/2590eb64f6f5e4e31c216a7341d5686df885a4fa))
* **040:** US1 — Ledger persona as code (core + OpenClaw/browser adapters) ([#388](https://github.com/lifekit-hq/finance-sentry/issues/388)) ([889a06a](https://github.com/lifekit-hq/finance-sentry/commit/889a06a01307ad83b825c4040950b62112a3b88b))
* **accounts:** merge Investments into Accounts as tabs; drop duplicate donut ([#379](https://github.com/lifekit-hq/finance-sentry/issues/379)) ([915c34d](https://github.com/lifekit-hq/finance-sentry/commit/915c34d6f5e8e363f6dd3054e0da24a315910239))
* **categorization:** seed common IE merchants for the TrueLayer no-MCC fallback ([#365](https://github.com/lifekit-hq/finance-sentry/issues/365)) ([d8173a5](https://github.com/lifekit-hq/finance-sentry/commit/d8173a567952ee86b3f67c17c5ec2d71999df402))
* **dashboard:** restore income analytics now that income is classified ([#376](https://github.com/lifekit-hq/finance-sentry/issues/376)) ([fe17121](https://github.com/lifekit-hq/finance-sentry/commit/fe17121a27d94fc731276a84665b585198d95c4e))
* **dashboard:** time-based analytics — stacked net worth, cash-flow & savings bars ([#371](https://github.com/lifekit-hq/finance-sentry/issues/371)) ([03c0b46](https://github.com/lifekit-hq/finance-sentry/commit/03c0b4642ed7c1d47c9f1d0a5d44116af2ddb9c4))
* **ia:** split Accounts vs Investments; drop duplicated allocation view ([#370](https://github.com/lifekit-hq/finance-sentry/issues/370)) ([cd23b77](https://github.com/lifekit-hq/finance-sentry/commit/cd23b77fc2798f9e38f2662d33e8981171522f05))
* **ibkr:** surface uninvested cash from the IBKR ledger ([#375](https://github.com/lifekit-hq/finance-sentry/issues/375)) ([d278bf0](https://github.com/lifekit-hq/finance-sentry/commit/d278bf08438f64cea15e69944e6a01e72b44fd87))
* **investments:** apply net-worth hero to the Investments header ([#378](https://github.com/lifekit-hq/finance-sentry/issues/378)) ([61a8e03](https://github.com/lifekit-hq/finance-sentry/commit/61a8e037945b0b9f382513ff3b3751f0d52ec3f9))
* **monobank:** group currency sub-accounts under their physical card ([#366](https://github.com/lifekit-hq/finance-sentry/issues/366)) ([96073a7](https://github.com/lifekit-hq/finance-sentry/commit/96073a748a5ffef2162514658027e10d19d1e198))
* **ui:** internal scroll + sticky header for data pages ([#380](https://github.com/lifekit-hq/finance-sentry/issues/380)) ([2b99047](https://github.com/lifekit-hq/finance-sentry/commit/2b9904705364fe3dc423e6ed457aa30294e89368))
* **ui:** subtle theme-aware scrollbars ([#381](https://github.com/lifekit-hq/finance-sentry/issues/381)) ([0f98962](https://github.com/lifekit-hq/finance-sentry/commit/0f9896234d89643eba7c94820ac6b526c49a9c62))
* **wealth:** surface stale bank connections; stop them faking net-worth moves ([#363](https://github.com/lifekit-hq/finance-sentry/issues/363)) ([b516653](https://github.com/lifekit-hq/finance-sentry/commit/b5166533fa73179fd05c13317f81968972a722ea))


### Bug Fixes

* **024:** retention module governs its own run-record tables ([#368](https://github.com/lifekit-hq/finance-sentry/issues/368)) ([8a3edb6](https://github.com/lifekit-hq/finance-sentry/commit/8a3edb6799f6ed75650bc25447ae56f3974c7d3e))
* **025:** allow gateway host in dev-server so SPA routes through the gateway ([#384](https://github.com/lifekit-hq/finance-sentry/issues/384)) ([a0ac3b9](https://github.com/lifekit-hq/finance-sentry/commit/a0ac3b9f2417e2e1036b3d45778b39f81ce1d743))
* **039:** register cross-module ports in the MCP host via shared FinanceSentry.Integration lib ([#387](https://github.com/lifekit-hq/finance-sentry/issues/387)) ([a260d0b](https://github.com/lifekit-hq/finance-sentry/commit/a260d0b8cac8066db79916383ffeb78f419779bf))
* **040:** make &lt;deep-chat&gt; fill its container (input at bottom) ([#394](https://github.com/lifekit-hq/finance-sentry/issues/394)) ([02786a7](https://github.com/lifekit-hq/finance-sentry/commit/02786a74be24f979dddd36ff602aa9be0ee35265))
* **040:** render &lt;deep-chat&gt; only after its bundle defines the element ([#393](https://github.com/lifekit-hq/finance-sentry/issues/393)) ([c7eb0af](https://github.com/lifekit-hq/finance-sentry/commit/c7eb0af14a5f32bbe0f79c903f23aafab8d609bf))
* **040:** suppress OpenClaw NO_REPLY sentinel in browser Ledger chat ([#391](https://github.com/lifekit-hq/finance-sentry/issues/391)) ([4fc13f1](https://github.com/lifekit-hq/finance-sentry/commit/4fc13f10eeb5fbadaeaa9d841c7b6410c79d5d61))
* **accounts:** USD-correct monthly outflow; rebuild net-worth hero ([#377](https://github.com/lifekit-hq/finance-sentry/issues/377)) ([713734d](https://github.com/lifekit-hq/finance-sentry/commit/713734dc4515f950707e69300a0067c55228a33e))
* **bank-sync,research:** map Monobank 403 to token-invalid; strip MarketBeat promo from firm names (M011 data repair) ([#361](https://github.com/lifekit-hq/finance-sentry/issues/361)) ([e132154](https://github.com/lifekit-hq/finance-sentry/commit/e1321546973fcc989a5ffaaf6750ba6a0bcb5741))
* **bank-sync:** detect cross-currency internal transfers ([#403](https://github.com/lifekit-hq/finance-sentry/issues/403)) ([950cedc](https://github.com/lifekit-hq/finance-sentry/commit/950cedcb9a3ae4bd972ff2ac257ac27143377b44))
* **bank-sync:** per-account transaction-sync watermark — white card never synced ([#405](https://github.com/lifekit-hq/finance-sentry/issues/405)) ([ec8228b](https://github.com/lifekit-hq/finance-sentry/commit/ec8228b6f3ec36920edd050d223e304974382e38))
* **categorization:** classify external "Payment from" credits as income, not transfer ([#374](https://github.com/lifekit-hq/finance-sentry/issues/374)) ([68e5e80](https://github.com/lifekit-hq/finance-sentry/commit/68e5e80c9937dde79f8c74c5ba1116b7e3d9ca20))
* **connect:** reset error state on navigation, preserve form on failed submit, fix error-code resolution ([#362](https://github.com/lifekit-hq/finance-sentry/issues/362)) ([d143076](https://github.com/lifekit-hq/finance-sentry/commit/d143076670da6eab0df021adde74c8d62221fcf9))
* **currency:** convert to USD before every cross-currency aggregation ([#357](https://github.com/lifekit-hq/finance-sentry/issues/357)) ([f1bb92b](https://github.com/lifekit-hq/finance-sentry/commit/f1bb92b2b5d53655582ec42784dba35424434b5e))
* dashboard KPI layout-shift on range change; harden deploy against api race ([#373](https://github.com/lifekit-hq/finance-sentry/issues/373)) ([662cc10](https://github.com/lifekit-hq/finance-sentry/commit/662cc1021bdf0e7d72e79fb6b41f3887ba9de8d7))
* **dashboard:** month chart labels read as dates — 'Jun 26' → "Jun '26" ([#398](https://github.com/lifekit-hq/finance-sentry/issues/398)) ([315b517](https://github.com/lifekit-hq/finance-sentry/commit/315b5174a7a08c1608ec8b57c89f2285dead2404))
* **dashboard:** window Top Spending Categories to 6 months, not all-time ([#404](https://github.com/lifekit-hq/finance-sentry/issues/404)) ([d08084f](https://github.com/lifekit-hq/finance-sentry/commit/d08084fbdc00734ffb20ea32141fe9613dc6ec37))
* **holdings:** show only provider-backed P&L; fix Monobank card indent ([#369](https://github.com/lifekit-hq/finance-sentry/issues/369)) ([6385f83](https://github.com/lifekit-hq/finance-sentry/commit/6385f833e610f61a28d55f64de2fae9f70d2d2fc))
* **mcp:** unify cash definition between get_portfolio_snapshot and allocation drift ([#397](https://github.com/lifekit-hq/finance-sentry/issues/397)) ([26c947c](https://github.com/lifekit-hq/finance-sentry/commit/26c947cb9ae31430e66e85c90598a474e7eb81a3))
* **subscriptions:** update list and summary in place on dismiss/restore ([#359](https://github.com/lifekit-hq/finance-sentry/issues/359)) ([5b992df](https://github.com/lifekit-hq/finance-sentry/commit/5b992df1d0fd918166588c4efbfd211be163eead))
* **tests:** add AmountUsd arg to GlobalTransactionDto test builders ([38ab03f](https://github.com/lifekit-hq/finance-sentry/commit/38ab03f6953b08edc3ef310411624614129fb59e))
* **ui:** make cmn-dialog viewport-safe below 512px ([#360](https://github.com/lifekit-hq/finance-sentry/issues/360)) ([443a188](https://github.com/lifekit-hq/finance-sentry/commit/443a18826db2aa0e875f56a3cd4d34fd0eef4d73))
* **ui:** premium theme + honest dashboard; drop charts built on absent income ([#372](https://github.com/lifekit-hq/finance-sentry/issues/372)) ([93d02da](https://github.com/lifekit-hq/finance-sentry/commit/93d02dacf9efccf30ae1608e03d176aab6805667))


### Documentation

* **011:** sign off T067 — 4 QA-sweep bugs fixed and re-verified ([#364](https://github.com/lifekit-hq/finance-sentry/issues/364)) ([56cd192](https://github.com/lifekit-hq/finance-sentry/commit/56cd192dd28cf4fdb4568ce9830085c5ed5cb694))
* **025:** record fast-fail as known limitation, deferred to 027-k8s ([#385](https://github.com/lifekit-hq/finance-sentry/issues/385)) ([48ae4e5](https://github.com/lifekit-hq/finance-sentry/commit/48ae4e5d062f5fa9ea214783284878db93e8b014))
* **040:** T028 QA passed against prod — feature 040 complete ([#396](https://github.com/lifekit-hq/finance-sentry/issues/396)) ([28a9fca](https://github.com/lifekit-hq/finance-sentry/commit/28a9fca6e7f2c43a381939cf82025646e390009a))
* drop stale 'known broken' note — those specs were deleted in April ([#402](https://github.com/lifekit-hq/finance-sentry/issues/402)) ([de1eed1](https://github.com/lifekit-hq/finance-sentry/commit/de1eed1c08f091b7a5dbfd1ee4878eb32ed6b85e))
* **qa:** record 2026-08-06 QA sweep results for 008/014/030/036, annotate 011 T067 ([#358](https://github.com/lifekit-hq/finance-sentry/issues/358)) ([09afee3](https://github.com/lifekit-hq/finance-sentry/commit/09afee32e1108e7327f3d1720597d36351f61e2e))

## 1.0.0 (2026-08-06)

First stable release. Finance Sentry is a personal finance aggregation platform —
an ASP.NET Core (.NET 10) modular monolith + Angular 21 SPA — that consolidates
banking, brokerage, and crypto accounts into one net-worth, spending, and research
view. All golden-path flows (login, accounts, dashboard, transactions, holdings,
budgets, subscriptions) are verified end-to-end against live data.

### Features

- **Research suite (companion data layer)**: analyst actions, valuation snapshots,
  thesis-source news (030); structured analyst data via Finnhub recommendation
  trends, retiring the Yahoo scraper (037); retrieval + RAG context layer with
  `search_research_corpus` / `get_research_context` MCP tools (036); opportunity
  scan job with machine nomination from market structure (019); MCP tool-surface
  refinement (035); guarded read-only analytics query tool (033)
- **Companion notifications**: notification modes + event-driven push (031)
- **Observability stack**: OpenTelemetry, Loki, Prometheus, Grafana dashboards,
  Hangfire-on-Postgres, job-failure Telegram alerts (023)
- **Brokerage**: IBKR holdings via tier-1 Portal session + OAuth
- **FX**: live exchange rates with daily refresh and offline fallback
- **Installments**: dedicated section with smarter detection + management
- **UI library**: new `@dsdevq-common/ui` composites — `cmn-page-header`,
  `cmn-page-container`, `cmn-tab-group`, `cmn-empty-state`, `cmn-disclosure-row`,
  `cmn-editable-field`, `cmn-list-item-row`, `cmn-select`; provider logos,
  per-asset holding icons, app version in the sidebar footer

### Bug Fixes

- **Categorization**: classify Monobank savings-jar top-ups as transfers, not
  government spend; categorize TrueLayer transactions from description text;
  case-insensitive transfer bucketing
- **TrueLayer**: self-healing reconnect, stale-sync reaper, dedup crash fix,
  rotated-refresh-token persistence, inline history-fetch + pre-expiry reminder
- **Reliability**: stop silent cron-failure loops (TrueLayer, TrendForce, Yahoo);
  treat provider 429s as transient (no false SyncFailure alerts)
- **Holdings/subscriptions**: drop zero-quantity and sold-out positions; exclude
  installments and canonicalize noisy merchant brands; USD-normalized monthly cost
- **Frontend**: green lint-build-test enforced pre-commit; alerts page no longer
  crashes on unmapped alert types; chart tooltips + URL-persisted dashboard range
- **Portfolio/quotes**: quote data-quality fixes; missing migration Designer for
  `M007_QuoteSessionMetadata`

## 0.11.0 (2026-07-09)

Baseline release — consolidation of everything built to date under a single
application version (previously the frontend and backend were versioned
independently, last tagged `frontend-v0.4.0` / backend `0.11.0`).

### Highlights

- Multi-provider aggregation: Plaid, Monobank, TrueLayer, Binance, IBKR
- Auth with email/password + Google OAuth, JWT + httpOnly refresh cookie
- Dashboard, transactions, budgets, alerts, subscription detection
- Net worth history snapshots
- Research suite: investment theses, thesis monitor, thesis track record, market structure radar, opportunity scanner, risk rules
- Read-only MCP server (`FinanceSentry.Mcp`) exposing financial data to MCP clients
- Full Docker stack + CI + automated VPS deployment
