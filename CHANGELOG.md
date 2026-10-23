# Changelog

All notable changes to Finance Sentry are documented here. The format follows
[Conventional Commits](https://www.conventionalcommits.org/) and versions follow
[Semantic Versioning](https://semver.org/). Entries from v0.12.0 onward are
generated automatically by [release-please](https://github.com/googleapis/release-please).

## [1.17.0](https://github.com/lifekit-hq/finance-sentry/compare/v1.16.0...v1.17.0) (2026-10-23)


### Features

* **bank-sync:** add coverage sentinel for accounts whose spend goes quiet ([#1244](https://github.com/lifekit-hq/finance-sentry/issues/1244)) ([15a5631](https://github.com/lifekit-hq/finance-sentry/commit/15a56313d7a509fb574e9d51cd8004233885c802))
* **bank-sync:** classify AIB ATM cash lodgements as income ([#1246](https://github.com/lifekit-hq/finance-sentry/issues/1246)) ([62f5a2e](https://github.com/lifekit-hq/finance-sentry/commit/62f5a2e5f68615d28656adbd0a6043ee4a88395b))
* **bank-sync:** rework Home around why net worth moved and where it is ([#1191](https://github.com/lifekit-hq/finance-sentry/issues/1191)) ([5cd99db](https://github.com/lifekit-hq/finance-sentry/commit/5cd99dbe797190186aafaa9d11c038d836149c90))
* **bank-sync:** show the net-worth bridge waterfall on Home ([#1174](https://github.com/lifekit-hq/finance-sentry/issues/1174)) ([344fc59](https://github.com/lifekit-hq/finance-sentry/commit/344fc59a0c567fc5be873e63d2fcc04a25b8f80a))
* **brokerage-sync:** move IBKR Flex from shadow to active connection-health alerting ([#1167](https://github.com/lifekit-hq/finance-sentry/issues/1167)) ([73346bb](https://github.com/lifekit-hq/finance-sentry/commit/73346bba356ef17fc05cc664d43a02373d446628))
* **companion:** add a ledger correction log with MCP record and list tools ([#1176](https://github.com/lifekit-hq/finance-sentry/issues/1176)) ([2730678](https://github.com/lifekit-hq/finance-sentry/commit/27306780ec1f5a466e7618aa479c627fc8489548))
* **frontend:** adopt [@lifekit-hq](https://github.com/lifekit-hq) 4.0.0 and swap Home's donut for cmn-allocation-bar ([#1193](https://github.com/lifekit-hq/finance-sentry/issues/1193)) ([f2ae82c](https://github.com/lifekit-hq/finance-sentry/commit/f2ae82cde860dfce6a67d6c8f2f3d19447487bbc))
* **frontend:** retire the Investments tab; Accounts carries every position ([#1166](https://github.com/lifekit-hq/finance-sentry/issues/1166)) ([c054715](https://github.com/lifekit-hq/finance-sentry/commit/c0547150e1e484ebbaee8c4f13dd6c65d3541b3f))
* **holdings:** add Home Investments card, drop hero invested-change line ([#1165](https://github.com/lifekit-hq/finance-sentry/issues/1165)) ([0e3ae09](https://github.com/lifekit-hq/finance-sentry/commit/0e3ae09b4a4f5fc1c0a25b8a7ce102a3225efe90))
* **holdings:** fold contributed-vs-growth and growth-of-10,000 into one performance card ([#1236](https://github.com/lifekit-hq/finance-sentry/issues/1236)) ([6473d73](https://github.com/lifekit-hq/finance-sentry/commit/6473d7396a6746a3b6864a5de284523c52ddc243))
* **holdings:** show current and average price per unit for every holding ([#1207](https://github.com/lifekit-hq/finance-sentry/issues/1207)) ([4171993](https://github.com/lifekit-hq/finance-sentry/commit/41719937170249a26851c16791ca523f29e658c6))
* **ledger:** fit the chat to the screen with no outer scroll and a keyboard-aware pinned composer ([#1220](https://github.com/lifekit-hq/finance-sentry/issues/1220)) ([#1224](https://github.com/lifekit-hq/finance-sentry/issues/1224)) ([e450f50](https://github.com/lifekit-hq/finance-sentry/commit/e450f505d28b6782c43f09853864cb454eb30082))
* **mcp:** refuse Research writes to service tokens (Ledger proposes, owner decides) ([#1164](https://github.com/lifekit-hq/finance-sentry/issues/1164)) ([d62cc14](https://github.com/lifekit-hq/finance-sentry/commit/d62cc140a135df724e569ba265c57146b4c46e76))
* **wealth:** add monthly-income lens to the dashboard hero ([#1173](https://github.com/lifekit-hq/finance-sentry/issues/1173)) ([4958b1b](https://github.com/lifekit-hq/finance-sentry/commit/4958b1bff07b18bc463ce33c80657916b6038ebd))
* **wealth:** one projection-assumptions engine (M10/C8) for the 12-month and FIRE projections ([#1172](https://github.com/lifekit-hq/finance-sentry/issues/1172)) ([b6775d8](https://github.com/lifekit-hq/finance-sentry/commit/b6775d85e0b743ae6558a93efe0c8cd3e1e17b5b))
* **wealth:** wire the net-worth bridge (M5) behind GET /net-worth/bridge ([#1171](https://github.com/lifekit-hq/finance-sentry/issues/1171)) ([3f67d9a](https://github.com/lifekit-hq/finance-sentry/commit/3f67d9a741297d6a3660883469373c80caa20b42))


### Bug Fixes

* **alerts:** stop mobile horizontal overflow and close the sticky day-header gap ([#1254](https://github.com/lifekit-hq/finance-sentry/issues/1254)) ([8265171](https://github.com/lifekit-hq/finance-sentry/commit/82651712d6ad5864b11c1b43bf38f0a67b28ce40))
* **bank-sync:** add scoped recategorize move to re-sort ESKY rows from rent and utilities to travel ([#1240](https://github.com/lifekit-hq/finance-sentry/issues/1240)) ([99aa720](https://github.com/lifekit-hq/finance-sentry/commit/99aa720ac148bb9756ae958635b41c23c2813ced))
* **bank-sync:** categorise travel-agency merchants as travel, not rent and utilities ([#1237](https://github.com/lifekit-hq/finance-sentry/issues/1237)) ([62bd37c](https://github.com/lifekit-hq/finance-sentry/commit/62bd37cb76d2098fc3b92b2d9d1c2eb5c58e163c))
* **bank-sync:** converge live MCC rows 4214/4215/4225/5832 to the code mapping ([#1162](https://github.com/lifekit-hq/finance-sentry/issues/1162)) ([fee5781](https://github.com/lifekit-hq/finance-sentry/commit/fee5781cad969df5056fb8604e607fc3167a4b8d))
* **bank-sync:** make the ledger insight line follow the selected period and account ([#1256](https://github.com/lifekit-hq/finance-sentry/issues/1256)) ([7d35069](https://github.com/lifekit-hq/finance-sentry/commit/7d35069ff6dc184e10e83c7cb737a6a26285e625))
* **bank-sync:** reconcile pending rows by date and add repair for retired ones ([#1242](https://github.com/lifekit-hq/finance-sentry/issues/1242)) ([66ed2a8](https://github.com/lifekit-hq/finance-sentry/commit/66ed2a837a9ade39ad3ff77ad166d6f6c1829890))
* **bank-sync:** treat Ukrainian card transfers as non-merchants in subscription detection ([#1179](https://github.com/lifekit-hq/finance-sentry/issues/1179)) ([3e7d917](https://github.com/lifekit-hq/finance-sentry/commit/3e7d917386c2a433fd9cea453d42dc155b9d26c5))
* **e2e:** disambiguate the live smoke's period locator ([#1208](https://github.com/lifekit-hq/finance-sentry/issues/1208)) ([#1209](https://github.com/lifekit-hq/finance-sentry/issues/1209)) ([083882d](https://github.com/lifekit-hq/finance-sentry/commit/083882dc6df1c5fb0cb1c5d36b3b3aa4b4bbe410))
* **e2e:** match the live smoke's Spent/Income tiles as links ([#1213](https://github.com/lifekit-hq/finance-sentry/issues/1213)) ([#1214](https://github.com/lifekit-hq/finance-sentry/issues/1214)) ([8a69f84](https://github.com/lifekit-hq/finance-sentry/commit/8a69f849a60d6f523ed0f8f435ac31244fb7d368))
* **events:** drop the fired card's duplicate second title ([#1232](https://github.com/lifekit-hq/finance-sentry/issues/1232)) ([d905434](https://github.com/lifekit-hq/finance-sentry/commit/d9054342f8fa42b39496098ab132313c9759ef6b))
* **frontend:** compose the asset dossier skeleton from cmn-skeleton presets ([#1233](https://github.com/lifekit-hq/finance-sentry/issues/1233)) ([b368ce8](https://github.com/lifekit-hq/finance-sentry/commit/b368ce82975d7ac93d2ff3b2fc95313c24bc498f))
* **frontend:** make the list the only scroll container on list pages ([#1243](https://github.com/lifekit-hq/finance-sentry/issues/1243)) ([01b55c0](https://github.com/lifekit-hq/finance-sentry/commit/01b55c082111a45ba8f91e3ade85d3e41e6cbef8))
* **gateway:** drop dead gateway duties and give auth session reads their own API limit ([#1177](https://github.com/lifekit-hq/finance-sentry/issues/1177)) ([f555c2d](https://github.com/lifekit-hq/finance-sentry/commit/f555c2d092201ac088996687028063af11fc59a8))
* **mcp:** carry no bare aggregate unrealized P&L when the cost basis is partial ([#1211](https://github.com/lifekit-hq/finance-sentry/issues/1211)) ([ef910c8](https://github.com/lifekit-hq/finance-sentry/commit/ef910c882f440bca2fbe80068fb6ef19acbd1856)), closes [#982](https://github.com/lifekit-hq/finance-sentry/issues/982)


### Refactoring

* **config:** bind provider configuration through validated Options ([#1181](https://github.com/lifekit-hq/finance-sentry/issues/1181)) ([8a40274](https://github.com/lifekit-hq/finance-sentry/commit/8a4027408e9de2aea8e389f6b63732878a6fbd3d))
* **config:** bind remaining raw configuration reads through validated Options ([#1185](https://github.com/lifekit-hq/finance-sentry/issues/1185)) ([6ea928e](https://github.com/lifekit-hq/finance-sentry/commit/6ea928e079d74a2108ad4711d49f577d57d85ba3))
* **frontend:** bring the Watchlist page up to the shared design system ([#1223](https://github.com/lifekit-hq/finance-sentry/issues/1223)) ([adc9fa9](https://github.com/lifekit-hq/finance-sentry/commit/adc9fa9fe808d609cab5f75f476ed275517f4cb6))
* **frontend:** move remaining stores onto withAsyncStatus and retire global Nullable/AsyncStatus ([#1182](https://github.com/lifekit-hq/finance-sentry/issues/1182)) ([cd166ef](https://github.com/lifekit-hq/finance-sentry/commit/cd166ef222025b9c3f75601b1964f8e1bc7d9b1e))
* **frontend:** replace raw navigation buttons with router links for no-raw-controls ([#1197](https://github.com/lifekit-hq/finance-sentry/issues/1197)) ([5cc8f45](https://github.com/lifekit-hq/finance-sentry/commit/5cc8f4563661f67720083490e6cc3c75e9185784)), closes [#1196](https://github.com/lifekit-hq/finance-sentry/issues/1196)
* **observability:** drop the API's own Loki and file log sinks ([#1183](https://github.com/lifekit-hq/finance-sentry/issues/1183)) ([c82a4a9](https://github.com/lifekit-hq/finance-sentry/commit/c82a4a9d02b9a7bc8ee9964aba572aa3be4b1be7)), closes [#938](https://github.com/lifekit-hq/finance-sentry/issues/938)
* **ui:** use the clickable list row for holding, breakdown and session rows ([#1216](https://github.com/lifekit-hq/finance-sentry/issues/1216)) ([14c05b9](https://github.com/lifekit-hq/finance-sentry/commit/14c05b97cb401a5762db9b6858b6cef1077a2e08))


### Documentation

* **ledger:** route OpenClaw build asks through Kit's relay instead of GitHub issues ([#1170](https://github.com/lifekit-hq/finance-sentry/issues/1170)) ([ba3e670](https://github.com/lifekit-hq/finance-sentry/commit/ba3e670c49d6de963afb5dd3490c37190a286a15))

## [1.16.0](https://github.com/lifekit-hq/finance-sentry/compare/v1.15.0...v1.16.0) (2026-10-19)


### Features

* **agent:** Ledger persona to committed verdicts and direct vault writes ([#1150](https://github.com/lifekit-hq/finance-sentry/issues/1150)) ([25a648d](https://github.com/lifekit-hq/finance-sentry/commit/25a648d3a85aef020477fe0439b410355939a757))
* **auth:** create a Member on first Logto sign-in with the app role ([#1142](https://github.com/lifekit-hq/finance-sentry/issues/1142)) ([e1f2fb5](https://github.com/lifekit-hq/finance-sentry/commit/e1f2fb58f0a2a554739bf7eaddbbc1ad7a5e85dd))
* **auth:** store asset-visibility settings on the profile ([#1145](https://github.com/lifekit-hq/finance-sentry/issues/1145)) ([7c39fa7](https://github.com/lifekit-hq/finance-sentry/commit/7c39fa74d44232e38462d3641f01f6c3044df501))
* **bank-sync:** closed accounts section and card lifecycle notes on Accounts ([#1141](https://github.com/lifekit-hq/finance-sentry/issues/1141)) ([efcc135](https://github.com/lifekit-hq/finance-sentry/commit/efcc1350aeba0f170dd20ab54dccd079305cd393))
* **bank-sync:** one Accounts row for every category, status on the avatar, brokers open in place ([#1138](https://github.com/lifekit-hq/finance-sentry/issues/1138)) ([2e80369](https://github.com/lifekit-hq/finance-sentry/commit/2e80369885906f6cae1e7b892d356f8869143e4f))
* **bank-sync:** retire, reopen and exclude vanished accounts from live totals (card lifecycle 2/3) ([#1135](https://github.com/lifekit-hq/finance-sentry/issues/1135)) ([0c83f1d](https://github.com/lifekit-hq/finance-sentry/commit/0c83f1d7358bb62efed97564dd316d55a5070c11))
* **bank-sync:** show the replaced card line on an in-place card reissue ([#1143](https://github.com/lifekit-hq/finance-sentry/issues/1143)) ([111ac26](https://github.com/lifekit-hq/finance-sentry/commit/111ac2682a26abdf79b9101155fcd3e2af360354))
* **bank-sync:** store IBAN, PANs and card expiry and log would-retire accounts (card lifecycle 1/3) ([#1133](https://github.com/lifekit-hq/finance-sentry/issues/1133)) ([ff50a66](https://github.com/lifekit-hq/finance-sentry/commit/ff50a666b7dfbe6d0861195c6edc36f4427308e1))
* **banksync:** map every MCC in code and fix recategorization side issues ([#1151](https://github.com/lifekit-hq/finance-sentry/issues/1151)) ([9909022](https://github.com/lifekit-hq/finance-sentry/commit/99090222547bbccc466d5dd460cebf336d3a21c3))
* **connections:** probe suspect and Dormant connections and slow Dormant sync cadence ([#1124](https://github.com/lifekit-hq/finance-sentry/issues/1124)) ([6276236](https://github.com/lifekit-hq/finance-sentry/commit/62762369fe7b5fa2ac0e32af4c8352c638e18c20))
* **connections:** surface connection health to users (retrying, reconnect needed, dormant) ([#1128](https://github.com/lifekit-hq/finance-sentry/issues/1128)) ([f55c141](https://github.com/lifekit-hq/finance-sentry/commit/f55c1415bee5ae98d2d2b6aa4d1c391626c74e46))
* **core:** M8 risk statistics to empyrical definitions ([#1156](https://github.com/lifekit-hq/finance-sentry/issues/1156)) ([9b2cf47](https://github.com/lifekit-hq/finance-sentry/commit/9b2cf471020e909f30ea07f59caf1725207ebff8))
* **core:** SourceEvent contract with UTC timestamp, network, fee and status ([#1153](https://github.com/lifekit-hq/finance-sentry/issues/1153)) ([e827ba6](https://github.com/lifekit-hq/finance-sentry/commit/e827ba61a3829ac459829d4ccfc778b2e2776d4f))
* **crypto-sync:** ingest venue fiat and Revolut X transfer flows for crypto contributions vs growth ([#1129](https://github.com/lifekit-hq/finance-sentry/issues/1129)) ([9bf2bb1](https://github.com/lifekit-hq/finance-sentry/commit/9bf2bb196dbc3cd1ef6e5bb7153ce1923f6a7f2b))
* **crypto-sync:** read Binance on-chain legs and match transfers across venues ([#1130](https://github.com/lifekit-hq/finance-sentry/issues/1130)) ([a1c71ec](https://github.com/lifekit-hq/finance-sentry/commit/a1c71ece5b7b4523ac7782385dfa0fc363331bd1))
* **crypto-sync:** read earn, airdrop and dust events into the growth split ([#1131](https://github.com/lifekit-hq/finance-sentry/issues/1131)) ([c48eea6](https://github.com/lifekit-hq/finance-sentry/commit/c48eea68b6c7adfddd57007ec43fc5834e42ab07))
* **crypto:** keep the Binance trade commission and book it as a fee ([#1155](https://github.com/lifekit-hq/finance-sentry/issues/1155)) ([e53742c](https://github.com/lifekit-hq/finance-sentry/commit/e53742c7d12cf425bf6aee6bad11dfede8a39c0d))
* **frontend:** asset-visibility settings and collapsed hidden rows on Accounts ([#1148](https://github.com/lifekit-hq/finance-sentry/issues/1148)) ([6aaa98d](https://github.com/lifekit-hq/finance-sentry/commit/6aaa98dae6cabd6b43075504b3c0dcb84c1dc589))
* **frontend:** continuous paging, shaped skeletons and kept content on revisit ([#1125](https://github.com/lifekit-hq/finance-sentry/issues/1125)) ([bd8efd6](https://github.com/lifekit-hq/finance-sentry/commit/bd8efd61851229efaa06146eafe92ca20c6418be))
* **frontend:** disable phone zoom and open dialogs as bottom sheets on [@lifekit-hq](https://github.com/lifekit-hq) 3.7.0 ([#1140](https://github.com/lifekit-hq/finance-sentry/issues/1140)) ([d919b82](https://github.com/lifekit-hq/finance-sentry/commit/d919b82c0467038ebbf2c66df56128d0b2235e88))
* **frontend:** specific empty states with a way out, and a fixed ledger summary ([#1139](https://github.com/lifekit-hq/finance-sentry/issues/1139)) ([b45eb0e](https://github.com/lifekit-hq/finance-sentry/commit/b45eb0ed3a40677c752ddc7bfe7b06352d2af171))
* **frontend:** sticky Accounts section headers, 44px menu targets and remembered disclosure ([#1121](https://github.com/lifekit-hq/finance-sentry/issues/1121)) ([59eac2c](https://github.com/lifekit-hq/finance-sentry/commit/59eac2c306982b8229993ad4679b06591a2f6122))
* **investments:** hide empty and small positions behind a collapsed row per asset class ([#1149](https://github.com/lifekit-hq/finance-sentry/issues/1149)) ([4de08d3](https://github.com/lifekit-hq/finance-sentry/commit/4de08d3bc69e2bfcaf03e82ea3aea17afa49af3b))
* **research:** Ledger scoreboard - an arena of one ([#986](https://github.com/lifekit-hq/finance-sentry/issues/986)) ([#1152](https://github.com/lifekit-hq/finance-sentry/issues/1152)) ([050da56](https://github.com/lifekit-hq/finance-sentry/commit/050da5636462d9bda49e35e45c5daefebaa9b7b5))
* **research:** one-word scorecard verdict with drill-down on dossier and recommendation card ([#1120](https://github.com/lifekit-hq/finance-sentry/issues/1120)) ([5299fe7](https://github.com/lifekit-hq/finance-sentry/commit/5299fe7baa4d306d10e8bf71b4c170cb4307e2ba))
* **wealth:** annotate hidden rows in the wealth summary with a visibility rule ([#1147](https://github.com/lifekit-hq/finance-sentry/issues/1147)) ([c721986](https://github.com/lifekit-hq/finance-sentry/commit/c72198690466ddd457c0d41261629c33cdac0828))
* **wealth:** brokerage contributions vs growth and time-weighted Book vs SPY ([#1123](https://github.com/lifekit-hq/finance-sentry/issues/1123)) ([3867f96](https://github.com/lifekit-hq/finance-sentry/commit/3867f96b401fb4ffede028232ced92ab8e9af8f6))
* **wealth:** enter Steam and other values by hand as L3 in-economy holdings ([#1154](https://github.com/lifekit-hq/finance-sentry/issues/1154)) ([fdc952a](https://github.com/lifekit-hq/finance-sentry/commit/fdc952af633a890fb4d94319721f50285ee543d1))
* **wealth:** R7 presentation patterns - contributions vs growth, timing gap, growth of 10,000 ([#1157](https://github.com/lifekit-hq/finance-sentry/issues/1157)) ([17d3ef0](https://github.com/lifekit-hq/finance-sentry/commit/17d3ef0dcc80e2a06354ac138c9124ace8213512))


### Bug Fixes

* **bank-sync:** show every negative on Accounts with a minus sign instead of "Owes" ([#1134](https://github.com/lifekit-hq/finance-sentry/issues/1134)) ([0a4e398](https://github.com/lifekit-hq/finance-sentry/commit/0a4e398d84e0f3ba22ee70cb0838c5c1e2411409))
* **brokerage-sync:** keep brokerage link day across IBKR credential relink ([#1127](https://github.com/lifekit-hq/finance-sentry/issues/1127)) ([f507db1](https://github.com/lifekit-hq/finance-sentry/commit/f507db139e2f367cb7d3857b307dd87aea5d0fd5))
* **crypto-sync:** fetch chain details and match transfers for Revolut X external_crypto sends ([#1137](https://github.com/lifekit-hq/finance-sentry/issues/1137)) ([c3e1b96](https://github.com/lifekit-hq/finance-sentry/commit/c3e1b9628a3ddca885c37ead27fb3297f49eb78f))
* **crypto-sync:** read Binance numeric positionId and wrap unreadable success bodies as BinanceException ([#1136](https://github.com/lifekit-hq/finance-sentry/issues/1136)) ([73a3cf6](https://github.com/lifekit-hq/finance-sentry/commit/73a3cf6f075f5894463d0b5d1a6699d8572b55cc))
* **frontend:** pin libexpat to the CVE-2026-77214 fix in the prod image ([#1146](https://github.com/lifekit-hq/finance-sentry/issues/1146)) ([fec2c10](https://github.com/lifekit-hq/finance-sentry/commit/fec2c1045ae37268b9d31e93803a92fcdb54e780))
* **frontend:** restore route view transitions and drop crossfade stopgaps ([#1144](https://github.com/lifekit-hq/finance-sentry/issues/1144)) ([8bcb005](https://github.com/lifekit-hq/finance-sentry/commit/8bcb005f155128883b000aa2147551d6901e99ae))
* **frontend:** static placeholders and history Retry after a failed dashboard range change ([#1126](https://github.com/lifekit-hq/finance-sentry/issues/1126)) ([47b5b29](https://github.com/lifekit-hq/finance-sentry/commit/47b5b297cdcbb51fd3ffca4208f880e196faebf2))
* **wealth:** stop a closed Monobank sub-account failing its institution; show negative net-worth categories ([#1132](https://github.com/lifekit-hq/finance-sentry/issues/1132)) ([1097eee](https://github.com/lifekit-hq/finance-sentry/commit/1097eee9fc2b7ce3672eb7cca730547a7e34efa0))

## [1.15.0](https://github.com/lifekit-hq/finance-sentry/compare/v1.14.0...v1.15.0) (2026-10-15)


### Features

* **859:** provision the VAPID keypair and subject in docker/.env.sops ([#975](https://github.com/lifekit-hq/finance-sentry/issues/975)) ([c2b0dae](https://github.com/lifekit-hq/finance-sentry/commit/c2b0daea993aa9a34500debaa883d99ee0cadebf))
* **agent:** cap Ledger replies with a 75-word Answer shape ([#1116](https://github.com/lifekit-hq/finance-sentry/issues/1116)) ([f82f41c](https://github.com/lifekit-hq/finance-sentry/commit/f82f41c2ee38dafe58d60a49f1797a884ae35560))
* **alerts:** analyst rating-change alert per held name per day ([#927](https://github.com/lifekit-hq/finance-sentry/issues/927)) ([61f9e53](https://github.com/lifekit-hq/finance-sentry/commit/61f9e53e5d27551d8ceb33aa8753887375652262))
* **alerts:** open the server-resolved appPath of each alert ([#917](https://github.com/lifekit-hq/finance-sentry/issues/917)) ([cbd893c](https://github.com/lifekit-hq/finance-sentry/commit/cbd893c6fad6cf05989dbec8e20338c718685b51))
* **alerts:** page, route and render every alert type on the alerts page ([#858](https://github.com/lifekit-hq/finance-sentry/issues/858)) ([900adc1](https://github.com/lifekit-hq/finance-sentry/commit/900adc1a36dc7fddf7bfe2c42e672ab24ecd672f))
* **alerts:** reshape the weekly, FIRE and family briefs into linked terse bullets ([#925](https://github.com/lifekit-hq/finance-sentry/issues/925)) ([63e3068](https://github.com/lifekit-hq/finance-sentry/commit/63e3068b8c5045488afec998129c123073a10990))
* **alerts:** resolve every alert to the app page it is about and open it from push ([#466](https://github.com/lifekit-hq/finance-sentry/issues/466)) ([#912](https://github.com/lifekit-hq/finance-sentry/issues/912)) ([e87629e](https://github.com/lifekit-hq/finance-sentry/commit/e87629e10446892e8e3315154982c9b4889579da))
* **assets:** draw the dossier radar trend with the library line chart ([#919](https://github.com/lifekit-hq/finance-sentry/issues/919)) ([4634b1f](https://github.com/lifekit-hq/finance-sentry/commit/4634b1f3569e537f7f23b77b2475dc5e3e1baccb))
* **assets:** redesign asset dossier with hero, section chips and richer position data ([#1073](https://github.com/lifekit-hq/finance-sentry/issues/1073)) ([f5fd04c](https://github.com/lifekit-hq/finance-sentry/commit/f5fd04cdf9ddf353fec580a9851a31e23f858e7b))
* **assets:** show price and day change in the dossier header ([#965](https://github.com/lifekit-hq/finance-sentry/issues/965)) ([51cd864](https://github.com/lifekit-hq/finance-sentry/commit/51cd8642a5335b020c7109e94d4ece9d1bfca2ef))
* **auth:** close your own account with a 30-day reopenable grace before purge ([#1086](https://github.com/lifekit-hq/finance-sentry/issues/1086)) ([b2743b2](https://github.com/lifekit-hq/finance-sentry/commit/b2743b2082de51f134c0dbec456d0a2194f12b13))
* **auth:** end the Logto session on sign-out (RP-initiated logout) ([#980](https://github.com/lifekit-hq/finance-sentry/issues/980)) ([47a17aa](https://github.com/lifekit-hq/finance-sentry/commit/47a17aac799c82857fcbaf6224e2fba5de541010))
* **auth:** gate sign-in behind Logto only and retire direct Google and password login ([#970](https://github.com/lifekit-hq/finance-sentry/issues/970)) ([76cbcda](https://github.com/lifekit-hq/finance-sentry/commit/76cbcda02cfdb9f271603c19f2330da41e788920))
* **auth:** migrate to the lifekit app-shell standard with per-user synced appearance ([#1074](https://github.com/lifekit-hq/finance-sentry/issues/1074)) ([f5d9d91](https://github.com/lifekit-hq/finance-sentry/commit/f5d9d9153d5e672d82ec3269685a840d98709552))
* **auth:** turn off direct Google sign-in by default in production ([#847](https://github.com/lifekit-hq/finance-sentry/issues/847)) ([0bc4692](https://github.com/lifekit-hq/finance-sentry/commit/0bc4692e8ccf918d3ea3ee38d64f40d4b509295c))
* **bank-sync:** add quick period chips to the transaction ledger ([#922](https://github.com/lifekit-hq/finance-sentry/issues/922)) ([fe07ee1](https://github.com/lifekit-hq/finance-sentry/commit/fe07ee1084a0dd598ca30ca85749356ecb1ed4ba))
* **bank-sync:** apply dashboard period selector to every tile ([#868](https://github.com/lifekit-hq/finance-sentry/issues/868)) ([4fea186](https://github.com/lifekit-hq/finance-sentry/commit/4fea1862a2ded97c227094b6095bc4879b74b728))
* **bank-sync:** brokerage and exchange nav rows on Accounts open Investments by provider ([#1114](https://github.com/lifekit-hq/finance-sentry/issues/1114)) ([c0985d4](https://github.com/lifekit-hq/finance-sentry/commit/c0985d4b1a3e099b8101e62cf9472fdd16a0f10f))
* **bank-sync:** carry dashboard range into transaction drill-downs ([#870](https://github.com/lifekit-hq/finance-sentry/issues/870)) ([a04a3a6](https://github.com/lifekit-hq/finance-sentry/commit/a04a3a6604dcfb455e0da7117c2124a93cac3f14))
* **bank-sync:** connection health core and policy in shadow mode (Option B S1) ([#891](https://github.com/lifekit-hq/finance-sentry/issues/891)) ([07b35e6](https://github.com/lifekit-hq/finance-sentry/commit/07b35e6c3bd8b3858acb5b945c6e76e3fd04fcae))
* **bank-sync:** drill flow breakdown figures down to their transactions ([#914](https://github.com/lifekit-hq/finance-sentry/issues/914)) ([0fdc428](https://github.com/lifekit-hq/finance-sentry/commit/0fdc42882ab9a757de68e82616a13a1eb3697a19))
* **bank-sync:** filter bar on the global transaction ledger with URL sync ([#884](https://github.com/lifekit-hq/finance-sentry/issues/884)) ([01d092d](https://github.com/lifekit-hq/finance-sentry/commit/01d092d3a73aa7bc270d460fb073603d6664d94d))
* **bank-sync:** ledger filter sheet for period, type and category ([#972](https://github.com/lifekit-hq/finance-sentry/issues/972)) ([3d9276b](https://github.com/lifekit-hq/finance-sentry/commit/3d9276b8b28450a9677119ede590f2f01ce13690))
* **bank-sync:** link transaction drawer, net worth and budget category to ledger views ([#911](https://github.com/lifekit-hq/finance-sentry/issues/911)) ([7cdbba6](https://github.com/lifekit-hq/finance-sentry/commit/7cdbba6663166bc5058fdacf235a4062460f70c5))
* **bank-sync:** route Monobank and TrueLayer sync failures through the connection-health policy (S2) ([#1082](https://github.com/lifekit-hq/finance-sentry/issues/1082)) ([a41cac0](https://github.com/lifekit-hq/finance-sentry/commit/a41cac09b6108df1249ccbada3a40b6a24601781))
* **bank-sync:** switch dashboard period presets to 1W/MTD/1M/3M/YTD/1Y/ALL ([#890](https://github.com/lifekit-hq/finance-sentry/issues/890)) ([32f29e1](https://github.com/lifekit-hq/finance-sentry/commit/32f29e1fd6c29d6a751c99aa3cb5017d4612cc59))
* **bank-sync:** value-first position rows, reconnect attention row and subtotal headers on Accounts ([#1111](https://github.com/lifekit-hq/finance-sentry/issues/1111)) ([7aad176](https://github.com/lifekit-hq/finance-sentry/commit/7aad176c8d695a6568cc02e10c244616921a956d))
* **brokerage-sync:** name Deposits & Withdrawals and Change in NAV in the IBKR Flex guide ([#1092](https://github.com/lifekit-hq/finance-sentry/issues/1092)) ([2382660](https://github.com/lifekit-hq/finance-sentry/commit/238266070c324dcb0be1a1087dcca2bf2c96a53b))
* **brokerage-sync:** simplify Interactive Brokers connect to a two-field Flex form ([#852](https://github.com/lifekit-hq/finance-sentry/issues/852)) ([f20dec8](https://github.com/lifekit-hq/finance-sentry/commit/f20dec818377e066fdfe18e5163aafd34fd050d5))
* **brokerage:** connect Inzhur by pasting the refresh-token session ([#987](https://github.com/lifekit-hq/finance-sentry/issues/987)) ([a0f005e](https://github.com/lifekit-hq/finance-sentry/commit/a0f005ed1cdeebf034077101b220ba98997160db))
* **brokerage:** publish IBKR Flex positions and cash as holdings ([#849](https://github.com/lifekit-hq/finance-sentry/issues/849)) ([95262dd](https://github.com/lifekit-hq/finance-sentry/commit/95262ddabfdda16e85ba3132d110ed43f2cf061c))
* **brokerage:** sync the owner's Inzhur cabinet, read-only, once a day ([#920](https://github.com/lifekit-hq/finance-sentry/issues/920)) ([cc2e556](https://github.com/lifekit-hq/finance-sentry/commit/cc2e5569a4a79d5ebf567aab9ecea2ff3c2dcfef))
* **budgets:** show month-end pace on budget summary, budgets page and get_budget_status ([#874](https://github.com/lifekit-hq/finance-sentry/issues/874)) ([a878dd6](https://github.com/lifekit-hq/finance-sentry/commit/a878dd66b59f71bdea8a3059c82a33b1a6935406))
* **companion:** carry an absolute appUrl on the agent wake and MCP companion event ([#916](https://github.com/lifekit-hq/finance-sentry/issues/916)) ([18bf926](https://github.com/lifekit-hq/finance-sentry/commit/18bf926be3a79d2a0a2f14543250c23953799770))
* **companion:** push subscription storage and API ([#861](https://github.com/lifekit-hq/finance-sentry/issues/861)) ([bbf54d5](https://github.com/lifekit-hq/finance-sentry/commit/bbf54d5f51267152bc3294e15813549f649203a3))
* **companion:** push the deduped analyst rating-change alert to phone ([#988](https://github.com/lifekit-hq/finance-sentry/issues/988)) ([eb915f0](https://github.com/lifekit-hq/finance-sentry/commit/eb915f0eb8a0b8eff6fa16d1b420ee31f1b5acc7))
* **companion:** save problem reports and forward them to the fleet inbox ([#1001](https://github.com/lifekit-hq/finance-sentry/issues/1001)) ([0b98fb5](https://github.com/lifekit-hq/finance-sentry/commit/0b98fb536bb49d97a9a779e93ec7c4725accf4a0))
* **companion:** send Web Push notifications via CompanionPushJob ([#862](https://github.com/lifekit-hq/finance-sentry/issues/862)) ([22afdc1](https://github.com/lifekit-hq/finance-sentry/commit/22afdc1c43e1948ac530287ae04639ce467714b3))
* **companion:** wire realtime agent push via gateway hook ([#643](https://github.com/lifekit-hq/finance-sentry/issues/643)) ([#899](https://github.com/lifekit-hq/finance-sentry/issues/899)) ([90783a4](https://github.com/lifekit-hq/finance-sentry/commit/90783a450dbe4842b90622b272b0bf6da89f25c0))
* **connections:** add RateLimited and NotReady failure classes with NotBefore ([#1101](https://github.com/lifekit-hq/finance-sentry/issues/1101)) ([0617623](https://github.com/lifekit-hq/finance-sentry/commit/0617623f6bf8143d4211df12983f44c014a51ab0))
* **connections:** drive IBKR sync-failure alerts from the connection-health policy; Flex, Binance and Revolut X classified in shadow ([#1083](https://github.com/lifekit-hq/finance-sentry/issues/1083)) ([58430a5](https://github.com/lifekit-hq/finance-sentry/commit/58430a571004be7fe1d39d5cd6fc9122bd0a08c2))
* **connections:** early re-checks for once-a-day Flex failures and a failures-and-retries contract ([#1103](https://github.com/lifekit-hq/finance-sentry/issues/1103)) ([ea5e9bb](https://github.com/lifekit-hq/finance-sentry/commit/ea5e9bbe074d626a0fa91579b69009bb1a372748))
* **core:** add the pure measure engine (M1-M6, M9) with golden fixtures ([#1098](https://github.com/lifekit-hq/finance-sentry/issues/1098)) ([0c419dc](https://github.com/lifekit-hq/finance-sentry/commit/0c419dce5403b23b4515818e351aac5143cf70a6))
* **core:** add typed Headline record and carry it on headline figures and MCP tools ([#1104](https://github.com/lifekit-hq/finance-sentry/issues/1104)) ([ba9f445](https://github.com/lifekit-hq/finance-sentry/commit/ba9f4458d884e5cbd102784f31b82b6fbcbb2f4f))
* **dashboard:** re-stack net-worth chart as cash and invested, add invested change ([#995](https://github.com/lifekit-hq/finance-sentry/issues/995)) ([75e8a40](https://github.com/lifekit-hq/finance-sentry/commit/75e8a40356b50efef6b5e78cca639cc213c6051f))
* **dashboard:** show invested and cash under the net-worth hero ([#992](https://github.com/lifekit-hq/finance-sentry/issues/992)) ([c3e099a](https://github.com/lifekit-hq/finance-sentry/commit/c3e099af2635c1e422830840cdaafa7eabcc58c9))
* **events:** open the entity each events-page entry is about ([#918](https://github.com/lifekit-hq/finance-sentry/issues/918)) ([eebc9a2](https://github.com/lifekit-hq/finance-sentry/commit/eebc9a217a79840297d9aad26496bb4e65cb24c0))
* **frontend:** add Report a problem sheet and clickable version footer ([#1051](https://github.com/lifekit-hq/finance-sentry/issues/1051)) ([83d3c88](https://github.com/lifekit-hq/finance-sentry/commit/83d3c88e31d66b53b99832b394ae46a2642f382f))
* **frontend:** adopt lifekit Monogram favicon, PWA icons and self-hosted Inter ([#856](https://github.com/lifekit-hq/finance-sentry/issues/856)) ([953f870](https://github.com/lifekit-hq/finance-sentry/commit/953f870ac9b24dccbded00d4b78ba0ee5456ed8a))
* **frontend:** adopt lifekit-common filter bar, chip row and sticky list sections ([#1115](https://github.com/lifekit-hq/finance-sentry/issues/1115)) ([ec0e633](https://github.com/lifekit-hq/finance-sentry/commit/ec0e633245842f72549a055c79aea3d09b35b43e))
* **frontend:** adopt the lifekit theme engine with a Settings &gt; Appearance colour picker ([#971](https://github.com/lifekit-hq/finance-sentry/issues/971)) ([5eb9363](https://github.com/lifekit-hq/finance-sentry/commit/5eb936372fe4ff0ae3462937c890d6b3e4783764))
* **frontend:** adopt the phone contract for page chrome, More page and avatar ([#962](https://github.com/lifekit-hq/finance-sentry/issues/962)) ([13f8e41](https://github.com/lifekit-hq/finance-sentry/commit/13f8e41e8c73c2b9b4b2636ab67b2a0fc134fad7))
* **frontend:** back ledger search with ?q= and drill into it from alerts, flow breakdown and subscriptions ([#924](https://github.com/lifekit-hq/finance-sentry/issues/924)) ([b6dcb35](https://github.com/lifekit-hq/finance-sentry/commit/b6dcb35ea2b61feac3cbfaa401ae18f49b54e0ca))
* **frontend:** bump [@lifekit-hq](https://github.com/lifekit-hq) packages to ^1.1.0 ([#879](https://github.com/lifekit-hq/finance-sentry/issues/879)) ([1bec087](https://github.com/lifekit-hq/finance-sentry/commit/1bec087fbd319d52975907518e4f5585781ecd5d))
* **frontend:** click through from dashboard and holdings charts to their entity pages ([#1012](https://github.com/lifekit-hq/finance-sentry/issues/1012)) ([7c6ae49](https://github.com/lifekit-hq/finance-sentry/commit/7c6ae49a556f18b783a608cb3d43ea6b2044adef))
* **frontend:** fixed colours for asset classes, gain/loss and money in/out ([#981](https://github.com/lifekit-hq/finance-sentry/issues/981)) ([1a7ffa2](https://github.com/lifekit-hq/finance-sentry/commit/1a7ffa24c44cc0bca67aea3bc5bd8ded43a221a8))
* **frontend:** link investment holdings and analyst actions to their targets ([#913](https://github.com/lifekit-hq/finance-sentry/issues/913)) ([9dca01e](https://github.com/lifekit-hq/finance-sentry/commit/9dca01e11597a7f77618e1f546356fd4c415a144))
* **frontend:** one dashboard hero card with scrubbable net-worth chart ([#966](https://github.com/lifekit-hq/finance-sentry/issues/966)) ([6df5706](https://github.com/lifekit-hq/finance-sentry/commit/6df57060b7804c2ed9d188415bfb37875360bd23))
* **frontend:** quality badges and reason sheet on headline figures ([#1107](https://github.com/lifekit-hq/finance-sentry/issues/1107)) ([0646fa8](https://github.com/lifekit-hq/finance-sentry/commit/0646fa8b5571d96f8e60eddb4a100ab6d87fbfc3))
* **frontend:** set page titles through Route.title and a TitleStrategy ([#954](https://github.com/lifekit-hq/finance-sentry/issues/954)) ([#1008](https://github.com/lifekit-hq/finance-sentry/issues/1008)) ([0690674](https://github.com/lifekit-hq/finance-sentry/commit/069067498b3044d8b89d85ea84eff1c7ed9be308))
* **frontend:** upgrade @lifekit-hq/* to v1.2.0 ([#887](https://github.com/lifekit-hq/finance-sentry/issues/887)) ([0622bec](https://github.com/lifekit-hq/finance-sentry/commit/0622becfd739ed145f2ddc23afd69af657eff7f5))
* **frontend:** what's new panel from the CHANGELOG and plain notes ([#998](https://github.com/lifekit-hq/finance-sentry/issues/998)) ([b390e6c](https://github.com/lifekit-hq/finance-sentry/commit/b390e6c7d7d76f930787a8771ece5b27f5bf24d2))
* **holdings:** provider filter and ?provider= deep link on Investments ([#1110](https://github.com/lifekit-hq/finance-sentry/issues/1110)) ([739468d](https://github.com/lifekit-hq/finance-sentry/commit/739468db92e238aeacd48788c906696c5cfe484e))
* **holdings:** show each holding's day change from the research quote ([#964](https://github.com/lifekit-hq/finance-sentry/issues/964)) ([e0f5b75](https://github.com/lifekit-hq/finance-sentry/commit/e0f5b751eea32c4e57d7fc2e1b1965abe4b98e47))
* **mcp:** serve the finance framework primer as an MCP resource ([#1118](https://github.com/lifekit-hq/finance-sentry/issues/1118)) ([dbd3d00](https://github.com/lifekit-hq/finance-sentry/commit/dbd3d0039b509c5235fe1a5cf97e7b81a8a90edd))
* **observability:** export per-job consecutive-failure streak metric ([#1031](https://github.com/lifekit-hq/finance-sentry/issues/1031)) ([c0f4ffc](https://github.com/lifekit-hq/finance-sentry/commit/c0f4ffc38297d00cd88e71b59a734f82549efaef))
* **radar:** holdings-based return beside the book vs SPY value change ([#1100](https://github.com/lifekit-hq/finance-sentry/issues/1100)) ([ce894d5](https://github.com/lifekit-hq/finance-sentry/commit/ce894d564a81a880e04656ab674b62994d8afa3f))
* **research:** add watchlist page ([#1112](https://github.com/lifekit-hq/finance-sentry/issues/1112)) ([3433349](https://github.com/lifekit-hq/finance-sentry/commit/343334966fc23cf8356107c3fb8db6345afc4eb0))
* **research:** asset dossier stage 2 - chart, checks and return decomposition ([#1075](https://github.com/lifekit-hq/finance-sentry/issues/1075)) ([5cc8108](https://github.com/lifekit-hq/finance-sentry/commit/5cc8108ee0f8303d07fedd30b71a74bcb15c99a2))
* **research:** Ledger proposes Add/Trim/Exit/Hold recommendations for owner confirmation ([#1077](https://github.com/lifekit-hq/finance-sentry/issues/1077)) ([e35b1db](https://github.com/lifekit-hq/finance-sentry/commit/e35b1db90a827976411898be1440ffcd4731640c))
* **research:** read ifrs-full 20-F/40-F fundamentals for foreign private issuers ([#837](https://github.com/lifekit-hq/finance-sentry/issues/837)) ([#896](https://github.com/lifekit-hq/finance-sentry/issues/896)) ([fb94e09](https://github.com/lifekit-hq/finance-sentry/commit/fb94e097325091c5628801815e054e93dedc2525))
* **research:** reconcile macro calendar dates and fire macro event alerts once ([#1113](https://github.com/lifekit-hq/finance-sentry/issues/1113)) ([e90d737](https://github.com/lifekit-hq/finance-sentry/commit/e90d737dd2d8bc95690cca970bdb3b3c8a6edc3d))
* **research:** score annual-only filers on fiscal-year periods ([#907](https://github.com/lifekit-hq/finance-sentry/issues/907)) ([b64db96](https://github.com/lifekit-hq/finance-sentry/commit/b64db96ec0d449042b57a39b03825a9c9ebe5283))
* **research:** source-agnostic fundamentals provider chain with provenance ([#910](https://github.com/lifekit-hq/finance-sentry/issues/910)) ([32e6b25](https://github.com/lifekit-hq/finance-sentry/commit/32e6b25bcb6478e1dcc9c02e3e206509b70a4d6c))
* **settings:** add push notification settings (spec 859 step 5) ([#867](https://github.com/lifekit-hq/finance-sentry/issues/867)) ([79d3207](https://github.com/lifekit-hq/finance-sentry/commit/79d32078f29c5be559db8f22947d36d4b2ace41a))
* **shell:** list held names, watchlist names and accounts in the command palette ([#923](https://github.com/lifekit-hq/finance-sentry/issues/923)) ([ecc27d4](https://github.com/lifekit-hq/finance-sentry/commit/ecc27d45a7d629d80c79927f0530904606e38b28))
* **subscriptions:** track commitments from a picked transaction with one Add dialog ([#848](https://github.com/lifekit-hq/finance-sentry/issues/848)) ([8c1f083](https://github.com/lifekit-hq/finance-sentry/commit/8c1f08357bc9e393fbedeffa76851e2b48e10a2d))
* **wealth:** add FIRE date tile to the dashboard and FIRE assumption controls in settings ([#882](https://github.com/lifekit-hq/finance-sentry/issues/882)) ([642caf9](https://github.com/lifekit-hq/finance-sentry/commit/642caf9aecfd5024f9fb86a2a2ec00a83e6f9be4))
* **wealth:** convert net-worth history rows at each date's published rate ([#1105](https://github.com/lifekit-hq/finance-sentry/issues/1105)) ([95f12f7](https://github.com/lifekit-hq/finance-sentry/commit/95f12f78f8fe31a36797017021ca89510e7eb276))
* **wealth:** freeze legacy sleeve columns and count other sources in net worth ([#1109](https://github.com/lifekit-hq/finance-sentry/issues/1109)) ([ed6f639](https://github.com/lifekit-hq/finance-sentry/commit/ed6f6391320bcba5d4b317752f8704e8fc232efd))
* **wealth:** hide net-worth change percentage across a scope change ([#1097](https://github.com/lifekit-hq/finance-sentry/issues/1097)) ([dc454c7](https://github.com/lifekit-hq/finance-sentry/commit/dc454c7ebb205091e47ec4c1d38c2979378acac0))
* **wealth:** read net-worth history, FIRE and dashboard from per-source snapshot rows ([#1108](https://github.com/lifekit-hq/finance-sentry/issues/1108)) ([2d307c8](https://github.com/lifekit-hq/finance-sentry/commit/2d307c813955437e516e0859191cbb0edca2a35c))
* **wealth:** send a monthly FIRE brief alert with the gauge and its assumptions ([#885](https://github.com/lifekit-hq/finance-sentry/issues/885)) ([9b05fe7](https://github.com/lifekit-hq/finance-sentry/commit/9b05fe7925b00d2acfbaed6b0ad90063a900fca8))
* **wealth:** split net-worth snapshots into cash and invested per sleeve ([#994](https://github.com/lifekit-hq/finance-sentry/issues/994)) ([915063a](https://github.com/lifekit-hq/finance-sentry/commit/915063aa6efd35f41ab583dc67b65ac9e73239d7))
* **wealth:** type FX conversion status and surface FX_TODAY_RATE / FX_UNKNOWN_1TO1 on headline figures ([#1099](https://github.com/lifekit-hq/finance-sentry/issues/1099)) ([91dc762](https://github.com/lifekit-hq/finance-sentry/commit/91dc762edc7139306bb43b434a17a7d48cb3f0b8))
* **wealth:** write one net_worth_snapshot_sources row per wealth source ([#1106](https://github.com/lifekit-hq/finance-sentry/issues/1106)) ([0802ddc](https://github.com/lifekit-hq/finance-sentry/commit/0802ddc59fb2d3e8031f4a3ebd01ac5d71eb35f9))


### Bug Fixes

* **agent:** give chat widget and sessions panel overlay semantics ([#1027](https://github.com/lifekit-hq/finance-sentry/issues/1027)) ([87507e9](https://github.com/lifekit-hq/finance-sentry/commit/87507e9ffc70b47e74cbcedb6291423085d3e3ba))
* **agent:** hide the floating Ledger chat button on phone widths ([#854](https://github.com/lifekit-hq/finance-sentry/issues/854)) ([6f5d9a6](https://github.com/lifekit-hq/finance-sentry/commit/6f5d9a6e3c6ed93235457d7c881f1f78f1694c2d))
* **agent:** note Code Mode camelCase callables in the OpenClaw adapter ([#1117](https://github.com/lifekit-hq/finance-sentry/issues/1117)) ([584f34c](https://github.com/lifekit-hq/finance-sentry/commit/584f34cb74b9e0dd8c948e92b96c5cccc3809079))
* **agent:** stream chat over HttpClient so the auth interceptor refreshes it ([#984](https://github.com/lifekit-hq/finance-sentry/issues/984)) ([b66fe78](https://github.com/lifekit-hq/finance-sentry/commit/b66fe78bc0849daa796907a1e9352813f9d3bc32))
* **assets:** map the radar severities the API emits on the asset dossier ([#1071](https://github.com/lifekit-hq/finance-sentry/issues/1071)) ([50a7ddb](https://github.com/lifekit-hq/finance-sentry/commit/50a7ddb4502b39fb03021c6bae7f1407fde5a191))
* **assets:** render dossier position, valuation and trend with library primitives ([#878](https://github.com/lifekit-hq/finance-sentry/issues/878)) ([450d7f4](https://github.com/lifekit-hq/finance-sentry/commit/450d7f4f2e603db398b469baaa40b3d7598627f4))
* **auth:** persist the Data Protection key ring in the Auth database ([#977](https://github.com/lifekit-hq/finance-sentry/issues/977)) ([e342690](https://github.com/lifekit-hq/finance-sentry/commit/e3426902a4c561795b5999b6cab01e1e4096a247))
* **auth:** validate FIRE assumption bounds on PUT /profile ([#894](https://github.com/lifekit-hq/finance-sentry/issues/894)) ([76f2f58](https://github.com/lifekit-hq/finance-sentry/commit/76f2f589839745cf2469423e9873d02dcfd20176))
* **bank-sync:** bind transaction from/to date filters as UTC so the ledger query stops 500ing ([#1022](https://github.com/lifekit-hq/finance-sentry/issues/1022)) ([cc9aefc](https://github.com/lifekit-hq/finance-sentry/commit/cc9aefcd96d51cdcf18129b51ba173c0d132f014))
* **bank-sync:** categorize incoming MCC 4829 transfers as TRANSFER_IN ([#1087](https://github.com/lifekit-hq/finance-sentry/issues/1087)) ([d690a7c](https://github.com/lifekit-hq/finance-sentry/commit/d690a7c04d33342b19e33468c73750b20d0c3e83))
* **bank-sync:** claim the account atomically before a sync ([#983](https://github.com/lifekit-hq/finance-sentry/issues/983)) ([ec8f496](https://github.com/lifekit-hq/finance-sentry/commit/ec8f496e04921119ce9ac8563b584690a6a02ea0))
* **bank-sync:** classify sync failures by type and tailor alert copy ([#877](https://github.com/lifekit-hq/finance-sentry/issues/877)) ([f73a34b](https://github.com/lifekit-hq/finance-sentry/commit/f73a34be8f2a7e0ebe336faf3f66c5d03c4e9fa0))
* **bank-sync:** open every Top spendings category in the ledger with its transactions ([#889](https://github.com/lifekit-hq/finance-sentry/issues/889)) ([bde6a94](https://github.com/lifekit-hq/finance-sentry/commit/bde6a946992d0a9a18a985740321149b0e0d4d57))
* **bank-sync:** open transactions with the account preselected from an accounts row ([#909](https://github.com/lifekit-hq/finance-sentry/issues/909)) ([3eb577f](https://github.com/lifekit-hq/finance-sentry/commit/3eb577fdc1305e7780a92696fb6974b77fe6624b))
* **bank-sync:** poll sync status via @lifekit-hq/core poll() so it stops on unsubscribe ([#985](https://github.com/lifekit-hq/finance-sentry/issues/985)) ([73ce1cd](https://github.com/lifekit-hq/finance-sentry/commit/73ce1cdd64d5c1b8961ce7b6a32389dc6af2851c))
* **bank-sync:** remove sync jobs of inactive accounts; let Hangfire retry Inzhur reads ([#1004](https://github.com/lifekit-hq/finance-sentry/issues/1004)) ([a0ab5ed](https://github.com/lifekit-hq/finance-sentry/commit/a0ab5ed0bf1397f1b396128f3ce8b15ffd453056)), closes [#941](https://github.com/lifekit-hq/finance-sentry/issues/941)
* **bank-sync:** report reauth_required and expired connections in get_sync_health; sync housekeeping ([#875](https://github.com/lifekit-hq/finance-sentry/issues/875)) ([279edb4](https://github.com/lifekit-hq/finance-sentry/commit/279edb44a6cbd0b191a1433f802336adfe203907))
* **bank-sync:** resolve the replaced TrueLayer connection's reconnect alert after a successful reconnect ([#1084](https://github.com/lifekit-hq/finance-sentry/issues/1084)) ([8afba15](https://github.com/lifekit-hq/finance-sentry/commit/8afba15dcafd466e17671f087ada4e6171c45232))
* **bank-sync:** restore the pre-884 transactions filters and account chips ([#906](https://github.com/lifekit-hq/finance-sentry/issues/906)) ([38ecd54](https://github.com/lifekit-hq/finance-sentry/commit/38ecd54815a32c062077afc11d9c92aaef2c5b98))
* **brokerage-sync:** end the daily IBKR Flex window yesterday and stop hiding 1003 ([#1093](https://github.com/lifekit-hq/finance-sentry/issues/1093)) ([6fb2246](https://github.com/lifekit-hq/finance-sentry/commit/6fb2246557624088a9c39a01d91edc309971d523))
* **brokerage-sync:** keep the Inzhur session alive hourly, apart from the daily read ([#1067](https://github.com/lifekit-hq/finance-sentry/issues/1067)) ([9e17da0](https://github.com/lifekit-hq/finance-sentry/commit/9e17da0b3de57c413d82d4f52599a438a78b1989))
* **brokerage:** correct Inzhur kopeck amounts and backfill its ledger history ([#1065](https://github.com/lifekit-hq/finance-sentry/issues/1065)) ([28d74f8](https://github.com/lifekit-hq/finance-sentry/commit/28d74f897dffda59d83c18a6eb22af65c675ed05))
* **brokerage:** show Inzhur holdings in their own currency (UAH) ([#1044](https://github.com/lifekit-hq/finance-sentry/issues/1044)) ([c3e1e0f](https://github.com/lifekit-hq/finance-sentry/commit/c3e1e0f34fcd0ddd47fc0df2ee57ea54b4867935))
* **companion:** never relay a resolved SyncFailure ([#876](https://github.com/lifekit-hq/finance-sentry/issues/876)) ([58bb38a](https://github.com/lifekit-hq/finance-sentry/commit/58bb38acb04b99b5f731302aeda924c0023ee369))
* **companion:** use the real gateway hook token for the realtime push ([#643](https://github.com/lifekit-hq/finance-sentry/issues/643)) ([#904](https://github.com/lifekit-hq/finance-sentry/issues/904)) ([b859a1a](https://github.com/lifekit-hq/finance-sentry/commit/b859a1acdc6dc714d5d97ca7d62fbbcc4333ebe7))
* **connections:** pause scheduled syncs on ActionRequired connections, probing daily ([#1096](https://github.com/lifekit-hq/finance-sentry/issues/1096)) ([271c1ed](https://github.com/lifekit-hq/finance-sentry/commit/271c1ed8b1a165360aa03f0bcba2a608cc59bbfb))
* **deploy:** rename prod compose project to finance-sentry, keeping the docker_* volumes ([#989](https://github.com/lifekit-hq/finance-sentry/issues/989)) ([1bdcabf](https://github.com/lifekit-hq/finance-sentry/commit/1bdcabfa3bc09266270dbbcd57573ea6adfa98eb))
* **deploy:** wait out the gateway's reactivation and keep the live smoke to one sign-in ([#990](https://github.com/lifekit-hq/finance-sentry/issues/990)) ([b598bbd](https://github.com/lifekit-hq/finance-sentry/commit/b598bbd6141d0b41f25c4597ad543ab4b34fa421))
* **frontend:** AA contrast for settings avatar and resolved alerts; no empty chart while loading ([#1032](https://github.com/lifekit-hq/finance-sentry/issues/1032)) ([351baf2](https://github.com/lifekit-hq/finance-sentry/commit/351baf2fee2a0072dc1987b794d52a9e687eb328))
* **frontend:** consume shared theme bootstrap and ThemeService from lifekit-common ([#888](https://github.com/lifekit-hq/finance-sentry/issues/888)) ([5b16c9e](https://github.com/lifekit-hq/finance-sentry/commit/5b16c9eeb98fd2cd3ca8a5288d45dfe128e50991))
* **frontend:** generate whats-new.json in the compose dev frontend ([#1002](https://github.com/lifekit-hq/finance-sentry/issues/1002)) ([fe3e6f3](https://github.com/lifekit-hq/finance-sentry/commit/fe3e6f3e322080db856bbd715bffa0977594ca97))
* **frontend:** group ledger transactions by viewer-local calendar day ([#845](https://github.com/lifekit-hq/finance-sentry/issues/845)) ([cc4dba6](https://github.com/lifekit-hq/finance-sentry/commit/cc4dba643af86d2fc4f2a9853e5a5f4fc27ba22b))
* **frontend:** honest loading, error and offline states on dashboard, accounts, ledger and login ([#973](https://github.com/lifekit-hq/finance-sentry/issues/973)) ([4a652d3](https://github.com/lifekit-hq/finance-sentry/commit/4a652d34f4e24bec109e101492fd635848021fce))
* **frontend:** keep last content above mobile tab bar, fix alerts filter and subscriptions sort ([#1089](https://github.com/lifekit-hq/finance-sentry/issues/1089)) ([cdcbba7](https://github.com/lifekit-hq/finance-sentry/commit/cdcbba7cd770eb3943a5b968bae607739aa60325))
* **frontend:** keep the dashboard hero a constant height across ranges and loading ([#979](https://github.com/lifekit-hq/finance-sentry/issues/979)) ([4ed2f81](https://github.com/lifekit-hq/finance-sentry/commit/4ed2f81344abc6550ea0a0039fc3016056464b65))
* **frontend:** let the service worker fetch the logo hosts img-src admits ([#921](https://github.com/lifekit-hq/finance-sentry/issues/921)) ([98426cd](https://github.com/lifekit-hq/finance-sentry/commit/98426cd501663622724578921936f427e6a86690))
* **frontend:** match mobile keyboards to the field with inputmode, enterkeyhint and autocomplete ([#893](https://github.com/lifekit-hq/finance-sentry/issues/893)) ([e3490c0](https://github.com/lifekit-hq/finance-sentry/commit/e3490c0be9d159149bbf9a9b77a5fd769316d86e))
* **frontend:** neutral non-status UI accents and identity-keyed category colours ([#1046](https://github.com/lifekit-hq/finance-sentry/issues/1046)) ([8367e26](https://github.com/lifekit-hq/finance-sentry/commit/8367e26f618e2388586b60303b13a6a9c0e38d31))
* **frontend:** render remaining loading, error and empty states through cmn-async-state ([#1025](https://github.com/lifekit-hq/finance-sentry/issues/1025)) ([275f612](https://github.com/lifekit-hq/finance-sentry/commit/275f6127544f876ffde0bcc927a8106351ea1d6c))
* **frontend:** replace role=button rows with native buttons stretched over the row ([#1009](https://github.com/lifekit-hq/finance-sentry/issues/1009)) ([9acd07d](https://github.com/lifekit-hq/finance-sentry/commit/9acd07dd14e1c4f9aa4213df3d13a2c4b6ae366e))
* **frontend:** run unit tests headless by default ([#883](https://github.com/lifekit-hq/finance-sentry/issues/883)) ([11e40a9](https://github.com/lifekit-hq/finance-sentry/commit/11e40a973db5b50475e8ade46d1cff5371a90dc4))
* **frontend:** serve precompressed assets and gate cold-load LCP ([#881](https://github.com/lifekit-hq/finance-sentry/issues/881)) ([19c123f](https://github.com/lifekit-hq/finance-sentry/commit/19c123ff82e7a1104a0e61dd4a8f2ac1af6c5eb1))
* **frontend:** show dashboard totals in the profile base currency instead of hard-coded USD ([#895](https://github.com/lifekit-hq/finance-sentry/issues/895)) ([47e5674](https://github.com/lifekit-hq/finance-sentry/commit/47e56747362d33cdf39b4a3f18c15adc9b63f8e7))
* **frontend:** take the Playwright webServer port from E2E_PORT so concurrent local e2e runs don't collide ([#1068](https://github.com/lifekit-hq/finance-sentry/issues/1068)) ([981ad26](https://github.com/lifekit-hq/finance-sentry/commit/981ad26711bdf115b49bbb3c343575ac9cfe7a58))
* **frontend:** use the shared lifekit relative-time formatter ([#915](https://github.com/lifekit-hq/finance-sentry/issues/915)) ([b595575](https://github.com/lifekit-hq/finance-sentry/commit/b595575aea0e0445f12e131fb17869858c1c21af))
* **gateway:** readable 429 and separate session-bootstrap rate limit ([#1088](https://github.com/lifekit-hq/finance-sentry/issues/1088)) ([7fe5552](https://github.com/lifekit-hq/finance-sentry/commit/7fe5552d53f7a10d395cd59891bf7527e7e547be))
* **holdings:** count broker cash as Cash, not Equities, and keep it out of the palette ([#974](https://github.com/lifekit-hq/finance-sentry/issues/974)) ([cdabbfa](https://github.com/lifekit-hq/finance-sentry/commit/cdabbfa609f4dc98044163daa678a9b6737a69f2))
* **pwa:** align browser chrome with the lifekit standard and gate it in CI ([#928](https://github.com/lifekit-hq/finance-sentry/issues/928)) ([a207d43](https://github.com/lifekit-hq/finance-sentry/commit/a207d434b126fe0f1a495723ed94042b09eac7ce))
* **radar:** measure book return vs SPY on invested brokerage, cash excluded ([#996](https://github.com/lifekit-hq/finance-sentry/issues/996)) ([8ecb013](https://github.com/lifekit-hq/finance-sentry/commit/8ecb0133a3fbc2984998f25b8c53e33243475649))
* **research:** filter the asset dossier's next earnings to earnings events ([#1072](https://github.com/lifekit-hq/finance-sentry/issues/1072)) ([e6a36f4](https://github.com/lifekit-hq/finance-sentry/commit/e6a36f437fa88d049d2921e66b6acee5eb4a6ef8))
* **research:** map jsonb list columns as EF collections and filter tickers/theses in SQL ([#1018](https://github.com/lifekit-hq/finance-sentry/issues/1018)) ([a4b035d](https://github.com/lifekit-hq/finance-sentry/commit/a4b035d44bb333d0c4892bd386bafb2d0adc3f35))
* **research:** parse TrendForce dates as UTC regardless of host zone ([#880](https://github.com/lifekit-hq/finance-sentry/issues/880)) ([39066c8](https://github.com/lifekit-hq/finance-sentry/commit/39066c8558ce3ace1b293f200c1c1e479f29a0d1))
* **research:** read asset dossier sections sequentially so sections stop vanishing ([#1070](https://github.com/lifekit-hq/finance-sentry/issues/1070)) ([28c972a](https://github.com/lifekit-hq/finance-sentry/commit/28c972a9a1198d6da2a5023e47b4d23bf72eab64))
* **research:** unique partial index on Active opportunity candidates ([#1066](https://github.com/lifekit-hq/finance-sentry/issues/1066)) ([daf5bdc](https://github.com/lifekit-hq/finance-sentry/commit/daf5bdc4fa0221de75a69dbfb304e0298f8955ed))
* **settings:** remove dead Email and 2FA edits, state the low-balance currency, land on /dashboard ([#1076](https://github.com/lifekit-hq/finance-sentry/issues/1076)) ([a3d0aa1](https://github.com/lifekit-hq/finance-sentry/commit/a3d0aa1eeee8449845a7c15d1cc26dcc8c85c2c2))
* **settings:** validate FIRE assumptions with reactive forms and toast on the server result ([#1021](https://github.com/lifekit-hq/finance-sentry/issues/1021)) ([0fe31be](https://github.com/lifekit-hq/finance-sentry/commit/0fe31be692fb2e8b0a19cdb17dc26ae41acd6160))
* **wealth,mcp:** tag cash in the accounts list, get_account_summary and get_tax_lots ([#997](https://github.com/lifekit-hq/finance-sentry/issues/997)) ([520c60f](https://github.com/lifekit-hq/finance-sentry/commit/520c60f5367652fd0bb8c2eb55d29b5835a6ba2d))
* **wealth:** correct net-worth history inflated by pre-fix Inzhur kopeck values ([#1079](https://github.com/lifekit-hq/finance-sentry/issues/1079)) ([0b46321](https://github.com/lifekit-hq/finance-sentry/commit/0b46321bba647f0a425998ca90ea8bd9c2ca187d))
* **wealth:** restore net-worth rows M005 dipped before the Inzhur inflation began ([#1081](https://github.com/lifekit-hq/finance-sentry/issues/1081)) ([7ead9e0](https://github.com/lifekit-hq/finance-sentry/commit/7ead9e00a8b74cc581b729ddbcc3441bbf98fc09))
* **wealth:** show and open a position's full ticker instead of its four-character cut ([#1095](https://github.com/lifekit-hq/finance-sentry/issues/1095)) ([a62b462](https://github.com/lifekit-hq/finance-sentry/commit/a62b46216a11f17322d32062466413f2fe8b4fa3))
* **wealth:** show the FIRE card in the profile base currency ([#897](https://github.com/lifekit-hq/finance-sentry/issues/897)) ([26f9bf4](https://github.com/lifekit-hq/finance-sentry/commit/26f9bf44bf781ac564d741e5c2860266ce3671ae))


### Performance

* **backend:** replace load-then-loop writes with ExecuteUpdate/ExecuteDelete ([#1005](https://github.com/lifekit-hq/finance-sentry/issues/1005)) ([71a36d3](https://github.com/lifekit-hq/finance-sentry/commit/71a36d32c4a9f2c517fd37365e25df33e976de9d))
* **tooling:** speed up no-mistakes pre-check (NuGet volume, 2-slot lock) ([#903](https://github.com/lifekit-hq/finance-sentry/issues/903)) ([d56fde2](https://github.com/lifekit-hq/finance-sentry/commit/d56fde2b0d90424e24da194ef4601f92985f8778))


### Refactoring

* **agent:** trim Ledger persona under OpenClaw's 20k bootstrap limit ([#968](https://github.com/lifekit-hq/finance-sentry/issues/968)) ([60b35a0](https://github.com/lifekit-hq/finance-sentry/commit/60b35a08c966d1d544d07c7f3b194f30fdc03542))
* **agent:** use the built-in SSE writer and parser ([#946](https://github.com/lifekit-hq/finance-sentry/issues/946)) ([#1020](https://github.com/lifekit-hq/finance-sentry/issues/1020)) ([a76d767](https://github.com/lifekit-hq/finance-sentry/commit/a76d767d224bf9f244e4ec370018b26858cc8be7))
* **alerts:** move alerts store onto withAsyncStatus ([#1064](https://github.com/lifekit-hq/finance-sentry/issues/1064)) ([ea1264c](https://github.com/lifekit-hq/finance-sentry/commit/ea1264cac7890fc9bca3f2e0afff3fe225507e64))
* **api:** handle API errors with IExceptionHandler instead of the BankSync middleware ([#932](https://github.com/lifekit-hq/finance-sentry/issues/932)) ([#1013](https://github.com/lifekit-hq/finance-sentry/issues/1013)) ([7d020c8](https://github.com/lifekit-hq/finance-sentry/commit/7d020c84da6f9b0f0d8c0e4fe59c76072ba68e2a))
* **assets:** move dossier store onto withNamedAsyncStatus ([#1059](https://github.com/lifekit-hq/finance-sentry/issues/1059)) ([22fb95c](https://github.com/lifekit-hq/finance-sentry/commit/22fb95c748c182f412289517b2b9793ecb46cdd3))
* **auth:** apply persistence conventions and shared design-time factory to AuthDbContext ([#947](https://github.com/lifekit-hq/finance-sentry/issues/947)) ([#1047](https://github.com/lifekit-hq/finance-sentry/issues/1047)) ([1e9be28](https://github.com/lifekit-hq/finance-sentry/commit/1e9be2838dfa028abec35772e705d6dbc1c326a1))
* **auth:** bind Jwt through validated JwtOptions and share one token validation setup with the MCP host (part of [#940](https://github.com/lifekit-hq/finance-sentry/issues/940)) ([#1016](https://github.com/lifekit-hq/finance-sentry/issues/1016)) ([b2d9632](https://github.com/lifekit-hq/finance-sentry/commit/b2d9632866e325bad23146486dfc9cb467d55aa8))
* **auth:** mark auth requests with HttpContext tokens and share one in-flight refresh ([#957](https://github.com/lifekit-hq/finance-sentry/issues/957)) ([#1011](https://github.com/lifekit-hq/finance-sentry/issues/1011)) ([3bfae93](https://github.com/lifekit-hq/finance-sentry/commit/3bfae93695b9603d40760867dd232f97ce625c01))
* **auth:** remove user-facing password endpoints, keep break-glass login flag ([#1014](https://github.com/lifekit-hq/finance-sentry/issues/1014)) ([c6c428f](https://github.com/lifekit-hq/finance-sentry/commit/c6c428f8625c0c9337e14cf4a4e93ea8a1872e4d))
* **backend:** convert Budgets, Events, Subscriptions, Risk and Retention contexts to OwnerScopedDbContext ([#1038](https://github.com/lifekit-hq/finance-sentry/issues/1038)) ([db68875](https://github.com/lifekit-hq/finance-sentry/commit/db6887546e09b30c61b3c6771a693c21ca897f02))
* **backend:** scope Agent, Alerts, Analytics, BankSync, BrokerageSync and CryptoSync DbContexts through OwnerScopedDbContext ([#1041](https://github.com/lifekit-hq/finance-sentry/issues/1041)) ([fe1bde3](https://github.com/lifekit-hq/finance-sentry/commit/fe1bde368e979e907b7652cfc6ffe648efee8a0f))
* **backend:** scope Companion, Radar and Research contexts through shared owner-scoped base ([#1045](https://github.com/lifekit-hq/finance-sentry/issues/1045)) ([e170820](https://github.com/lifekit-hq/finance-sentry/commit/e170820a30b661cc4dbe4b600f17937a2d92787f))
* **backend:** stamp UpdatedAt with a SaveChangesInterceptor ([#1007](https://github.com/lifekit-hq/finance-sentry/issues/1007)) ([586b0b5](https://github.com/lifekit-hq/finance-sentry/commit/586b0b5bea5bb4663006efa1df58617a073a0a57))
* **backend:** upsert Wealth, Events and Subscriptions rows with INSERT ... ON CONFLICT ([#1042](https://github.com/lifekit-hq/finance-sentry/issues/1042)) ([760f695](https://github.com/lifekit-hq/finance-sentry/commit/760f695f2be964e332490d927084c98334128ad3))
* **bank-sync:** drop the unused audit_logs table and its retention policy ([#1055](https://github.com/lifekit-hq/finance-sentry/issues/1055)) ([c2226a7](https://github.com/lifekit-hq/finance-sentry/commit/c2226a79a2c54b95101f202fd0cf21f4f1244bbc))
* **bank-sync:** move FIRE store onto withAsyncStatus ([#1054](https://github.com/lifekit-hq/finance-sentry/issues/1054)) ([0d25dbc](https://github.com/lifekit-hq/finance-sentry/commit/0d25dbc82d7045c2cf7331b2cfd8a52dfac6d605))
* **bank-sync:** move flow-breakdown store onto withAsyncStatus ([#1058](https://github.com/lifekit-hq/finance-sentry/issues/1058)) ([2135d7c](https://github.com/lifekit-hq/finance-sentry/commit/2135d7cc082aece197277282f52c96cfedd3d679))
* **bank-sync:** move transaction-ledger store onto withAsyncStatus ([#1063](https://github.com/lifekit-hq/finance-sentry/issues/1063)) ([8ae4237](https://github.com/lifekit-hq/finance-sentry/commit/8ae4237c4f5acb7ac9f28ef5b1618ca879441877))
* **bank-sync:** named IsActive query filter on BankAccount ([#943](https://github.com/lifekit-hq/finance-sentry/issues/943)) ([#1006](https://github.com/lifekit-hq/finance-sentry/issues/1006)) ([adbfccc](https://github.com/lifekit-hq/finance-sentry/commit/adbfcccaa33e3176d1e7d63a903ce2866371a784))
* **bank-sync:** pin a committed merchant with INSERT ... ON CONFLICT ([#1037](https://github.com/lifekit-hq/finance-sentry/issues/1037)) ([3a60f8f](https://github.com/lifekit-hq/finance-sentry/commit/3a60f8f258837d582b39a2c09a23a836f27862c0))
* **bank-sync:** sync dashboard and flow-breakdown query params through withUrlSync (part of [#955](https://github.com/lifekit-hq/finance-sentry/issues/955)) ([#1024](https://github.com/lifekit-hq/finance-sentry/issues/1024)) ([3c75205](https://github.com/lifekit-hq/finance-sentry/commit/3c75205af89f20f380d9ab6362b51aa8f36f780f))
* **bank-sync:** sync transaction ledger query params through withUrlSync (closes [#955](https://github.com/lifekit-hq/finance-sentry/issues/955)) ([#1026](https://github.com/lifekit-hq/finance-sentry/issues/1026)) ([2c623f0](https://github.com/lifekit-hq/finance-sentry/commit/2c623f06875a999afa2b2e6c12d268ec3641a32d))
* **budgets:** move budgets store onto withAsyncStatus ([#1062](https://github.com/lifekit-hq/finance-sentry/issues/1062)) ([94528d8](https://github.com/lifekit-hq/finance-sentry/commit/94528d84d643ea038d1fa9381f74e36bab6681c8))
* **caches:** bound provider caches with a size limit and drive expiry from TimeProvider ([#1023](https://github.com/lifekit-hq/finance-sentry/issues/1023)) ([cf91308](https://github.com/lifekit-hq/finance-sentry/commit/cf91308ce7425a142089722863a6880004f413c8))
* **companion:** upsert events, settings, push subscriptions and watermarks with INSERT ... ON CONFLICT ([#936](https://github.com/lifekit-hq/finance-sentry/issues/936)) ([#1040](https://github.com/lifekit-hq/finance-sentry/issues/1040)) ([1bd6a98](https://github.com/lifekit-hq/finance-sentry/commit/1bd6a9855b26dcec38315cb6bdc8541ecf125d46))
* **crypto-sync:** upsert holdings with INSERT ... ON CONFLICT ([#936](https://github.com/lifekit-hq/finance-sentry/issues/936)) ([#1015](https://github.com/lifekit-hq/finance-sentry/issues/1015)) ([f70c5b9](https://github.com/lifekit-hq/finance-sentry/commit/f70c5b93abfaca5710a3bdbfa6b6a7cafca42db7))
* **events:** move events store onto withNamedAsyncStatus ([#1060](https://github.com/lifekit-hq/finance-sentry/issues/1060)) ([25ea125](https://github.com/lifekit-hq/finance-sentry/commit/25ea125ea7493ee4e42421ed81d44b8adff33ef1))
* **frontend:** adopt cmn-async-state on events and truelayer picker ([#901](https://github.com/lifekit-hq/finance-sentry/issues/901)) ([f12c434](https://github.com/lifekit-hq/finance-sentry/commit/f12c43442e3358c84c7dfc2ed9cf46635463c75f))
* **frontend:** adopt cmn-tab-group for accounts and events tabs ([#892](https://github.com/lifekit-hq/finance-sentry/issues/892)) ([b535426](https://github.com/lifekit-hq/finance-sentry/commit/b535426c6d3914a63f2981cd7cf1b99e09636cb9))
* **frontend:** adopt lifekit-common 1.0.0 app-layout ([#871](https://github.com/lifekit-hq/finance-sentry/issues/871)) ([be41391](https://github.com/lifekit-hq/finance-sentry/commit/be413910d08ae9ecdcdfa9e03e6f195f5e2b1dda))
* **frontend:** consolidate date keys and labels on formatDate with one app locale ([#1029](https://github.com/lifekit-hq/finance-sentry/issues/1029)) ([992752c](https://github.com/lifekit-hq/finance-sentry/commit/992752cb99f42efabd017ae2cde59126f7a3ca14))
* **frontend:** move holdings, dossier, dashboard, flow breakdown, accounts and ledger onto cmn-async-state ([#967](https://github.com/lifekit-hq/finance-sentry/issues/967)) ([d74c744](https://github.com/lifekit-hq/finance-sentry/commit/d74c74444efb8c5bcef036f8b935335ba792cdab))
* **frontend:** take base.css and cmn-page-container from lifekit-common ([#929](https://github.com/lifekit-hq/finance-sentry/issues/929)) ([a2ea709](https://github.com/lifekit-hq/finance-sentry/commit/a2ea709905319e5bfb56c416d570820117d54b80))
* **frontend:** use extractErrorCode from @lifekit-hq/core ([#1003](https://github.com/lifekit-hq/finance-sentry/issues/1003)) ([d39f406](https://github.com/lifekit-hq/finance-sentry/commit/d39f406ed0cab2ed803c856c14c7b4f4746f4510))
* **frontend:** use lk-segmented for single-choice pickers in alerts and subscriptions ([#1010](https://github.com/lifekit-hq/finance-sentry/issues/1010)) ([3c5fecd](https://github.com/lifekit-hq/finance-sentry/commit/3c5fecd57883346f7fa2a4243aa11ea9cf0243b3))
* **holdings:** move holdings store onto withAsyncStatus ([#1053](https://github.com/lifekit-hq/finance-sentry/issues/1053)) ([41e69d4](https://github.com/lifekit-hq/finance-sentry/commit/41e69d48703a965d2d82b4c531dfb96a5c610c06))
* **infrastructure:** add OwnerScopedDbContext base with owner filter and shared column conventions (part of [#947](https://github.com/lifekit-hq/finance-sentry/issues/947)) ([#1019](https://github.com/lifekit-hq/finance-sentry/issues/1019)) ([d2a649b](https://github.com/lifekit-hq/finance-sentry/commit/d2a649b5ab757e5d6ce36cf8347b5ad381e59dae))
* **infrastructure:** adopt Microsoft.Extensions.Http.Resilience for provider HTTP retries and rate limits ([#1017](https://github.com/lifekit-hq/finance-sentry/issues/1017)) ([45db6f5](https://github.com/lifekit-hq/finance-sentry/commit/45db6f50267715cdce0a01daf92c9a89303f2f82))
* **observability:** drop correlation-id layer and TraceEnricher for W3C trace ids ([#1048](https://github.com/lifekit-hq/finance-sentry/issues/1048)) ([50ae616](https://github.com/lifekit-hq/finance-sentry/commit/50ae61616d90f67a117337c8073b10afffc12989))
* **observability:** remove in-app consecutive-failure alerting in favour of the Grafana rule ([#1035](https://github.com/lifekit-hq/finance-sentry/issues/1035)) ([aef852f](https://github.com/lifekit-hq/finance-sentry/commit/aef852fd9a2fa02f7a95148e55516d74fe29b95e))
* **radar:** upsert daily bars and universe members with INSERT ... ON CONFLICT ([#936](https://github.com/lifekit-hq/finance-sentry/issues/936)) ([#1039](https://github.com/lifekit-hq/finance-sentry/issues/1039)) ([d1095bc](https://github.com/lifekit-hq/finance-sentry/commit/d1095bc3144cd596a2710706b44a7c27b74eeada))
* **research:** upsert Research rows with INSERT ... ON CONFLICT ([#1043](https://github.com/lifekit-hq/finance-sentry/issues/1043)) ([8c51247](https://github.com/lifekit-hq/finance-sentry/commit/8c512473716f5effe867a43dc44cff2054414eb4))
* **settings:** move Settings, Push and People stores onto withAsyncStatus ([#1050](https://github.com/lifekit-hq/finance-sentry/issues/1050)) ([5a376e4](https://github.com/lifekit-hq/finance-sentry/commit/5a376e4b76f0db54d7de105780240068609043ff))
* **subscriptions:** move commitment-picker store onto withAsyncStatus ([#1057](https://github.com/lifekit-hq/finance-sentry/issues/1057)) ([9a0fc53](https://github.com/lifekit-hq/finance-sentry/commit/9a0fc5338a1cbda4e0532f6102de119fb6a4a0b2))
* **subscriptions:** move subscriptions store onto named withAsyncStatus slices ([#1061](https://github.com/lifekit-hq/finance-sentry/issues/1061)) ([ccf4067](https://github.com/lifekit-hq/finance-sentry/commit/ccf40677f83fcfbc6d480686c707cf752f4e6382))
* **wealth:** wrap banking, brokerage and crypto readers as IWealthSource registrations ([#1102](https://github.com/lifekit-hq/finance-sentry/issues/1102)) ([6aa02b1](https://github.com/lifekit-hq/finance-sentry/commit/6aa02b1e358b1425dc54a953b5ee7dd54c28102b))
* **whats-new:** move whats-new store onto withAsyncStatus ([#1056](https://github.com/lifekit-hq/finance-sentry/issues/1056)) ([f27715a](https://github.com/lifekit-hq/finance-sentry/commit/f27715aa15ad318d4cf9ae2b858b94ac4a3a0c99))


### Documentation

* **040:** fold live-only OpenClaw Ledger persona changes back ([#416](https://github.com/lifekit-hq/finance-sentry/issues/416)) ([#900](https://github.com/lifekit-hq/finance-sentry/issues/900)) ([5a3999e](https://github.com/lifekit-hq/finance-sentry/commit/5a3999eef65956f505b3f522921f2efd4c873230))
* **040:** fold post-[#900](https://github.com/lifekit-hq/finance-sentry/issues/900) live Ledger persona changes back ([#416](https://github.com/lifekit-hq/finance-sentry/issues/416)) ([#1078](https://github.com/lifekit-hq/finance-sentry/issues/1078)) ([b3a9e2a](https://github.com/lifekit-hq/finance-sentry/commit/b3a9e2a903d53deb28f37e4ae9617b115ccdc265))
* **040:** mark Ledger persona agent-as-code deploy as done ([#1080](https://github.com/lifekit-hq/finance-sentry/issues/1080)) ([f6e7bfb](https://github.com/lifekit-hq/finance-sentry/commit/f6e7bfb1b3ee56b0238813ec599e855754954a34))
* **726:** record the owner's 2026-10-07 answers, close OQ-1..OQ-10 ([#963](https://github.com/lifekit-hq/finance-sentry/issues/963)) ([0478508](https://github.com/lifekit-hq/finance-sentry/commit/047850886681e2efb93b6f1269406916bdae84a1))
* **859:** spec Web Push notifications, amending 031 FR-015/SC-007 ([#860](https://github.com/lifekit-hq/finance-sentry/issues/860)) ([6fdb929](https://github.com/lifekit-hq/finance-sentry/commit/6fdb9293584c29513384a89a2336d1fd829578b9))
* **agents:** add Platform first section ([#855](https://github.com/lifekit-hq/finance-sentry/issues/855)) ([837479c](https://github.com/lifekit-hq/finance-sentry/commit/837479c5f977beaa5fc8b3f8de193d6d65e1c716))
* **agents:** apply the backpass trial - app-state refresh moves to the periodic pass, drop stale sections, gate runs touched e2e specs, corrections-only document note ([#978](https://github.com/lifekit-hq/finance-sentry/issues/978)) ([5dbc740](https://github.com/lifekit-hq/finance-sentry/commit/5dbc740041c74f9682ede468167d0c121bbda479))
* **agents:** merge CLAUDE.md into AGENTS.md as the single instruction file ([#850](https://github.com/lifekit-hq/finance-sentry/issues/850)) ([2a365d6](https://github.com/lifekit-hq/finance-sentry/commit/2a365d64dddcbbe6170a2ddc38579d771b9703c1))
* **finance:** relabel Book vs SPY as change in invested value; add finance framework doc ([#1094](https://github.com/lifekit-hq/finance-sentry/issues/1094)) ([8de2323](https://github.com/lifekit-hq/finance-sentry/commit/8de2323229fc2dc1b64aa48712300ed198144659))
* **spec:** add 726 portfolio construction spec and plan ([#902](https://github.com/lifekit-hq/finance-sentry/issues/902)) ([fb4533e](https://github.com/lifekit-hq/finance-sentry/commit/fb4533e4850d68583536a639d6d34d986b080a14)), closes [#726](https://github.com/lifekit-hq/finance-sentry/issues/726)

## [1.14.0](https://github.com/lifekit-hq/finance-sentry/compare/v1.13.0...v1.14.0) (2026-10-05)


### Features

* **agent:** restrict agent chat and Ledger write-ups to the Owner role ([#757](https://github.com/lifekit-hq/finance-sentry/issues/757)) ([f36e0c9](https://github.com/lifekit-hq/finance-sentry/commit/f36e0c9d7aa6d255b63e0eefc54b9970582228a3))
* **alerts:** scope alerts to the current user with a named Owner query filter ([#773](https://github.com/lifekit-hq/finance-sentry/issues/773)) ([34edf43](https://github.com/lifekit-hq/finance-sentry/commit/34edf43888c228274a7c16853834c7a4213e40fa))
* **api:** attach rate-limit policies and require hostname/edge config ([#834](https://github.com/lifekit-hq/finance-sentry/issues/834)) ([e927c64](https://github.com/lifekit-hq/finance-sentry/commit/e927c646986ee15f3a6747535e6902ece08ef9dd))
* **auth:** add Owner/Member roles with permission claims and frontend guards ([#770](https://github.com/lifekit-hq/finance-sentry/issues/770)) ([90d47e1](https://github.com/lifekit-hq/finance-sentry/commit/90d47e19e8ec1a4cb9ab7708231e148b7dbf18e0))
* **auth:** invite-only onboarding with an owner People page ([#776](https://github.com/lifekit-hq/finance-sentry/issues/776)) ([b83c762](https://github.com/lifekit-hq/finance-sentry/commit/b83c762b57dcfc4aae1b98947b85722ec9e09c60))
* **auth:** move the API host to the stock ASP.NET Core authentication pipeline ([#755](https://github.com/lifekit-hq/finance-sentry/issues/755)) ([774419a](https://github.com/lifekit-hq/finance-sentry/commit/774419aacbf74432d176c34bc3b41c3110e11d01))
* **auth:** scope refresh tokens and MCP authorization codes to the current user with the Owner query filter ([#781](https://github.com/lifekit-hq/finance-sentry/issues/781)) ([046f44b](https://github.com/lifekit-hq/finance-sentry/commit/046f44bd07bc6b3d5c94a94f539de7efe61e90cb))
* **auth:** seed a dedicated smoke account for the post-deploy live smoke ([#835](https://github.com/lifekit-hq/finance-sentry/issues/835)) ([423a654](https://github.com/lifekit-hq/finance-sentry/commit/423a6540b9f6582c347bf9f06f99814dcc0ef549))
* **auth:** sign in through the lifekit OIDC provider ([#828](https://github.com/lifekit-hq/finance-sentry/issues/828)) ([f9a8ba8](https://github.com/lifekit-hq/finance-sentry/commit/f9a8ba8f7e9a3a09ab77924b7eae1afbf757c5d9))
* **backend:** scope Agent, Analytics, Budgets, Companion, Events, Risk and Wealth with the Owner query filter ([#779](https://github.com/lifekit-hq/finance-sentry/issues/779)) ([264a928](https://github.com/lifekit-hq/finance-sentry/commit/264a928daef2a55f771fc5c1a0b6e2c977a551fc))
* **banksync:** scope BankSync, BrokerageSync and CryptoSync to the current user with the Owner query filter ([#775](https://github.com/lifekit-hq/finance-sentry/issues/775)) ([73a5d15](https://github.com/lifekit-hq/finance-sentry/commit/73a5d15e66bd828bedd0e7466a751c0eb0368cc9))
* **deploy:** deploy CI-built images by commit SHA ([#771](https://github.com/lifekit-hq/finance-sentry/issues/771)) ([b0deb80](https://github.com/lifekit-hq/finance-sentry/commit/b0deb8005bf80d5c33a647db7ae8b64d4325bb38))
* **frontend:** adopt [@lifekit-hq](https://github.com/lifekit-hq) 0.5.0 phone shell and table list rows ([#783](https://github.com/lifekit-hq/finance-sentry/issues/783)) ([6fa3d57](https://github.com/lifekit-hq/finance-sentry/commit/6fa3d570b9c7e55fffd390cd40d9eb16677bab2e))
* **frontend:** adopt the shared lifekit-common PWA prompts and update service ([#791](https://github.com/lifekit-hq/finance-sentry/issues/791)) ([2885a9f](https://github.com/lifekit-hq/finance-sentry/commit/2885a9f38c37f6568ebe9406f1f424f0c20bba93))
* **frontend:** day-grouped transactions, tidier holdings and asset dossier ([#787](https://github.com/lifekit-hq/finance-sentry/issues/787)) ([47404a4](https://github.com/lifekit-hq/finance-sentry/commit/47404a4b8cac99b9b50e2b3c42b7c5979011dc16))
* **frontend:** drop the header theme toggle from the app shell ([#821](https://github.com/lifekit-hq/finance-sentry/issues/821)) ([514d5dd](https://github.com/lifekit-hq/finance-sentry/commit/514d5dd33a0964da5e06917cfc0249dfea51b372))
* **frontend:** installable PWA app shell with API-safe service worker ([#778](https://github.com/lifekit-hq/finance-sentry/issues/778)) ([c670e01](https://github.com/lifekit-hq/finance-sentry/commit/c670e01db771468bb3b51497386e928bb2bb386a))
* **frontend:** login copy, Ledger sessions sheet on phone, avatar initial from signed-in user ([#789](https://github.com/lifekit-hq/finance-sentry/issues/789)) ([4e07bda](https://github.com/lifekit-hq/finance-sentry/commit/4e07bda316b705028950b35aab95ebe00dc792aa))
* **frontend:** shared money pipe, responsive stat grids, skeletons and library form controls ([#774](https://github.com/lifekit-hq/finance-sentry/issues/774)) ([0f351c4](https://github.com/lifekit-hq/finance-sentry/commit/0f351c4fc93a210064a6bba1aa1acaaf2612149f))
* **frontend:** simplify Dashboard and Accounts pages ([9eca32b](https://github.com/lifekit-hq/finance-sentry/commit/9eca32ba0f171444fb66fea86a3c8ca5d691a4b1))
* **frontend:** simplify Subscriptions, Budgets, Alerts and Events pages ([#788](https://github.com/lifekit-hq/finance-sentry/issues/788)) ([b6d2286](https://github.com/lifekit-hq/finance-sentry/commit/b6d22869be2cc0e16e801b1f58b2a6251b89a4aa))
* **gateway:** add security headers and a public listener limited to app and API ([#822](https://github.com/lifekit-hq/finance-sentry/issues/822)) ([8f47865](https://github.com/lifekit-hq/finance-sentry/commit/8f47865b678a6072ce907a50e41b0dc217b38bd5))
* **mcp:** move the MCP HTTP host to the stock ASP.NET Core authentication pipeline ([#760](https://github.com/lifekit-hq/finance-sentry/issues/760)) ([92b136c](https://github.com/lifekit-hq/finance-sentry/commit/92b136c4b84949fd15936dcd4483026a605e7014))
* **ops:** run the uptime probe as a compose service instead of operator cron ([#761](https://github.com/lifekit-hq/finance-sentry/issues/761)) ([6a2fc3e](https://github.com/lifekit-hq/finance-sentry/commit/6a2fc3e19096bcc7357919d3d9120c5e67ce88d8))
* **radar,subscriptions:** scope Radar and Subscriptions to the current user with the Owner query filter ([#780](https://github.com/lifekit-hq/finance-sentry/issues/780)) ([d5a3001](https://github.com/lifekit-hq/finance-sentry/commit/d5a30011bcccad0d975ad3c71d1092e0ba32171f))
* **research:** fire scheduled policy review on recorded cadence ([#765](https://github.com/lifekit-hq/finance-sentry/issues/765)) ([b6aa2df](https://github.com/lifekit-hq/finance-sentry/commit/b6aa2df80ea6ca76d63794031ab2a09ed94396e2))
* **research:** prove the look-ahead detectors on real events and report silent ones ([#698](https://github.com/lifekit-hq/finance-sentry/issues/698)) ([#764](https://github.com/lifekit-hq/finance-sentry/issues/764)) ([7668543](https://github.com/lifekit-hq/finance-sentry/commit/766854351b754800d9efa639d2aa3967d60ae5e6))
* **research:** re-measure risk tolerance and enforce drawdown tolerance ([#768](https://github.com/lifekit-hq/finance-sentry/issues/768)) ([e56bf46](https://github.com/lifekit-hq/finance-sentry/commit/e56bf464ba12f84086738e99f51f93d382fa8579))
* **research:** scope Research to the current user with the Owner query filter ([#777](https://github.com/lifekit-hq/finance-sentry/issues/777)) ([cd19dbc](https://github.com/lifekit-hq/finance-sentry/commit/cd19dbc5d633706228fd832fd8276893d1b014f8))
* **research:** surface a benchmark-relative track record ([#766](https://github.com/lifekit-hq/finance-sentry/issues/766)) ([e9f1f15](https://github.com/lifekit-hq/finance-sentry/commit/e9f1f153f226c4087a9441900f9402fcc4b231c7))


### Bug Fixes

* **agent:** move Ledger conversation delete behind a ••• menu ([#801](https://github.com/lifekit-hq/finance-sentry/issues/801)) ([6a86aa9](https://github.com/lifekit-hq/finance-sentry/commit/6a86aa90a4a78320bff9fda40fc08b2331ce2681))
* **alerts:** hide raw SEC URL in filing alerts and open filing in new tab ([#831](https://github.com/lifekit-hq/finance-sentry/issues/831)) ([489bdc8](https://github.com/lifekit-hq/finance-sentry/commit/489bdc8fccf6f41c25d52b37fea7b39f961cbd19))
* **alerts:** make a policy acknowledgement cover every alert derived from that policy ([#762](https://github.com/lifekit-hq/finance-sentry/issues/762)) ([15dbf12](https://github.com/lifekit-hq/finance-sentry/commit/15dbf12a00872790787227578091d370670c78d3))
* **analytics:** allowlist SQL guard functions and revoke set_config from PUBLIC ([#750](https://github.com/lifekit-hq/finance-sentry/issues/750)) ([baabb55](https://github.com/lifekit-hq/finance-sentry/commit/baabb55bc2a162a72ec223506da69abb40afa4f6))
* **api:** flush fatal unhandled exceptions to the log before the process dies ([#748](https://github.com/lifekit-hq/finance-sentry/issues/748)) ([16f3c39](https://github.com/lifekit-hq/finance-sentry/commit/16f3c39e58f4af496b87274d1de6b92aa4791ab9))
* **api:** register recurring jobs in a background service with lock-aware retry ([#746](https://github.com/lifekit-hq/finance-sentry/issues/746)) ([1e9d24b](https://github.com/lifekit-hq/finance-sentry/commit/1e9d24b56f6870f297f8dcb68ff6f33cf3878134))
* **assets:** plain-language labels and rendered markdown in asset dossier ([#799](https://github.com/lifekit-hq/finance-sentry/issues/799)) ([a9c23e7](https://github.com/lifekit-hq/finance-sentry/commit/a9c23e77e3bb49c607824efc179cabfdff71c5e0))
* **assets:** use surface-raised token for dossier rows in dark mode ([#806](https://github.com/lifekit-hq/finance-sentry/issues/806)) ([877fcb2](https://github.com/lifekit-hq/finance-sentry/commit/877fcb2195729be951a095e4f457058898f69f27))
* **auth:** remove no-audience token exception from API host ([#792](https://github.com/lifekit-hq/finance-sentry/issues/792)) ([4a5a64a](https://github.com/lifekit-hq/finance-sentry/commit/4a5a64a15d9684fe261f775cd3cc57e82dede1ab))
* **auth:** require Owner role for Hangfire dashboard and restrict forwarded-header trust ([#751](https://github.com/lifekit-hq/finance-sentry/issues/751)) ([1ca08e7](https://github.com/lifekit-hq/finance-sentry/commit/1ca08e7461ce5a7506584babb4ec9a3ba3cc6a15))
* **auth:** restyle MCP connect page with page header and card ([#811](https://github.com/lifekit-hq/finance-sentry/issues/811)) ([cd41af7](https://github.com/lifekit-hq/finance-sentry/commit/cd41af7d59706ab3a63919812669de0445c4b5cd))
* **auth:** tell a locked-out user the account is locked ([#819](https://github.com/lifekit-hq/finance-sentry/issues/819)) ([b9115ac](https://github.com/lifekit-hq/finance-sentry/commit/b9115acff14749b90d238adbc5b3d24938b3552a))
* **bank-sync:** bind the TrueLayer callback state to the initiating browser ([#752](https://github.com/lifekit-hq/finance-sentry/issues/752)) ([4a5a042](https://github.com/lifekit-hq/finance-sentry/commit/4a5a042ec65da53a881671fc6de80ae07b4aafe9))
* **bank-sync:** calm dashboard Month pace chips ([#793](https://github.com/lifekit-hq/finance-sentry/issues/793)) ([ba5ba03](https://github.com/lifekit-hq/finance-sentry/commit/ba5ba03897ae1969f5da841583149ee1500c813e))
* **bank-sync:** Flow breakdown month stepper, Saved label and category share bars ([#832](https://github.com/lifekit-hq/finance-sentry/issues/832)) ([a3566fe](https://github.com/lifekit-hq/finance-sentry/commit/a3566fef1c7559efdd7b26cf3edd488d075a1321))
* **bank-sync:** mark TrueLayer connection expired when refresh is rejected during discovery ([#785](https://github.com/lifekit-hq/finance-sentry/issues/785)) ([cd4e138](https://github.com/lifekit-hq/finance-sentry/commit/cd4e138bcde59b40bf4b5e14a63df6217a80f692))
* **bank-sync:** phone list rows, 16px gutter and 2x2 stats on Month breakdown ([#796](https://github.com/lifekit-hq/finance-sentry/issues/796)) ([fea0471](https://github.com/lifekit-hq/finance-sentry/commit/fea0471386365cb9401dd45e40d1f79b9163b541))
* **bank-sync:** show sync status as text on phone Accounts list ([#800](https://github.com/lifekit-hq/finance-sentry/issues/800)) ([d1fab3a](https://github.com/lifekit-hq/finance-sentry/commit/d1fab3a8c964b5f121ddd97dd9a30f4175ac8165))
* **bank-sync:** two-row month stat tiles on phone dashboard ([#803](https://github.com/lifekit-hq/finance-sentry/issues/803)) ([80aa7fb](https://github.com/lifekit-hq/finance-sentry/commit/80aa7fb551d71fe0e12babda45d3ae3b0347d646))
* **ci:** cap the no-mistakes backend test step's memory so it stops being OOM-killed ([#754](https://github.com/lifekit-hq/finance-sentry/issues/754)) ([179927e](https://github.com/lifekit-hq/finance-sentry/commit/179927ed57268a30c361e9bfd44e36fcbc26806a))
* **companion:** keep held-for-digest events out of the realtime pull ([#763](https://github.com/lifekit-hq/finance-sentry/issues/763)) ([76d75c4](https://github.com/lifekit-hq/finance-sentry/commit/76d75c4323f8e220d8688fd14e8b4cd9a83b7538))
* **docker:** set the real Google OAuth client id in the production secrets ([#818](https://github.com/lifekit-hq/finance-sentry/issues/818)) ([06b9133](https://github.com/lifekit-hq/finance-sentry/commit/06b913392faeed13828adfa248280a1b6f544af8))
* **events:** dedupe calendar day header and row titles ([#808](https://github.com/lifekit-hq/finance-sentry/issues/808)) ([54b9feb](https://github.com/lifekit-hq/finance-sentry/commit/54b9feb5ffdc8fee872dbb124dcf422d50d54bc8))
* **events:** stop outcome tag wrapping in Fired rows, mute not_delivered ([#802](https://github.com/lifekit-hq/finance-sentry/issues/802)) ([cdd70e1](https://github.com/lifekit-hq/finance-sentry/commit/cdd70e19808c3809ad2f81a1d0024bd5d9d56d0c))
* **frontend:** add All/In/Out type chips to Transactions ([#812](https://github.com/lifekit-hq/finance-sentry/issues/812)) ([7f3521e](https://github.com/lifekit-hq/finance-sentry/commit/7f3521e986340a4ed4d54bfb12a477b8c802afe1))
* **frontend:** adopt the phone overlay layout for the PWA shell ([#823](https://github.com/lifekit-hq/finance-sentry/issues/823)) ([a58b90a](https://github.com/lifekit-hq/finance-sentry/commit/a58b90a5e8d66c7e9adca6e560484cc981636863))
* **frontend:** apply stored and OS theme on guest pages ([#798](https://github.com/lifekit-hq/finance-sentry/issues/798)) ([4d5cbbc](https://github.com/lifekit-hq/finance-sentry/commit/4d5cbbcb47290bf57ece7d023556fe69128a5fa2))
* **frontend:** budgets over-budget copy reads "&lt;amount&gt; over · &lt;limit&gt; budget" ([#809](https://github.com/lifekit-hq/finance-sentry/issues/809)) ([0a6edf7](https://github.com/lifekit-hq/finance-sentry/commit/0a6edf7f0e6b2daa495262e284dac8542618691c))
* **frontend:** fit the phone PWA shell to the full viewport height ([#817](https://github.com/lifekit-hq/finance-sentry/issues/817)) ([53dfd41](https://github.com/lifekit-hq/finance-sentry/commit/53dfd41c110ffe2e906ef0aacbf0a88b5fe04a72))
* **frontend:** flatten Holdings group cards and shrink phone donut ([#815](https://github.com/lifekit-hq/finance-sentry/issues/815)) ([88094b0](https://github.com/lifekit-hq/finance-sentry/commit/88094b04b46fe91c09577825e54c414c8a94e1f2))
* **frontend:** header avatar uses profile name initials, falls back to email ([#813](https://github.com/lifekit-hq/finance-sentry/issues/813)) ([c8aefe5](https://github.com/lifekit-hq/finance-sentry/commit/c8aefe532f2ebfa87ab532eafe283d51c9eeaf93))
* **frontend:** label Later subscriptions and use ••• row menu ([#807](https://github.com/lifekit-hq/finance-sentry/issues/807)) ([56cdf53](https://github.com/lifekit-hq/finance-sentry/commit/56cdf533c5a1ae32c85f1e2651b53678f3b07a99))
* **frontend:** login phone gutter, sentence-case labels, Google button width ([#805](https://github.com/lifekit-hq/finance-sentry/issues/805)) ([bbe329d](https://github.com/lifekit-hq/finance-sentry/commit/bbe329dadc141d8afe77b0e83578a4536e2ebb14))
* **frontend:** make the active Accounts tab visibly distinct and aria-current ([#794](https://github.com/lifekit-hq/finance-sentry/issues/794)) ([a59b452](https://github.com/lifekit-hq/finance-sentry/commit/a59b452b53acd207284487206d439ebd65f15960))
* **frontend:** show Monobank, IBKR and Binance logos on Transactions rows ([#797](https://github.com/lifekit-hq/finance-sentry/issues/797)) ([cda2945](https://github.com/lifekit-hq/finance-sentry/commit/cda2945281abc974c2168e2fd232647f97c2fd0f))
* **frontend:** stop signed-out pages scrolling on iOS home-screen apps ([#827](https://github.com/lifekit-hq/finance-sentry/issues/827)) ([44746cd](https://github.com/lifekit-hq/finance-sentry/commit/44746cddb647ef400684ddc1b762ae49f7d7547c))
* **frontend:** stop the phone shell scrolling the document and clipping the tab bar ([#826](https://github.com/lifekit-hq/finance-sentry/issues/826)) ([1a69b7d](https://github.com/lifekit-hq/finance-sentry/commit/1a69b7d902bdd33dc40e021be03b56851f702296))
* **frontend:** use one shared page-container width across pages ([#814](https://github.com/lifekit-hq/finance-sentry/issues/814)) ([97495e7](https://github.com/lifekit-hq/finance-sentry/commit/97495e7601d922075699b9239690e2f9a9bc9bce))
* **mcp:** scope all MCP tools to the authenticated user ([#749](https://github.com/lifekit-hq/finance-sentry/issues/749)) ([8d2a9fb](https://github.com/lifekit-hq/finance-sentry/commit/8d2a9fbfc912f87b6df4cd38d72e39f26fd04b83))
* **research:** require 4 distinct stories for Google News thesis hits ([#790](https://github.com/lifekit-hq/finance-sentry/issues/790)) ([766e072](https://github.com/lifekit-hq/finance-sentry/commit/766e0724725d6d05401ea505cd9e436efa5c1ce5))
* **settings:** add back link to Settings and spacing on People page ([#810](https://github.com/lifekit-hq/finance-sentry/issues/810)) ([33b4511](https://github.com/lifekit-hq/finance-sentry/commit/33b45116dbf134b4888c5aeb6e43229218b490aa))
* **settings:** use labelled form fields and spacing on Settings page ([#804](https://github.com/lifekit-hq/finance-sentry/issues/804)) ([b560151](https://github.com/lifekit-hq/finance-sentry/commit/b560151e5b5f7166ff971e02c739d96163112287))
* **shell:** reserve clearance for the floating Ledger chat button ([#795](https://github.com/lifekit-hq/finance-sentry/issues/795)) ([2feebb0](https://github.com/lifekit-hq/finance-sentry/commit/2feebb05da0b13adce83d9120ddef59c7a4c82c7))


### Refactoring

* **auth:** make external sign-in provider-agnostic ([#820](https://github.com/lifekit-hq/finance-sentry/issues/820)) ([19e93a5](https://github.com/lifekit-hq/finance-sentry/commit/19e93a540258267ef184ddca116dd81d3153b6c6))
* **frontend:** use library controls for the last raw buttons and banner ([#830](https://github.com/lifekit-hq/finance-sentry/issues/830)) ([332ef36](https://github.com/lifekit-hq/finance-sentry/commit/332ef3630c9ff3816d9d40380dba18a39671be31))


### Documentation

* **qa:** correct QA test-account guidance and drop stale credentials ([#836](https://github.com/lifekit-hq/finance-sentry/issues/836)) ([7147ea0](https://github.com/lifekit-hq/finance-sentry/commit/7147ea0d7ab70ee0a5533f25e395d61f0677cfd3))

## [1.13.0](https://github.com/lifekit-hq/finance-sentry/compare/v1.12.0...v1.13.0) (2026-09-29)


### Features

* **banksync:** monthly family clearing statement job and Telegram delivery ([#735](https://github.com/lifekit-hq/finance-sentry/issues/735)) ([b84da85](https://github.com/lifekit-hq/finance-sentry/commit/b84da85ff1bc86dc25ab3f0ce8ab3745bc0a868a))
* **docker:** add memory caps to every prod compose service ([#741](https://github.com/lifekit-hq/finance-sentry/issues/741)) ([a47a0f2](https://github.com/lifekit-hq/finance-sentry/commit/a47a0f284e3a650b0fc2385e4d2a59dd45ee65a9))


### Refactoring

* **docker:** move finance-sentry to lifekit-shared network ([#742](https://github.com/lifekit-hq/finance-sentry/issues/742)) ([3353bc6](https://github.com/lifekit-hq/finance-sentry/commit/3353bc6d9c0e772be8b2c6871667bdf93440e2e8))
* **integration:** narrow cross-module adapters to Domain.Ports read ports ([#673](https://github.com/lifekit-hq/finance-sentry/issues/673)) ([#737](https://github.com/lifekit-hq/finance-sentry/issues/737)) ([ba04abf](https://github.com/lifekit-hq/finance-sentry/commit/ba04abfa015ba0aa5dd9d521b1c929ae31bccd5e))

## [1.12.0](https://github.com/lifekit-hq/finance-sentry/compare/v1.11.0...v1.12.0) (2026-09-28)


### Features

* **research:** add benchmark-relative thesis invalidation metric ([#729](https://github.com/lifekit-hq/finance-sentry/issues/729)) ([fb4ea04](https://github.com/lifekit-hq/finance-sentry/commit/fb4ea0467c23c6f3704d806c2ed657f388ec9453))
* **wealth:** backfill approximate net-worth history before first snapshot ([#734](https://github.com/lifekit-hq/finance-sentry/issues/734)) ([d9285e6](https://github.com/lifekit-hq/finance-sentry/commit/d9285e6724905f6d83bde2749bba4623fbfff866))


### Bug Fixes

* **risk:** let paper-sleeve promotions skip the real-book cash floor ([#728](https://github.com/lifekit-hq/finance-sentry/issues/728)) ([ef8b715](https://github.com/lifekit-hq/finance-sentry/commit/ef8b7157fe6244e7e06b47c0820b410ceb789714)), closes [#704](https://github.com/lifekit-hq/finance-sentry/issues/704)

## [1.11.0](https://github.com/lifekit-hq/finance-sentry/compare/v1.10.0...v1.11.0) (2026-09-26)


### Features

* **435:** date-aware EUR reporting boundary (S3) ([#683](https://github.com/lifekit-hq/finance-sentry/issues/683)) ([ea1903a](https://github.com/lifekit-hq/finance-sentry/commit/ea1903afc3af5bb9cfcf8aabef1058400a83800b))
* **alerts:** auto-resolve alerts when the raising condition clears ([#419](https://github.com/lifekit-hq/finance-sentry/issues/419) S3) ([#701](https://github.com/lifekit-hq/finance-sentry/issues/701)) ([bffd437](https://github.com/lifekit-hq/finance-sentry/commit/bffd437cd85a628ca33379ea8accaed51a5d6925))
* **alerts:** daily age-based alert expiry job ([#419](https://github.com/lifekit-hq/finance-sentry/issues/419) S4) ([#703](https://github.com/lifekit-hq/finance-sentry/issues/703)) ([4ef9546](https://github.com/lifekit-hq/finance-sentry/commit/4ef9546ce08e81bc87a97a223b41877a78a4b873))
* **alerts:** occurrence counter and Defer lifecycle ([#419](https://github.com/lifekit-hq/finance-sentry/issues/419) S5) ([#709](https://github.com/lifekit-hq/finance-sentry/issues/709)) ([908b5af](https://github.com/lifekit-hq/finance-sentry/commit/908b5af3570c112c510ce0d3c34c76082c2acb1b))
* **architecture:** enforce namespace-level module boundaries (NetArchTest) ([#674](https://github.com/lifekit-hq/finance-sentry/issues/674)) ([081f64b](https://github.com/lifekit-hq/finance-sentry/commit/081f64b5ad1ba57a837e8839593388c294373492))
* **bank-sync:** add family clearing statement query ([#434](https://github.com/lifekit-hq/finance-sentry/issues/434) S2) ([#710](https://github.com/lifekit-hq/finance-sentry/issues/710)) ([5358bd4](https://github.com/lifekit-hq/finance-sentry/commit/5358bd4eb0aac57cd0e3164cbab608d331428945))
* **bank-sync:** counterparty expected monthly inflow + rent confirmation fields ([#434](https://github.com/lifekit-hq/finance-sentry/issues/434) S3) ([#715](https://github.com/lifekit-hq/finance-sentry/issues/715)) ([0e8659c](https://github.com/lifekit-hq/finance-sentry/commit/0e8659ccc864aba14cd67a734e4a4fcff2b7f2b7))
* **bank-sync:** native per-currency subtotals on counterparty monthly flow ([#705](https://github.com/lifekit-hq/finance-sentry/issues/705)) ([82f5cb7](https://github.com/lifekit-hq/finance-sentry/commit/82f5cb74d1de9f775aa9fbe0cb761c7e94c4841f)), closes [#434](https://github.com/lifekit-hq/finance-sentry/issues/434)
* **banksync:** push transaction filtering into SQL ([#418](https://github.com/lifekit-hq/finance-sentry/issues/418) S1) ([#702](https://github.com/lifekit-hq/finance-sentry/issues/702)) ([bc52ace](https://github.com/lifekit-hq/finance-sentry/commit/bc52ace94a5f6d793b742549a6d0d8f06465a6a5))
* **brokerage:** gate gain/loss output on a verified cost basis ([#688](https://github.com/lifekit-hq/finance-sentry/issues/688)) ([#720](https://github.com/lifekit-hq/finance-sentry/issues/720)) ([77dc657](https://github.com/lifekit-hq/finance-sentry/commit/77dc65719840c6aeebdb73e37974d86ad11987df))
* **brokerage:** IBKR Flex Web Service ingestion infrastructure ([#716](https://github.com/lifekit-hq/finance-sentry/issues/716)) ([1f531f6](https://github.com/lifekit-hq/finance-sentry/commit/1f531f6af65445d11a14edc36cb15348da0b42e5)), closes [#435](https://github.com/lifekit-hq/finance-sentry/issues/435)
* **brokerage:** persist IBKR Flex trade/cash history, daily sync + one-shot backfill ([#435](https://github.com/lifekit-hq/finance-sentry/issues/435) S5) ([#717](https://github.com/lifekit-hq/finance-sentry/issues/717)) ([7a998aa](https://github.com/lifekit-hq/finance-sentry/commit/7a998aad8842ecef927f30068da2f1bad0161f6b))
* **brokerage:** persist IBKR instrument identity and human-set tax classification ([#435](https://github.com/lifekit-hq/finance-sentry/issues/435) S4) ([#714](https://github.com/lifekit-hq/finance-sentry/issues/714)) ([622a688](https://github.com/lifekit-hq/finance-sentry/commit/622a6889c57393827d1f5c0f0a3efb84a4ed4120))
* **budgets:** add BudgetPace primitive ([#706](https://github.com/lifekit-hq/finance-sentry/issues/706)) ([40b34b7](https://github.com/lifekit-hq/finance-sentry/commit/40b34b7b49886ead09ae8178185d39aa1c523ed8)), closes [#433](https://github.com/lifekit-hq/finance-sentry/issues/433)
* **budgets:** alert when a monthly budget reaches 90% and 100% of its limit ([#666](https://github.com/lifekit-hq/finance-sentry/issues/666)) ([c3e7c58](https://github.com/lifekit-hq/finance-sentry/commit/c3e7c58e7018c9fe0c6f0cb69accd634eb52acbd))
* **budgets:** fire pace crossing on the budget breach job ([#433](https://github.com/lifekit-hq/finance-sentry/issues/433) S2) ([#708](https://github.com/lifekit-hq/finance-sentry/issues/708)) ([f6dcde5](https://github.com/lifekit-hq/finance-sentry/commit/f6dcde52332805b698a9417519ace51933c7a49d))
* **crypto-sync:** persist crypto fills to a new trades table ([#435](https://github.com/lifekit-hq/finance-sentry/issues/435) S1) ([#681](https://github.com/lifekit-hq/finance-sentry/issues/681)) ([30788ac](https://github.com/lifekit-hq/finance-sentry/commit/30788ac5c28cdfb96656826a29c1be15047c57a7))
* **crypto-sync:** soft-close closed crypto holdings instead of deleting them ([#435](https://github.com/lifekit-hq/finance-sentry/issues/435) S2) ([#682](https://github.com/lifekit-hq/finance-sentry/issues/682)) ([034d6c0](https://github.com/lifekit-hq/finance-sentry/commit/034d6c0ddd3c8ff4743eb0fc770dbc9946083862))
* **events:** add Events module with upcoming calendar, fired-events feed, MCP tools and SPA page ([#669](https://github.com/lifekit-hq/finance-sentry/issues/669)) ([81a1d41](https://github.com/lifekit-hq/finance-sentry/commit/81a1d419f4eb8a71f89c807a7d74511bd79d48d6))
* **mcp:** delegate list_transactions filtering to the transaction query ([#418](https://github.com/lifekit-hq/finance-sentry/issues/418) S2) ([#707](https://github.com/lifekit-hq/finance-sentry/issues/707)) ([7de9a83](https://github.com/lifekit-hq/finance-sentry/commit/7de9a83fd0f7dc267c59c6621202cd11466d4759))
* **mcp:** expose family clearing statement as an MCP tool ([#434](https://github.com/lifekit-hq/finance-sentry/issues/434) S4) ([#712](https://github.com/lifekit-hq/finance-sentry/issues/712)) ([480abf2](https://github.com/lifekit-hq/finance-sentry/commit/480abf21491e272673aca6f7126268b0eceeb7ac))
* **migrations:** halt startup on a failed migration instead of serving a half-migrated schema ([#667](https://github.com/lifekit-hq/finance-sentry/issues/667)) ([370b612](https://github.com/lifekit-hq/finance-sentry/commit/370b61280e980cbdaa7884903dcf49d49651b72f))
* **migrations:** report a never-migrated schema in the readiness check ([#670](https://github.com/lifekit-hq/finance-sentry/issues/670)) ([2c7aa70](https://github.com/lifekit-hq/finance-sentry/commit/2c7aa70691a51c3fa96a9ad057528a85751e052b))
* **observability:** give retried Hangfire jobs their own linked trace ([#668](https://github.com/lifekit-hq/finance-sentry/issues/668)) ([c20f7e2](https://github.com/lifekit-hq/finance-sentry/commit/c20f7e2fbc441f713cccbabdf6ce6e81aae2e88b))
* **research:** add intraday-move sentinel alerts for holdings and watchlist ([#665](https://github.com/lifekit-hq/finance-sentry/issues/665)) ([562865a](https://github.com/lifekit-hq/finance-sentry/commit/562865aa3cb99e9ca5cbb867e8d3ba4d63fe3297))
* **research:** retire thesis news sources when their thesis is deleted or drifts off-topic ([#662](https://github.com/lifekit-hq/finance-sentry/issues/662)) ([2074e6e](https://github.com/lifekit-hq/finance-sentry/commit/2074e6e7ab2a81a97f52b13b0d8d3fe174d65334))
* **wealth:** FIRE calculator, per-user assumptions, projection endpoint ([#713](https://github.com/lifekit-hq/finance-sentry/issues/713)) ([635aeeb](https://github.com/lifekit-hq/finance-sentry/commit/635aeebb408fb193e1d20e955ddc281d13624c2f)), closes [#433](https://github.com/lifekit-hq/finance-sentry/issues/433)


### Bug Fixes

* **alerts:** key provider-level SyncFailure alerts on the provider ([#419](https://github.com/lifekit-hq/finance-sentry/issues/419) S2) ([#684](https://github.com/lifekit-hq/finance-sentry/issues/684)) ([d7cfe35](https://github.com/lifekit-hq/finance-sentry/commit/d7cfe358aa8ba5ed0f9f97deddd38113d3666221))
* **alerts:** supersede an open job-failure alert instead of throwing on the next streak ([#671](https://github.com/lifekit-hq/finance-sentry/issues/671)) ([c500ceb](https://github.com/lifekit-hq/finance-sentry/commit/c500cebd5fd83565444252ddc137cbeaa969ca38))
* **alerts:** unread badge excludes resolved alerts ([#680](https://github.com/lifekit-hq/finance-sentry/issues/680)) ([8700daf](https://github.com/lifekit-hq/finance-sentry/commit/8700dafd2e116481e4fd5c6238bda51a4f686585)), closes [#419](https://github.com/lifekit-hq/finance-sentry/issues/419)
* **brokerage:** stop a single transient IBKR blip from alerting nightly ([#725](https://github.com/lifekit-hq/finance-sentry/issues/725)) ([084a412](https://github.com/lifekit-hq/finance-sentry/commit/084a4125caff2295c05dd2055bedc98f48c094be)), closes [#695](https://github.com/lifekit-hq/finance-sentry/issues/695)
* **ci:** run the declared dotnet test steps through the SDK image when dotnet is not on PATH ([#672](https://github.com/lifekit-hq/finance-sentry/issues/672)) ([7447291](https://github.com/lifekit-hq/finance-sentry/commit/7447291b42723ec8d5ce9fca81cd6582c1082971))
* **companion:** execute the notification policy on real settings, not an unsaved default ([#686](https://github.com/lifekit-hq/finance-sentry/issues/686)) ([#718](https://github.com/lifekit-hq/finance-sentry/issues/718)) ([0c6bd47](https://github.com/lifekit-hq/finance-sentry/commit/0c6bd47a4299ea9bbe8f468e394a60af038b33f3))
* **events:** give the scheduled wake-up an explicit verdict contract ([#721](https://github.com/lifekit-hq/finance-sentry/issues/721)) ([9d3ecbb](https://github.com/lifekit-hq/finance-sentry/commit/9d3ecbb11335bb88a455e8455a8472783cef9a7d)), closes [#687](https://github.com/lifekit-hq/finance-sentry/issues/687)
* **mcp:** classify get_cashflow_report through money-flow statistics ([#676](https://github.com/lifekit-hq/finance-sentry/issues/676)) ([e1fc59e](https://github.com/lifekit-hq/finance-sentry/commit/e1fc59e7ed2c1f4b2bf966f7b2a44d4b01e4703a))
* **migrations:** run Risk's schema before Research's M012 on fresh databases ([#664](https://github.com/lifekit-hq/finance-sentry/issues/664)) ([d20c82c](https://github.com/lifekit-hq/finance-sentry/commit/d20c82c936c3d2ddc9bd45d457fe6de7ef464221))
* **observability:** link a manual dashboard Requeue's trace like an automatic retry ([#616](https://github.com/lifekit-hq/finance-sentry/issues/616)) ([#677](https://github.com/lifekit-hq/finance-sentry/issues/677)) ([6449209](https://github.com/lifekit-hq/finance-sentry/commit/6449209832e59417fea25b2b9b73bed686c3c6b1))
* **research:** bring news-materiality detector back to its designed fire rate ([#723](https://github.com/lifekit-hq/finance-sentry/issues/723)) ([9f81085](https://github.com/lifekit-hq/finance-sentry/commit/9f81085ce18614922492c3403246022195389341)), closes [#693](https://github.com/lifekit-hq/finance-sentry/issues/693)
* **research:** reject thesis invalidation triggers that cannot be evaluated ([#724](https://github.com/lifekit-hq/finance-sentry/issues/724)) ([2132d51](https://github.com/lifekit-hq/finance-sentry/commit/2132d51ea26d71ac582bf4588c5301ed41b3963c)), closes [#692](https://github.com/lifekit-hq/finance-sentry/issues/692)
* **research:** surface provider failure on earnings/filings sources ([#679](https://github.com/lifekit-hq/finance-sentry/issues/679)) ([d8673c3](https://github.com/lifekit-hq/finance-sentry/commit/d8673c32e66820e8a65f1edfd70f2c87f10c7b57))
* **risk:** stop allocation-drift matching targets against the wrong taxonomy ([#722](https://github.com/lifekit-hq/finance-sentry/issues/722)) ([32bb7ff](https://github.com/lifekit-hq/finance-sentry/commit/32bb7fff600b88c82f3180816e5bc90671df5e8d)), closes [#690](https://github.com/lifekit-hq/finance-sentry/issues/690)
* **risk:** surface acknowledgement state and a reportable flag on policy violations ([#719](https://github.com/lifekit-hq/finance-sentry/issues/719)) ([8f00c36](https://github.com/lifekit-hq/finance-sentry/commit/8f00c3684fcd2a725d71cc4de037ed99678a47bb))
* **subscriptions:** match manual commitments by normalized merchant name ([#678](https://github.com/lifekit-hq/finance-sentry/issues/678)) ([7c2e7b4](https://github.com/lifekit-hq/finance-sentry/commit/7c2e7b4566d07ca4c91330d8a496ed8e8c99d7bb))


### Documentation

* **money-semantics:** neutral wording for the rent-fields note ([#711](https://github.com/lifekit-hq/finance-sentry/issues/711)) ([134812b](https://github.com/lifekit-hq/finance-sentry/commit/134812bd2eedf9a992c43cdca017db2cb375423b))

## [1.10.0](https://github.com/lifekit-hq/finance-sentry/compare/v1.9.0...v1.10.0) (2026-09-21)


### Features

* **research:** add earnings-ahead detector job with EarningsAhead alerts ([#655](https://github.com/lifekit-hq/finance-sentry/issues/655)) ([f815ae7](https://github.com/lifekit-hq/finance-sentry/commit/f815ae7e81db297eda8df79d5f1566ccb1cf38d4))
* **research:** add hourly filing-watch job emitting FilingLanded alerts ([#656](https://github.com/lifekit-hq/finance-sentry/issues/656)) ([df3b189](https://github.com/lifekit-hq/finance-sentry/commit/df3b189a3e78d36b7fc36f24e6f9f314153e0044))
* **research:** add news-cluster detector with Google News RSS geopolitics source ([#660](https://github.com/lifekit-hq/finance-sentry/issues/660)) ([c7127e9](https://github.com/lifekit-hq/finance-sentry/commit/c7127e9c292befdb7f7b77ef202e4756ed9e29ae))


### Refactoring

* **421:** rename research/assets/{symbol}/ledger-read to /narrative (Principle VII) ([#646](https://github.com/lifekit-hq/finance-sentry/issues/646)) ([9d7f6f5](https://github.com/lifekit-hq/finance-sentry/commit/9d7f6f5a2079ec5962ad04e0eb507878d4101f5e))

## [1.9.0](https://github.com/lifekit-hq/finance-sentry/compare/v1.8.0...v1.9.0) (2026-09-18)


### Features

* **048:** conform finance-sentry to the platform contract ([#639](https://github.com/lifekit-hq/finance-sentry/issues/639)) ([c065fff](https://github.com/lifekit-hq/finance-sentry/commit/c065fff011c494b4cd5f0b24e3e0472d63d9092a))
* **bank-sync:** discover new accounts on existing connections during scheduled sync ([#637](https://github.com/lifekit-hq/finance-sentry/issues/637)) ([d164b31](https://github.com/lifekit-hq/finance-sentry/commit/d164b310bdf1e8847dc60507f198313bd75ad9d2))
* **companion:** authenticate the agent wake and hold SyncFailure for the digest ([#641](https://github.com/lifekit-hq/finance-sentry/issues/641)) ([3f07f28](https://github.com/lifekit-hq/finance-sentry/commit/3f07f280cac8e6f12e1023d2c7f4b002d3637365))
* **crypto:** accumulate Revolut X cost basis from trades and keep venue fiat as venue cash ([#634](https://github.com/lifekit-hq/finance-sentry/issues/634)) ([8648734](https://github.com/lifekit-hq/finance-sentry/commit/8648734d599279162a899f6537728f9a362c39fb))
* **crypto:** sync Revolut X holdings behind a provider-keyed exchange seam ([#629](https://github.com/lifekit-hq/finance-sentry/issues/629)) ([2c8db0c](https://github.com/lifekit-hq/finance-sentry/commit/2c8db0ccf33a0df2fa9dc19e1b00bc3f9b848dae))
* **observability:** add HTTP trace spine (gateway -&gt; api -&gt; Npgsql) with trace ids in logs ([#628](https://github.com/lifekit-hq/finance-sentry/issues/628)) ([5b991a6](https://github.com/lifekit-hq/finance-sentry/commit/5b991a665d44e4e29e382c74ca40e5e4fbef8cbd))
* **observability:** propagate trace context into Hangfire jobs ([#632](https://github.com/lifekit-hq/finance-sentry/issues/632)) ([b3878a5](https://github.com/lifekit-hq/finance-sentry/commit/b3878a54b74a219eb602bf1ee75eb28dc188e413))
* **scanner:** gate the widened scan's alerts and prove the funnel end… ([#612](https://github.com/lifekit-hq/finance-sentry/issues/612)) ([e8616ff](https://github.com/lifekit-hq/finance-sentry/commit/e8616ff6dfec3b54aa6e5cad3c4d9266ee648534))


### Bug Fixes

* **agent:** never save a failed or silent agent turn as a Ledger read ([#636](https://github.com/lifekit-hq/finance-sentry/issues/636)) ([f31ed3e](https://github.com/lifekit-hq/finance-sentry/commit/f31ed3e26fd41d9f807d3777af650929f117f0a2)), closes [#635](https://github.com/lifekit-hq/finance-sentry/issues/635)
* **auth:** revoke refresh token on logout and honor configured JWT lifetime ([#638](https://github.com/lifekit-hq/finance-sentry/issues/638)) ([20a5501](https://github.com/lifekit-hq/finance-sentry/commit/20a5501edc850051c45a486a31b34541a5801fbf))
* **backend:** match OpenTelemetry Prometheus exporter for nuget minor/patch group ([#631](https://github.com/lifekit-hq/finance-sentry/issues/631)) ([5631bfc](https://github.com/lifekit-hq/finance-sentry/commit/5631bfc48ecf2f6d5cf83b67efc1e3eb09b2630a))
* **banksync:** keep digits-only last4 and link only after accounts exist ([#633](https://github.com/lifekit-hq/finance-sentry/issues/633)) ([1848a19](https://github.com/lifekit-hq/finance-sentry/commit/1848a19ce6888cf7b55c180e35a19f9c723d3545))
* **research:** stop a failed quote-cache write poisoning the thesis save ([#627](https://github.com/lifekit-hq/finance-sentry/issues/627)) ([403b1bd](https://github.com/lifekit-hq/finance-sentry/commit/403b1bd915d67fa47a2fa2e1994d98ff354c87cc)), closes [#626](https://github.com/lifekit-hq/finance-sentry/issues/626)


### Documentation

* **constitution:** add Principle VII — Ledger boundary (MCP-only tools, id-only hook, no Ledger-named endpoint) ([#640](https://github.com/lifekit-hq/finance-sentry/issues/640)) ([f55c87d](https://github.com/lifekit-hq/finance-sentry/commit/f55c87d38a0ccdef74f819eeeda5bf2259aab39c))

## [1.8.0](https://github.com/lifekit-hq/finance-sentry/compare/v1.7.0...v1.8.0) (2026-09-14)


### Features

* **044:** classify counterparty flows per direction, drop netting ([#547](https://github.com/lifekit-hq/finance-sentry/issues/547)) ([69b811d](https://github.com/lifekit-hq/finance-sentry/commit/69b811dc785a47732bd28c9ff360108d35b153af))
* **432:** US2 cash-sweep proposal + US3 one-tap acknowledgement ([#549](https://github.com/lifekit-hq/finance-sentry/issues/549)) ([0cefaf2](https://github.com/lifekit-hq/finance-sentry/commit/0cefaf2b48f9c611fc4537ae67e54dd7815175b3))
* **554:** widen committed outflow to rent, loans and counterparty obligations ([#602](https://github.com/lifekit-hq/finance-sentry/issues/602)) ([cd73dd7](https://github.com/lifekit-hq/finance-sentry/commit/cd73dd7e0e7c584278e54e8966d30e6a2bf40449)), closes [#554](https://github.com/lifekit-hq/finance-sentry/issues/554)
* **576:** flow-breakdown drill-down — click a tile to audit its transactions ([#577](https://github.com/lifekit-hq/finance-sentry/issues/577)) ([5ceacae](https://github.com/lifekit-hq/finance-sentry/commit/5ceacae026af7e9b53a2418d86e75072f59d591a))
* **580:** exclude self-routed money via currency-scoped counterparty rules ([#583](https://github.com/lifekit-hq/finance-sentry/issues/583)) ([f9df3d8](https://github.com/lifekit-hq/finance-sentry/commit/f9df3d835f29fe71aea88c6bbb4bb43db1b58a00)), closes [#580](https://github.com/lifekit-hq/finance-sentry/issues/580)
* Merge origin/main into goal/issue-414 weekly performance brief ([#545](https://github.com/lifekit-hq/finance-sentry/issues/545)) ([28211c3](https://github.com/lifekit-hq/finance-sentry/commit/28211c3e5f4a0092691a02cf9db2bf4808471c73)), closes [#414](https://github.com/lifekit-hq/finance-sentry/issues/414)
* **research:** re-probe retired news sources so retirement is not permanent ([#588](https://github.com/lifekit-hq/finance-sentry/issues/588)) ([5fab4ec](https://github.com/lifekit-hq/finance-sentry/commit/5fab4ecb949647253762e87560c3af133d4ea07f))


### Bug Fixes

* **044:** align sentinel queries and thresholds to spec ([#548](https://github.com/lifekit-hq/finance-sentry/issues/548)) ([4de1810](https://github.com/lifekit-hq/finance-sentry/commit/4de1810594de147f33a99c60b00d90f5b79a6cac))
* **044:** count the mortgage as household spending and cover Liza's transliteration variants ([#575](https://github.com/lifekit-hq/finance-sentry/issues/575)) ([b9623ad](https://github.com/lifekit-hq/finance-sentry/commit/b9623adfc60cabbffca5bb7763d803a198df6ffb)), closes [#573](https://github.com/lifekit-hq/finance-sentry/issues/573)
* **044:** cover Latin-script family transfers and Revolut X in counterparty rules ([#572](https://github.com/lifekit-hq/finance-sentry/issues/572)) ([4d9aebd](https://github.com/lifekit-hq/finance-sentry/commit/4d9aebd12d1814556817a1640ffdb346db9d3542)), closes [#571](https://github.com/lifekit-hq/finance-sentry/issues/571)
* **044:** give M011/M012 data ops explicit column types so their SQL generates ([#566](https://github.com/lifekit-hq/finance-sentry/issues/566)) ([e9dd5ac](https://github.com/lifekit-hq/finance-sentry/commit/e9dd5ac6b78889ee7a260cc1428ee49813f4b57e)), closes [#565](https://github.com/lifekit-hq/finance-sentry/issues/565)
* **044:** make M011's FAMILY_SUPPORT category seed idempotent ([#568](https://github.com/lifekit-hq/finance-sentry/issues/568)) ([97320fc](https://github.com/lifekit-hq/finance-sentry/commit/97320fcf1f1e3c391bf91fc94e41e90b45ee1b6a)), closes [#567](https://github.com/lifekit-hq/finance-sentry/issues/567)
* **044:** reconcile BankSync snapshot with the model so M011/M012 apply ([#564](https://github.com/lifekit-hq/finance-sentry/issues/564)) ([63d8ddd](https://github.com/lifekit-hq/finance-sentry/commit/63d8dddbd2041ff6cb20cca4e4d1555e8748d405)), closes [#563](https://github.com/lifekit-hq/finance-sentry/issues/563)
* **044:** stop the FX-spread sentinel accusing banks over a stale rate ([#586](https://github.com/lifekit-hq/finance-sentry/issues/586)) ([74662de](https://github.com/lifekit-hq/finance-sentry/commit/74662de3580dcdc804223b0c5902e16ae1963727))
* **406:** add postinstall shim to patch @lifekit-hq/ui@0.2.0 stacked… ([#542](https://github.com/lifekit-hq/finance-sentry/issues/542)) ([996057b](https://github.com/lifekit-hq/finance-sentry/commit/996057b535ae5a793a4148478a3b35209921f938))
* **421:** dossier no-data state and a stale tag that fired before first… ([#546](https://github.com/lifekit-hq/finance-sentry/issues/546)) ([6d3172d](https://github.com/lifekit-hq/finance-sentry/commit/6d3172d8f92ab4f471921d652ea951d513ed18d7))
* **581:** consult the merchant-keyword bridge on the Monobank ingest path ([#582](https://github.com/lifekit-hq/finance-sentry/issues/582)) ([5c67e88](https://github.com/lifekit-hq/finance-sentry/commit/5c67e88dde7146caf944430727bea34b277b5bb3)), closes [#581](https://github.com/lifekit-hq/finance-sentry/issues/581)
* **alerts:** make the M003 acknowledgement migration discoverable by EF ([#555](https://github.com/lifekit-hq/finance-sentry/issues/555)) ([7f2c519](https://github.com/lifekit-hq/finance-sentry/commit/7f2c519dc90ec846912d3083338ab8cd8a629044))
* **bank-sync:** a credential write records the key version it encrypted under ([#610](https://github.com/lifekit-hq/finance-sentry/issues/610)) ([10e35b2](https://github.com/lifekit-hq/finance-sentry/commit/10e35b281e134cca112a4e40206f274c57f6c9d1))
* **bank-sync:** reconcile AIB's rewritten settlements and retire own-account routing ([#607](https://github.com/lifekit-hq/finance-sentry/issues/607)) ([db50e19](https://github.com/lifekit-hq/finance-sentry/commit/db50e197d67ef76fae879a436f3e455da16aca46))
* **ci:** close the gate holes that let unvalidated PRs merge ([#562](https://github.com/lifekit-hq/finance-sentry/issues/562)) ([57a691b](https://github.com/lifekit-hq/finance-sentry/commit/57a691b30222a1128d480dc284e9529044554072)), closes [#561](https://github.com/lifekit-hq/finance-sentry/issues/561)
* **ci:** release workflows act through the release bot app token so the release PR can merge ([#623](https://github.com/lifekit-hq/finance-sentry/issues/623)) ([#624](https://github.com/lifekit-hq/finance-sentry/issues/624)) ([bfd0f23](https://github.com/lifekit-hq/finance-sentry/commit/bfd0f23e10e9501deec8a44b7c4333a18d22e6f1))
* **encryption:** stop encrypting bank credentials with a published key ([#493](https://github.com/lifekit-hq/finance-sentry/issues/493)) ([#609](https://github.com/lifekit-hq/finance-sentry/issues/609)) ([c25e86d](https://github.com/lifekit-hq/finance-sentry/commit/c25e86d64ec60edc43c41152f6434642c905192c))
* **mcp:** keep worker-role registrations out of the MCP host ([#613](https://github.com/lifekit-hq/finance-sentry/issues/613)) ([#614](https://github.com/lifekit-hq/finance-sentry/issues/614)) ([7df1edc](https://github.com/lifekit-hq/finance-sentry/commit/7df1edce14c154a4de6aa839d29abf6fb645dd68))
* **research:** self-heal the TrendForce source off its stale seeded URL ([#552](https://github.com/lifekit-hq/finance-sentry/issues/552)) ([8beec79](https://github.com/lifekit-hq/finance-sentry/commit/8beec790d440f4a403f0e96e45d510c6ff8c0c20)), closes [#318](https://github.com/lifekit-hq/finance-sentry/issues/318)
* **sentinels:** drop the lookback widening that selected the same rows ([#606](https://github.com/lifekit-hq/finance-sentry/issues/606)) ([3090fd9](https://github.com/lifekit-hq/finance-sentry/commit/3090fd91abb40b468d15b382e50a4373d3eecd1f))
* **specify:** untrack the per-checkout feature.json pointer ([#556](https://github.com/lifekit-hq/finance-sentry/issues/556)) ([59676a6](https://github.com/lifekit-hq/finance-sentry/commit/59676a601a0daeab2fda271952f376b3550ac444))


### Refactoring

* **553:** one ordered categorization ladder on every ingest path ([#601](https://github.com/lifekit-hq/finance-sentry/issues/601)) ([7d2a779](https://github.com/lifekit-hq/finance-sentry/commit/7d2a7795b71bfa48fc93eac52c653c5547379f0f))
* **554:** one key seam and a race-safe write behind committed pins ([#605](https://github.com/lifekit-hq/finance-sentry/issues/605)) ([c3a5853](https://github.com/lifekit-hq/finance-sentry/commit/c3a5853d74222346d909707990d4d68c6acf14f3))


### Documentation

* **538:** repoint M004's rounding reference after the recognizer move ([#550](https://github.com/lifekit-hq/finance-sentry/issues/550)) ([718a399](https://github.com/lifekit-hq/finance-sentry/commit/718a399d49ae95c051011dd2a45723f3031a022b))
* **agents:** correct two stale sandbox frontend setup steps ([#551](https://github.com/lifekit-hq/finance-sentry/issues/551)) ([e9de3ed](https://github.com/lifekit-hq/finance-sentry/commit/e9de3ed82e601a99f2462772b8cb2ed52ba8f03d))
* point agents at the vault for project memory ([#622](https://github.com/lifekit-hq/finance-sentry/issues/622)) ([f7d696a](https://github.com/lifekit-hq/finance-sentry/commit/f7d696a1d411d3450e2315939d2e3be3c3c09006))
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
