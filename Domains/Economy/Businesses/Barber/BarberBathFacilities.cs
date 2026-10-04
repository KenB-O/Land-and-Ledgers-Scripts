using System;
using LandLedgers.Economy.Equipment.Workstations;

namespace LandLedgers.Economy.Businesses.Barber
{
    /// <summary>
    /// D1B: one bath tub. A tub IS a workstation instance (WorkstationId
    /// "barber-bath-station", Tech X §3.5): readiness derives from its
    /// component assets plus the shop's hot-water capability, never from a
    /// flag. Canon Part V barber profile: "bath tubs/hot-water capability" is
    /// the scale column — the bath service line exists because the shop
    /// invested in tubs. Concurrent baths can never exceed ready tubs.
    /// </summary>
    [Serializable]
    public sealed class BarberTub
    {
        public int TubIndex;
        public WorkstationInstance Station = new WorkstationInstance();

        public BarberTub() { }
    }
}
