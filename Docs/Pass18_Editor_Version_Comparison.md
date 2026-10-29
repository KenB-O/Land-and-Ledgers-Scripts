# Pass XVIII — Editor Version Comparison

## 6000.2.8f1 disposable control

Project: `F:\Land & Ledgers_Pass18_Backups\unity-6000.2.8f1-probe`

The probe contained one trivial C# class and a fresh NUnit EditMode test. Unity 6000.2.8f1 compiled normally, completed Bee/Tundra build, exited normally, and produced `F:\Land & Ledgers_Pass18_Backups\unity-6000.2.8f1-probe-cli.xml`.

The XML reports one test, one pass, zero failures.

A PlayMode invocation completed but discovered zero tests; its result is retained at `F:\Land & Ledgers_Pass18_Backups\unity-6000.2.8f1-probe-playmode-2.xml`. It is not a PlayMode pass.

The contrast with the repeated 6000.4.2f1 trivial-script stall is strong evidence of an editor-version or installation/environment interaction. The Land & Ledgers project was never opened in 6000.2.8f1.
