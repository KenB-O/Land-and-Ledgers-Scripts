using System;

namespace LandLedgers.Tasks
{
    /// <summary>
    /// NX-1A: execution gate for TTS-2 tasks. Canon 4.1 CANON LOCK — "A task is
    /// executable only when the relevant combination exists: capable Person(s)
    /// + required tool/equipment method + suitable workspace/location +
    /// materials/inputs + motive power where needed + authority/right +
    /// available time." TaskAuthority consults the installed gate in StartTask
    /// (work actually begins); a non-null return refuses execution loudly with
    /// the named missing requirement — never a silent debuff (Tech X §3.9).
    /// Null gate = no equipment enforcement (legacy/test behavior).
    /// </summary>
    public interface ITaskExecutionGate
    {
        /// <summary>
        /// Returns null when the task may start; otherwise the refusal reason
        /// naming the missing requirement.
        /// </summary>
        string CheckStart(WorkTask task, TaskDefinition definition, int dayIndex);
    }
}
