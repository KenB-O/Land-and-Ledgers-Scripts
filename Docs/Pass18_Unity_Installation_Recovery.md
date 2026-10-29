# Pass XVIII — Unity Installation Recovery

Date: 2026-10-29

## Result

The exact Unity 6000.4.2f1 environment was not recovered. The failure reproduced in disposable projects containing a trivial user script, while the installed 6000.2.8f1 editor compiled the same class and passed a fresh EditMode test. This is strong version/install evidence, but it is not permission to migrate Land & Ledgers.

## Baseline

- Project: `F:\Land & Ledgers`
- Scripts branch: `integration/pass12-unity-readiness`
- Starting SHA: `90d9cd493a88cc1755dbcb725f87aa8da2fc0e18`
- Unity: `6000.4.2f1 (7a4c1aeef971)`
- No gameplay source or Unity asset changes were made.

## Repair attempt

The existing editor is installed under Program Files and a supported same-version install with the default path returned `ELEVATION_REQUIRED`. The Unity CLI install path was temporarily pointed to `F:\Land & Ledgers_Pass18_Backups\UnityEditors`; the forced 6000.4.2f1 install then remained idle without network, file or CPU progress and was stopped. The install path was restored to `C:\Program Files\Unity\Hub\Editor`. The existing installation was not removed or overwritten.

Windows Application events also contain Unity.exe faults in Unity.dll with `0x80000003` on 2026-10-28 and `0xc0000005` on 2026-10-27. These support an editor/environment problem but do not identify a specific IL postprocessor.

## Conclusion

Land & Ledgers remains unverified under 6000.4.2f1. The next supported action is an administrator-capable repair or fresh installation of the exact same editor, followed by fresh probes and then the real project. Do not open the real project in 6000.2.8f1 without separate approval.
