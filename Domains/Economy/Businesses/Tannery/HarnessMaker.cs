using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using UnityEngine;

namespace LandLedgers.Economy.Tannery
{
    /// <summary>
    /// NX-1B drive-by: harness crafting recipe. The tannery unblocked leather
    /// (TanningBatch → LeatherLot); this closes the saddler/harness-maker
    /// capability from the EQP survey: leather + iron hardware (buckles, rings
    /// — blacksmith upstream) + thread becomes a work harness EquipmentAsset,
    /// which the EQU-3 DraftWorkUnit needs to put a horse to an implement.
    /// Provenance carries: hide source → leather lot → harness.
    /// </summary>
    public sealed class HarnessMaker
    {
        /// <summary>Leatherworking skill id (TTS-3 extension path).</summary>
        public const string LeatherworkingSkillId = "leatherworking";

        /// <summary>Calibration: lbs of leather per full work harness.</summary>
        public const int LeatherLbsPerHarness = 12;
        /// <summary>Calibration: iron buckles/rings per harness (blacksmith hardware).</summary>
        public const int BucklesPerHarness = 4;
        /// <summary>Calibration: thread units per harness.</summary>
        public const int ThreadUnitsPerHarness = 2;
        /// <summary>Journeyman floor (Canon §7.2) for harness work.</summary>
        public const int JourneymanLevelFloor = 2;

        private readonly string businessInstanceId;
        private readonly string businessName;
        private int nextHarnessNumber = 1;

        public HarnessMaker(string businessInstanceId, string businessName)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.businessName = businessName ?? string.Empty;
        }

        /// <summary>Registers the leatherworking skill via the TTS-3 extension path.</summary>
        public static void RegisterSkills(SkillService skillService, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (skillService == null)
            {
                diagnostics.Add("HarnessMaker: no SkillService — leatherworking skill not registered.");
                return;
            }
            if (skillService.GetSkill(LeatherworkingSkillId) == null)
            {
                string rejection;
                if (!skillService.RegisterSkill(
                    new SkillDefinition(LeatherworkingSkillId, "Leatherworking",
                        "Cutting, stitching, and riveting leather goods — harness, tack, and repair. Registered by the harness maker (TTS-3 extension path)."),
                    out rejection))
                {
                    diagnostics.Add("HarnessMaker: leatherworking skill rejected: " + rejection);
                }
            }
        }

        /// <summary>
        /// Crafts one work harness from a leather lot + hardware + thread.
        /// Consumes the leather (lot lbs reduced) and the hardware/thread
        /// stocks passed in. Returns the harness asset, or null with a
        /// diagnostic when the recipe cannot be honestly filled.
        /// </summary>
        public EquipmentAsset CraftHarness(
            TanneryRuntime.LeatherLot leatherLot,
            ref int buckleUnitsOnHand,
            ref int threadUnitsOnHand,
            int saddlerPersonId,
            int dayIndex,
            SkillService skillService,
            EquipmentTaskGate gate,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (leatherLot == null || leatherLot.Lbs < LeatherLbsPerHarness)
            {
                diagnostics.Add($"HarnessMaker: need a leather lot with ≥{LeatherLbsPerHarness} lbs — have {(leatherLot == null ? 0 : leatherLot.Lbs)}.");
                return null;
            }
            if (skillService == null)
            {
                diagnostics.Add("HarnessMaker: no SkillService — cannot verify the saddler.");
                return null;
            }
            EntityId saddler = EntityId.For(EntityKind.Person, saddlerPersonId);
            if (skillService.GetLevel(saddler, LeatherworkingSkillId) < JourneymanLevelFloor)
            {
                diagnostics.Add($"HarnessMaker: saddler {saddlerPersonId} below journeyman leatherworking — the harness would fail in the field (Canon §7.2).");
                return null;
            }
            if (buckleUnitsOnHand < BucklesPerHarness)
            {
                diagnostics.Add($"HarnessMaker: short {BucklesPerHarness - buckleUnitsOnHand}x iron buckles — blacksmith hardware is upstream (canon: upstream provenance).");
                return null;
            }
            if (threadUnitsOnHand < ThreadUnitsPerHarness)
            {
                diagnostics.Add($"HarnessMaker: short {ThreadUnitsPerHarness - threadUnitsOnHand}x thread.");
                return null;
            }

            // Equipment gate: the harness-maker's kit (awls, punches, needles —
            // EQP survey leatherworking profile; wheelwright-kit covers it).
            if (gate != null)
            {
                var codes = new List<string> { "kit:wheelwright-kit" };
                string problem = gate.CheckCodes(codes, "business", businessInstanceId, dayIndex, diagnostics);
                if (problem != null)
                {
                    diagnostics.Add("HarnessMaker: refused — " + problem);
                    return null;
                }
            }

            leatherLot.Lbs -= LeatherLbsPerHarness;
            buckleUnitsOnHand -= BucklesPerHarness;
            threadUnitsOnHand -= ThreadUnitsPerHarness;

            var asset = new EquipmentAsset
            {
                AssetId = $"HRN-{businessInstanceId}-{nextHarnessNumber++:D3}",
                Kind = "harness",
                DisplayName = "Work Harness",
                Condition01 = 1f,
                OwnerKind = "business",
                OwnerId = businessInstanceId,
                MadeByBusinessId = businessInstanceId,
                MadeByBusinessName = businessName,
                MadeDayIndex = dayIndex,
            };
            asset.MaterialLotIds.Add(leatherLot.LotId);
            diagnostics.Add(
                $"HarnessMaker: crafted {asset.DisplayName} ({asset.AssetId}) from lot {leatherLot.LotId} " +
                $"({LeatherLbsPerHarness} lbs leather + {BucklesPerHarness} buckles + {ThreadUnitsPerHarness} thread) — hide provenance {leatherLot.HideSourceNote}.");
            return asset;
        }
    }
}
