using UnityEngine;

namespace LandLedgers.Population
{
    /// <summary>
    /// Runtime presentation binding from a visible actor to its authoritative
    /// population Person. This component stores only the stable identity; it
    /// never owns or mutates demographic, household, economic, or schedule state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PersonVisualIdentityBinding : MonoBehaviour
    {
        [SerializeField, Min(0)]
        private int personId = -1;

        public int PersonId => personId;
        public bool IsBound => personId >= 0;

        public bool Bind(int stablePersonId)
        {
            if (stablePersonId < 0)
            {
                return false;
            }

            // A view may not change which persistent Person it represents.
            // The current director does not pool actors; any future pooling must
            // add an explicit unbind/rebind lifecycle rather than silently
            // transferring identity here.
            if (personId >= 0)
            {
                return personId == stablePersonId;
            }

            personId = stablePersonId;
            return true;
        }

        public bool TryResolve(PopulationState population, out PersonState person)
        {
            person = IsBound && population != null ? population.GetPerson(personId) : null;
            return person != null;
        }
    }
}
