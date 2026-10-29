# Pass XVII — Environment isolation matrix

| Environment | User scripts | Test Framework | Result |
|---|---:|---:|---|
| Clean disposable Unity project | No | No | Compiled and exited normally |
| Clean disposable Unity project | Yes, one trivial class | No | Bee second-stage stall |
| Clean disposable Unity project | No | Yes, built-in 1.6.0 | Completed after generated-state initialization |
| Clean disposable Unity project | Yes, one trivial NUnit test | Yes, built-in 1.6.0 | Bee/ILPP stall before NUnit XML |
| Land & Ledgers | Many | Yes, built-in 1.6.0 | Bee/ILPP stall before NUnit XML |

The Unity installation is `C:/Program Files/Unity/Hub/Editor/6000.4.2f1-x86_64`.
The primary manifest/lockfile were not changed. The disposable experiments
were kept under `F:/Land & Ledgers_Pass17_Backups`.

The Test Framework manifest experiment was initially pinned by a generated
lockfile; after that lockfile was moved aside, Unity still resolved the
installation’s built-in Test Framework 1.6.0. It is therefore not evidence for
an alternate-version fix.

