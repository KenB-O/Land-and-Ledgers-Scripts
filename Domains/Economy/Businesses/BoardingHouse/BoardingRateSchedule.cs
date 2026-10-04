using System;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.BoardingHouse
{
    /// <summary>
    /// W2C: the weekly rate schedule as data. Canon §8.1D: "The owner can
    /// set room-only versus room-and-board pricing where the establishment
    /// supports both, weekly or longer-stay terms, transient rates..."
    ///
    /// Default calibration (Canon Part XV): shared-bed room-only 150¢/week
    /// plus the board add-on 125¢/week = 275¢/week with board — exactly the
    /// existing SharedBusinessRuntimeManager BoardingHouseWeeklyBoardCents
    /// (the shared weekly-ops authority), so the detailed layer agrees with
    /// the shared layer on the default price. All rates are proprietor
    /// policy, settable per house; money moves only through ledger
    /// authorities, never in this schedule.
    /// </summary>
    [Serializable]
    public sealed class BoardingRateSchedule
    {
        [SerializeField, Min(0)]
        private int sharedBedRoomOnlyWeeklyCents = 150;
        [SerializeField, Min(0)]
        private int privateRoomOnlyWeeklyCents = 250;
        [SerializeField, Min(0)]
        private int familyRoomOnlyWeeklyCents = 400;
        [SerializeField, Min(0)]
        private int boardAddOnWeeklyCents = 125;
        [SerializeField, Min(0)]
        private int transientNightlyRoomOnlyCents = 25;
        [SerializeField, Min(0)]
        private int transientNightlyWithBoardCents = 45;

        public BoardingRateSchedule() { }

        public BoardingRateSchedule(int sharedBedRoomOnlyWeeklyCents, int privateRoomOnlyWeeklyCents,
            int familyRoomOnlyWeeklyCents, int boardAddOnWeeklyCents,
            int transientNightlyRoomOnlyCents, int transientNightlyWithBoardCents)
        {
            this.sharedBedRoomOnlyWeeklyCents = Math.Max(0, sharedBedRoomOnlyWeeklyCents);
            this.privateRoomOnlyWeeklyCents = Math.Max(0, privateRoomOnlyWeeklyCents);
            this.familyRoomOnlyWeeklyCents = Math.Max(0, familyRoomOnlyWeeklyCents);
            this.boardAddOnWeeklyCents = Math.Max(0, boardAddOnWeeklyCents);
            this.transientNightlyRoomOnlyCents = Math.Max(0, transientNightlyRoomOnlyCents);
            this.transientNightlyWithBoardCents = Math.Max(0, transientNightlyWithBoardCents);
        }

        public int SharedBedRoomOnlyWeeklyCents => Math.Max(0, sharedBedRoomOnlyWeeklyCents);
        public int PrivateRoomOnlyWeeklyCents => Math.Max(0, privateRoomOnlyWeeklyCents);
        public int FamilyRoomOnlyWeeklyCents => Math.Max(0, familyRoomOnlyWeeklyCents);
        public int BoardAddOnWeeklyCents => Math.Max(0, boardAddOnWeeklyCents);
        public int TransientNightlyRoomOnlyCents => Math.Max(0, transientNightlyRoomOnlyCents);
        public int TransientNightlyWithBoardCents => Math.Max(0, transientNightlyWithBoardCents);

        /// <summary>Weekly rate for a room type, with or without the board add-on.</summary>
        public int WeeklyRateCents(BoardingRoomType roomType, bool boardIncluded)
        {
            int roomOnly;
            switch (roomType)
            {
                case BoardingRoomType.PrivateRoom: roomOnly = PrivateRoomOnlyWeeklyCents; break;
                case BoardingRoomType.FamilyRoom: roomOnly = FamilyRoomOnlyWeeklyCents; break;
                default: roomOnly = SharedBedRoomOnlyWeeklyCents; break;
            }
            return Math.Max(0, roomOnly + (boardIncluded ? BoardAddOnWeeklyCents : 0));
        }

        /// <summary>Per-night rate for transient stays.</summary>
        public int TransientNightlyRateCents(bool boardIncluded)
        {
            return boardIncluded ? TransientNightlyWithBoardCents : TransientNightlyRoomOnlyCents;
        }

        #region Save / Load
        [Serializable]
        public sealed class BoardingRateScheduleSaveDto
        {
            public int SharedBedRoomOnlyWeeklyCents;
            public int PrivateRoomOnlyWeeklyCents;
            public int FamilyRoomOnlyWeeklyCents;
            public int BoardAddOnWeeklyCents;
            public int TransientNightlyRoomOnlyCents;
            public int TransientNightlyWithBoardCents;
        }

        public BoardingRateScheduleSaveDto CaptureSaveDto()
        {
            return new BoardingRateScheduleSaveDto
            {
                SharedBedRoomOnlyWeeklyCents = SharedBedRoomOnlyWeeklyCents,
                PrivateRoomOnlyWeeklyCents = PrivateRoomOnlyWeeklyCents,
                FamilyRoomOnlyWeeklyCents = FamilyRoomOnlyWeeklyCents,
                BoardAddOnWeeklyCents = BoardAddOnWeeklyCents,
                TransientNightlyRoomOnlyCents = TransientNightlyRoomOnlyCents,
                TransientNightlyWithBoardCents = TransientNightlyWithBoardCents,
            };
        }

        public void LoadFromSaveDto(BoardingRateScheduleSaveDto dto)
        {
            if (dto == null) return;
            sharedBedRoomOnlyWeeklyCents = Math.Max(0, dto.SharedBedRoomOnlyWeeklyCents);
            privateRoomOnlyWeeklyCents = Math.Max(0, dto.PrivateRoomOnlyWeeklyCents);
            familyRoomOnlyWeeklyCents = Math.Max(0, dto.FamilyRoomOnlyWeeklyCents);
            boardAddOnWeeklyCents = Math.Max(0, dto.BoardAddOnWeeklyCents);
            transientNightlyRoomOnlyCents = Math.Max(0, dto.TransientNightlyRoomOnlyCents);
            transientNightlyWithBoardCents = Math.Max(0, dto.TransientNightlyWithBoardCents);
        }
        #endregion
    }
}
