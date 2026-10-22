using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Time;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass V P1-2: cadence wiring for the demographic drivers. Daily events
    /// (births due, mortality) run every day; monthly events (conceptions,
    /// marriage, migration, household splits) run on a fixed day-of-month so
    /// small ticks, accelerated jumps, and save/load all produce the same
    /// sequence (day boundaries always fire per day, even collapsed).
    /// </summary>
    public static class DemographicDriverSchedule
    {
        /// <summary>Monthly drivers run on this day-of-month (fixed, documented).</summary>
        public const int MonthlyDriverDayOfMonth = 15;

        public static void EvaluateDay(DemographicDriverContext ctx)
        {
            BirthDriver.EvaluateBirths(ctx);
            MortalityDriver.EvaluateDay(ctx);
        }

        public static void EvaluateMonth(DemographicDriverContext ctx)
        {
            if (ctx == null)
            {
                return;
            }

            if (ctx.CurrentDate.DayOfMonth != MonthlyDriverDayOfMonth)
            {
                return;
            }

            BirthDriver.EvaluateConceptions(ctx);
            MarriageDriver.EvaluateMonth(ctx);
            MigrationDriver.EvaluateMonth(ctx);
            HouseholdSplitDriver.EvaluateMonth(ctx);
        }
    }

    /// <summary>
    /// Pass V P1-2: marriage formation between real persons.
    ///
    /// Monthly hazard by age with the 1890 medians as DISTRIBUTION ANCHORS
    /// (male peak 26, female peak 22 — calibration), never triggers: no one
    /// marries AT a threshold age. A smooth biological-plausibility ramp makes
    /// childhood marriage vanishingly rare without inventing a legal threshold
    /// (P3-3: marriage eligibility by jurisdiction/year is an explicit research
    /// hold, never an invented number).
    ///
    /// Marriage creates a real Spouse kinship edge and forms a new household
    /// from the two spouses (FormHousehold + TransferMember — the same Persons,
    /// never copies). Close kin are never paired.
    /// </summary>
    public static class MarriageDriver
    {
        public const double MaleAnchorAge = 26d;
        public const double FemaleAnchorAge = 22d;
        private const double HazardSigma = 7d;
        private const double PeakMonthlyHazard = 0.03;

        /// <summary>Monthly marriage hazard for one unmarried person (smooth, no thresholds).</summary>
        public static double MonthlyHazard(int age, bool isFemale)
        {
            double peak = isFemale ? FemaleAnchorAge : MaleAnchorAge;
            double distance = age - peak;
            double gaussian = Math.Exp(-(distance * distance) / (2d * HazardSigma * HazardSigma));
            // Biological-plausibility ramp: smooth, documented, not a legal rule.
            double youthRamp = Smoothstep(15d, 19d, age);
            double elderFade = age > 70 ? 0.2 : 1d;
            return PeakMonthlyHazard * gaussian * youthRamp * elderFade;
        }

        public static void EvaluateMonth(DemographicDriverContext ctx)
        {
            if (ctx == null || ctx.Population == null || ctx.Population.people == null || ctx.Kinship == null || ctx.Lifecycle == null)
            {
                return;
            }

            ctx.Anchor.FromAbsoluteDay(ctx.CurrentDayIndex, out int year, out int month, out int day);
            var people = ctx.Population.people;
            for (int i = 0; i < people.Count; i++)
            {
                PersonState candidate = people[i];
                if (!IsMarriageable(ctx, candidate))
                {
                    continue;
                }

                int age = candidate.GetTrueAge(year, month, day);
                double hazard = MonthlyHazard(age, candidate.isFemale);
                double draw = DemographicRng.Draw01(ctx.WorldSeed, ctx.CurrentDayIndex, candidate.id, "marriage");
                if (draw >= hazard)
                {
                    continue;
                }

                PersonState partner = FindPartner(ctx, candidate, age, year, month, day);
                if (partner == null)
                {
                    continue;
                }

                Marry(ctx, candidate, partner);
            }
        }

        /// <summary>
        /// Pass IX (A2-02, PASS6-003): the CURRENT spouse of a person — the
        /// living spouse on their kinship spouse edges, or -1 when none.
        /// Historical spouse edges are preserved forever (estate and kinship
        /// history depend on them and are never deleted); a widow(er)'s dead
        /// spouse simply stops being their CURRENT spouse, so remarriage and
        /// new conceptions become possible again.
        /// </summary>
        public static int CurrentSpouseId(DemographicDriverContext ctx, int personId)
        {
            if (ctx == null || ctx.Kinship == null || ctx.Population == null || personId < 0)
            {
                return -1;
            }

            return ctx.Kinship.GetCurrentSpouseId(personId, spouseId =>
            {
                PersonState spouse = ctx.Population.GetPerson(spouseId);
                return spouse != null && spouse.deathDayIndex < 0;
            });
        }

        /// <summary>Pass IX (A2-02): true when the person has a living spouse.</summary>
        public static bool HasCurrentSpouse(DemographicDriverContext ctx, int personId)
        {
            return CurrentSpouseId(ctx, personId) >= 0;
        }

        private static bool IsMarriageable(DemographicDriverContext ctx, PersonState person)
        {
            return person != null
                && person.deathDayIndex < 0
                && !person.isOffMap
                && !HasCurrentSpouse(ctx, person.id);
        }

        private static PersonState FindPartner(
            DemographicDriverContext ctx, PersonState candidate, int candidateAge,
            int year, int month, int day)
        {
            PersonState best = null;
            int bestAgeGap = int.MaxValue;
            var people = ctx.Population.people;
            for (int i = 0; i < people.Count; i++)
            {
                PersonState other = people[i];
                if (other == null || other.id == candidate.id || other.isFemale == candidate.isFemale)
                {
                    continue;
                }

                if (!IsMarriageable(ctx, other))
                {
                    continue;
                }

                if (AreCloseKin(ctx, candidate.id, other.id))
                {
                    continue;
                }

                int otherAge = other.GetTrueAge(year, month, day);
                int gap = Math.Abs(otherAge - candidateAge);
                if (gap > 12)
                {
                    continue;
                }

                if (gap < bestAgeGap || (gap == bestAgeGap && (best == null || other.id < best.id)))
                {
                    best = other;
                    bestAgeGap = gap;
                }
            }

            return best;
        }

        private static bool AreCloseKin(DemographicDriverContext ctx, int a, int b)
        {
            return ctx.Kinship.GetParents(a).Contains(b)
                || ctx.Kinship.GetParents(b).Contains(a)
                || ctx.Kinship.GetRelated(a, KinshipRelation.Sibling).Contains(b);
        }

        private static void Marry(DemographicDriverContext ctx, PersonState a, PersonState b)
        {
            ctx.Kinship.AddSpouses(a.id, b.id, "marriage");
            string surname = !string.IsNullOrWhiteSpace(a.lastName) ? a.lastName : b.lastName;
            // Pass VI cohesion fix: RegisterMembership rejects a second Active membership
            // (first wins), so A's old membership must end before FormHousehold runs.
            // Otherwise A stays in the old household while the new one is logged as formed.
            if (ctx.Memberships != null)
            {
                ctx.Memberships.EndMembership(a.id, ctx.CurrentDayIndex, "marriage");
            }
            HouseholdState household = ctx.Lifecycle.FormHousehold(
                $"{surname} Household", surname, a.id, ctx.CurrentDayIndex, HouseholdMembershipSource.Authored);
            if (household == null)
            {
                ctx.Diagnostics.Add($"MarriageDriver: FormHousehold refused for P{a.id} + P{b.id}.");
                return;
            }

            string rejection = ctx.Lifecycle.TransferMember(b.id, household.id, ctx.CurrentDayIndex);
            int homeBuildingId = ResolveHomeBuilding(ctx, a);
            household.homeBuildingId = homeBuildingId;
            a.homeBuildingId = homeBuildingId;
            b.homeBuildingId = homeBuildingId;
            ctx.Ledgers?.GetOrCreate(household.id);

            ctx.Diagnostics.Add(
                $"MarriageDriver: P{a.id} ({a.DisplayName}) married P{b.id} ({b.DisplayName}); " +
                $"new household H{household.id} formed from the same persons." +
                (rejection != null ? $" WARNING: transfer: {rejection}" : string.Empty));
        }

        private static int ResolveHomeBuilding(DemographicDriverContext ctx, PersonState person)
        {
            int householdId = ctx.Memberships != null
                ? ctx.Memberships.GetActiveHouseholdId(person.id)
                : person.householdId;
            HouseholdState household = householdId >= 0 ? ctx.Population.GetHousehold(householdId) : null;
            return household != null ? household.homeBuildingId : person.homeBuildingId;
        }

        private static double Smoothstep(double edge0, double edge1, double x)
        {
            double t = Math.Max(0d, Math.Min(1d, (x - edge0) / (edge1 - edge0)));
            return t * t * (3d - 2d * t);
        }
    }

    /// <summary>
    /// Pass V P1-2: migration with identity preservation.
    ///
    /// Out: distressed or opportunity-seeking adults leave the map. The Person
    /// record, PersonId, kinship edges, ownership, and debts are PRESERVED —
    /// only the household membership ends (reason "migrated off-map") and the
    /// isOffMap flag is set. Off-map persons keep aging (birthday scheduling
    /// includes them) and can return.
    ///
    /// In: rare arrivals join existing households as real persons with
    /// GeneratedExact birth dates (documented as generated, never as fact).
    /// </summary>
    public static class MigrationDriver
    {
        /// <summary>Monthly arrival probability for the whole town (rare, documented).</summary>
        public const double MonthlyArrivalProbability = 0.04;

        public static void EvaluateMonth(DemographicDriverContext ctx)
        {
            if (ctx == null || ctx.Population == null || ctx.Population.people == null || ctx.Lifecycle == null)
            {
                return;
            }

            ctx.Anchor.FromAbsoluteDay(ctx.CurrentDayIndex, out int year, out int month, out int day);
            var people = ctx.Population.people;
            for (int i = 0; i < people.Count; i++)
            {
                PersonState person = people[i];
                if (person == null || person.deathDayIndex >= 0 || person.isOffMap)
                {
                    continue;
                }

                int age = person.GetTrueAge(year, month, day);
                if (age < 18)
                {
                    continue;
                }

                bool distressed = person.laborUrgency01 > 0.9f;
                double probability = distressed ? 0.02 : 0.002;
                double draw = DemographicRng.Draw01(ctx.WorldSeed, ctx.CurrentDayIndex, person.id, "migrate-out");
                if (draw < probability)
                {
                    MigrateOut(ctx, person, distressed);
                }
            }

            double arrivalDraw = DemographicRng.Draw01(ctx.WorldSeed, ctx.CurrentDayIndex, -1, "migrate-in");
            if (arrivalDraw < MonthlyArrivalProbability)
            {
                MigrateIn(ctx, year, month, day);
            }
        }

        public static MigrationOutcome MigrateOut(DemographicDriverContext ctx, PersonState person, bool distressed)
        {
            var outcome = new MigrationOutcome
            {
                PersonId = person.id,
                Distressed = distressed,
                DayIndex = ctx.CurrentDayIndex,
            };
            ctx.Lifecycle.RemoveMember(person.id, ctx.CurrentDayIndex, distressed ? "migrated off-map (distress)" : "migrated off-map (opportunity)");
            person.isOffMap = true;
            // Pass IX (A2-10): migration ends local employment, mirroring the
            // death path (PASS6-012b). Otherwise the departed person's Active
            // employments draw full wages forever — no payroll path checked
            // isOffMap. Identity, kinship, ownership, and debts are preserved;
            // only the local job ends (reason MigratedOut, history retained).
            if (ctx.Employments != null)
            {
                outcome.EndedEmployments = ctx.Employments.EndEmploymentByReason(
                    person.id, EmploymentEndReason.MigratedOut, ctx.CurrentDayIndex);
                if (outcome.EndedEmployments.Count > 0)
                {
                    ctx.Diagnostics.Add(
                        $"MigrationDriver: ended {outcome.EndedEmployments.Count} employment(s) for departed P{person.id}.");
                }
            }

            ctx.Diagnostics.Add(
                $"MigrationDriver: P{person.id} ({person.DisplayName}) migrated off-map " +
                $"({(distressed ? "distress" : "opportunity")}); identity, kinship, ownership, and debts preserved.");
            return outcome;
        }

        private static void MigrateIn(DemographicDriverContext ctx, int year, int month, int day)
        {
            HouseholdState host = PickHostHousehold(ctx);
            if (host == null)
            {
                return;
            }

            double ageDraw = DemographicRng.Draw01(ctx.WorldSeed, ctx.CurrentDayIndex, -2, "migrant-age");
            int age = 18 + (int)(ageDraw * 28d) % 28;
            double sexDraw = DemographicRng.Draw01(ctx.WorldSeed, ctx.CurrentDayIndex, -3, "migrant-sex");
            bool isFemale = sexDraw < 0.5;
            double monthDraw = DemographicRng.Draw01(ctx.WorldSeed, ctx.CurrentDayIndex, -4, "migrant-birthmonth");
            // Birth month on or before the current month so the re-derived age
            // reproduces the drawn age (birth-date authority coherence).
            int birthMonth = 1 + (int)(monthDraw * month) % month;
            var person = new PersonState
            {
                id = ctx.Population.AllocateNextPersonId(),
                firstName = PickName(ctx, isFemale, -5),
                lastName = host.surname,
                isFemale = isFemale,
                birthDate = new BirthDate(
                    year - age, birthMonth, 1,
                    BirthDateProvenance.GeneratedExact,
                    "Runtime migrant arrival; internally generated, documented as generated."),
                age = age,
                ageBand = HouseholdLifecycleManager.AgeBandForAge(age),
                laborAccessLevel = LaborAccessFor(HouseholdLifecycleManager.AgeBandForAge(age)),
                householdId = host.id,
                homeBuildingId = host.homeBuildingId,
                scheduleState = PopulationScheduleState.AtHome,
                deathDayIndex = -1,
            };
            person.RefreshDerivedAge(year, month, day);
            person.EnsureWorkerTraitsInitialized(person.id);
            ctx.Population.people.Add(person);

            string rejection = ctx.Lifecycle.AddMember(host.id, person.id, ctx.CurrentDayIndex, HouseholdMembershipSource.Authored, "migrant arrival");
            string scheduleRefusal = BirthdayService.EnsureScheduledForPerson(
                person, ctx.Scheduler, ctx.Anchor, ctx.CurrentDayIndex, ctx.GameSecondsPerDay);
            ctx.Diagnostics.Add(
                $"MigrationDriver: P{person.id} ({person.DisplayName}, age {person.age}) arrived and joined H{host.id}." +
                (rejection != null ? $" WARNING: {rejection}" : string.Empty) +
                (string.IsNullOrEmpty(scheduleRefusal) ? string.Empty : $" WARNING: {scheduleRefusal}"));
        }

        private static HouseholdState PickHostHousehold(DemographicDriverContext ctx)
        {
            HouseholdState best = null;
            int bestCount = int.MaxValue;
            var households = ctx.Population.households;
            if (households == null)
            {
                return null;
            }

            for (int i = 0; i < households.Count; i++)
            {
                HouseholdState household = households[i];
                if (household == null || household.lifecycleState != HouseholdLifecycleState.Active)
                {
                    continue;
                }

                int count = household.memberIds != null ? household.memberIds.Count : 0;
                if (count < bestCount)
                {
                    best = household;
                    bestCount = count;
                }
            }

            return best;
        }

        private static string PickName(DemographicDriverContext ctx, bool isFemale, int salt)
        {
            string[] pool = isFemale ? ctx.FemaleNames : ctx.MaleNames;
            if (pool == null || pool.Length == 0)
            {
                return isFemale ? "Anna" : "Thomas";
            }

            double draw = DemographicRng.Draw01(ctx.WorldSeed, ctx.CurrentDayIndex, salt, "migrant-name");
            return pool[(int)(draw * pool.Length) % pool.Length];
        }

        private static LaborAccessLevel LaborAccessFor(AgeBand band)
        {
            switch (band)
            {
                case AgeBand.Child0To9: return LaborAccessLevel.None;
                case AgeBand.Helper10To12: return LaborAccessLevel.HouseholdHelper;
                case AgeBand.JuniorWorker13To15: return LaborAccessLevel.JuniorLowTrust;
                case AgeBand.YoungWorker16To17: return LaborAccessLevel.YoungWorker;
                default: return LaborAccessLevel.FullLaborMarket;
            }
        }
    }

    /// <summary>
    /// Pass V P1-2: household splitting. An adult child (18+, living with a
    /// parent, employed or married) may leave home — as the SAME Person, via
    /// FormHousehold + TransferMember. No new person is ever created for a
    /// departure.
    /// </summary>
    public static class HouseholdSplitDriver
    {
        /// <summary>Monthly split probability per eligible adult child.</summary>
        public const double MonthlySplitProbability = 0.05;

        public static void EvaluateMonth(DemographicDriverContext ctx)
        {
            if (ctx == null || ctx.Population == null || ctx.Population.people == null
                || ctx.Kinship == null || ctx.Lifecycle == null || ctx.Memberships == null)
            {
                return;
            }

            ctx.Anchor.FromAbsoluteDay(ctx.CurrentDayIndex, out int year, out int month, out int day);
            var people = ctx.Population.people;
            for (int i = 0; i < people.Count; i++)
            {
                PersonState person = people[i];
                if (person == null || person.deathDayIndex >= 0 || person.isOffMap)
                {
                    continue;
                }

                if (person.GetTrueAge(year, month, day) < 18)
                {
                    continue;
                }

                int householdId = ctx.Memberships.GetActiveHouseholdId(person.id);
                if (householdId < 0)
                {
                    continue;
                }

                if (!LivesWithParent(ctx, person.id, householdId))
                {
                    continue;
                }

                // Pass IX (A2-11): eligibility reads the employment REGISTRY —
                // the production hiring path (RecruitmentService) never writes
                // the legacy workplaceBuildingId field, so registry-employed
                // adult children previously read as unemployed and could
                // never split (same stale-field pattern as A2-01).
                bool employed = ctx.Employments != null
                    && ctx.Employments.GetActiveByEmployee(person.id).Count > 0;
                bool married = MarriageDriver.HasCurrentSpouse(ctx, person.id);
                if (!employed && !married)
                {
                    continue;
                }

                double draw = DemographicRng.Draw01(ctx.WorldSeed, ctx.CurrentDayIndex, person.id, "household-split");
                if (draw >= MonthlySplitProbability)
                {
                    continue;
                }

                SplitHousehold(ctx, person, householdId);
            }
        }

        private static bool LivesWithParent(DemographicDriverContext ctx, int personId, int householdId)
        {
            List<int> parents = ctx.Kinship.GetParents(personId);
            for (int i = 0; i < parents.Count; i++)
            {
                if (ctx.Memberships.GetActiveHouseholdId(parents[i]) == householdId)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SplitHousehold(DemographicDriverContext ctx, PersonState person, int oldHouseholdId)
        {
            HouseholdState oldHousehold = ctx.Population.GetHousehold(oldHouseholdId);
            // Pass VI cohesion fix: RegisterMembership rejects a second Active membership
            // (first wins), so the old membership must end BEFORE FormHousehold runs.
            // Otherwise FormHousehold returns a ghost household while the person stays
            // in the old one, and the split is logged as a false success.
            if (ctx.Memberships != null)
            {
                ctx.Memberships.EndMembership(person.id, ctx.CurrentDayIndex, "household split");
            }
            HouseholdState household = ctx.Lifecycle.FormHousehold(
                $"{person.lastName} Household", person.lastName, person.id,
                ctx.CurrentDayIndex, HouseholdMembershipSource.Authored);
            if (household == null)
            {
                ctx.Diagnostics.Add($"HouseholdSplitDriver: FormHousehold refused for P{person.id}.");
                return;
            }

            // Pass IX (A2-11): stamp the new household with the founder's
            // employer from the authoritative employment registry (the same
            // record that made the split eligible), for inspection/location.
            if (ctx.Employments != null)
            {
                List<EmploymentRelationship> active = ctx.Employments.GetActiveByEmployee(person.id);
                if (active.Count > 0 && active[0] != null)
                {
                    household.EmployerBusinessId = active[0].EmployerBusinessId ?? string.Empty;
                }
            }

            // The founder is already registered by FormHousehold; the split is
            // complete. Share the building (accommodation follows separately).
            household.homeBuildingId = oldHousehold != null ? oldHousehold.homeBuildingId : person.homeBuildingId;
            person.homeBuildingId = household.homeBuildingId;
            ctx.Ledgers?.GetOrCreate(household.id);
            ctx.Diagnostics.Add(
                $"HouseholdSplitDriver: P{person.id} ({person.DisplayName}) left H{oldHouseholdId} for new H{household.id}; same person, no duplicate.");
        }
    }
}
