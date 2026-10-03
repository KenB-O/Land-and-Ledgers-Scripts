using LandLedgers.Primitives;

namespace LandLedgers.Orchestration.Player
{
    /// <summary>
    /// TTS-4: the contract Kennedy's player prefab implements (Unity side).
    ///
    /// The script side (<see cref="PlayerDirector"/>) is the authority: it decides
    /// what the player does, books time against the work-time budget, and owns task
    /// assignment through the shared <see cref="Tasks.TaskAuthority"/>. The avatar is
    /// the execution end: it moves the model, plays the visuals, and handles input
    /// by translating it into director calls — never by bypassing the director.
    ///
    /// See PlayerAvatarContract.md for the full Unity-side vs script-side split.
    /// </summary>
    public interface IPlayerAvatar
    {
        /// <summary>Where the avatar logically is right now (mirrors the director's truth).</summary>
        string CurrentLocationId { get; }

        /// <summary>True while the avatar is executing a move or task (animation/translation in flight).</summary>
        bool IsBusy { get; }

        /// <summary>
        /// Director → avatar: move to a destination. The director has already booked
        /// the travel minutes against the player's work-time budget; the avatar
        /// performs the visual movement and the director advances progress as the
        /// clock runs (see <see cref="PlayerDirector.RecordMovementProgress"/>).
        /// </summary>
        void ExecuteMove(string destinationLocationId, int travelMinutes);

        /// <summary>
        /// Director → avatar: perform a task. The task is already assigned and
        /// started in the shared task authority; the avatar plays the working
        /// visuals at the task's location.
        /// </summary>
        void ExecuteTask(EntityId taskId);

        /// <summary>Director → avatar: stop whatever is executing (visuals halt; script truth already updated).</summary>
        void ExecuteCancel();
    }
}
