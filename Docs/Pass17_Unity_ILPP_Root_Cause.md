# Pass XVII — Unity ILPP/Bee root-cause investigation

Date: 2026-10-29

## Conclusion

The strongest supported conclusion is an environment/Unity 6000.4.2f1 script
compilation failure or incompatibility in Bee/ILPP. It is not specific to the
Land & Ledgers source, HDRP, TextMeshPro, or the agricultural correction.

Evidence:

* A clean project with only built-in modules and no user scripts compiled and exited normally.
* Adding one trivial C# script to that clean project reproduced the Bee second-stage stall.
* A clean project with the built-in Test Framework and no user scripts completed after its generated state was rebuilt.
* The full Land & Ledgers project reproduced the same stall before NUnit discovery.

The last assembly name in a log is not treated as the culprit.

## Process evidence

The Land & Ledgers reproduction used Unity 6000.4.2f1 directly with a unique
`-logFile` and `-testResults` path. The associated Unity, Bee and ILPP processes
were identified by command line and parent process. After package registration,
the log stopped at script compilation; no NUnit XML appeared. Unity and the
child processes remained responsive but consumed negligible CPU. ILPP threads
were predominantly in Windows `Wait/UserRequest` and `Wait/EventPairLow`
states, while Bee threads were also waiting. This supports a blocked
IPC/synchronization condition, but does not prove a deadlock or expose a
specific call stack.

No dump utility was available in the environment, so no process dump was
created. Raw diagnostic logs remain local and were not published.

## Important historical clue

The preserved Pass XV log contains:
`bee_backend: error: More than one copy of bee_backend running ... PID 51052 waiting`.
Pass XVII cleaned only verified project-associated processes before each
reproduction. The current reproductions did not establish that stale-process
condition as the sole cause.

