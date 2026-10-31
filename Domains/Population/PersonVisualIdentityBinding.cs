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
                personId = -1;
                return false;
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
