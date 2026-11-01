# Land & Ledgers — Agent Rules and Repository Governance

## 1. Authority Hierarchy
When developing in this codebase, prioritize authority in the following order:
1. **Design & Architecture Authority**: Technical Bible, Canon, and Master Roadmap define intended architecture and feature requirements.
2. **Implementation Authority (Repository-First Rule)**: The actual code on disk in the repository is the absolute truth for what currently exists. Planning documents, roadmaps, and conceptual designs must *never* be assumed to be implemented without verifying against existing files, classes, and tests.
3. **Operational Documentation**: `AGENTS.md`, `REPOSITORY_MAP.md`, `AUTHORITY_MAP.md`, `DEPENDENCY_RULES.md`, and `MIGRATION_STATE.md` govern repository navigation, ownership, dependency rules, and architectural boundaries.

## 2. Baseline & Branching Rule
- All work must be anchored against the current branch and its latest durable checkpoint commit.
- Never hard-code temporary branch names as permanent policy; always query Git (`git branch --show-current`, `git status`) to establish baseline context before beginning work.

## 3. Unity Meta & Serialization Integrity
- **Unity Meta Preservation**: Every script, folder, and asset in Unity is tracked by a companion `.meta` file containing a unique GUID. When moving or renaming any file, its `.meta` file must be kept in sync. Never delete, regenerate, or casually modify a `.meta` file.
- **Serialized Types & Fields**: Never casually rename classes, structs, enums, or serialized fields. Doing so breaks references inside Unity scenes (`.unity`), prefabs (`.prefab`), and ScriptableObjects (`.asset`).
- **Namespace Stability**: Namespaces were intentionally preserved during repository migrations to maintain assembly and code compatibility. Do not perform ad-hoc namespace alterations without explicit project-wide migration mandates.

## 4. Repository Structure & Folder Ownership
Code must reside strictly within its designated folder hierarchy:
- `Core/`: Truly foundational, cross-domain primitives (e.g., simulation clock in `Core/Time`). Avoid using `Core` as a junk drawer.
- `Domains/`: Domain-specific business logic, state models, and domain simulations (e.g., `Domains/Economy`, `Domains/World`, `Domains/Population`).
- `Infrastructure/`: External integrations, disk I/O, and persistence mechanisms (e.g., `Infrastructure/Persistence`).
- `Orchestration/`: Cross-domain coordination, game boot sequencing, and player guidance (e.g., `Orchestration/Bootstrap`).
- `Presentation/`: Rendering helpers, cameras, audio, and generic UI components. Feature-specific presentation lives alongside the feature within its domain (e.g., `Domains/Economy/Businesses/GeneralStore/Presentation`).
- `ReadModels/`: Read-only reporting, ledger projections, and analytical snapshots (e.g., `ReadModels/Reporting`).
- `Tests/`: Automated tests residing under `Tests/Editor/<Domain>`.

## 5. Engineering Practices
- **Targeted Changes over Rewrites**: Make surgical, minimal diffs. Do not rewrite functioning legacy code or refactor unrelated subsystems while implementing a task.
- **No Broad Architectural Shifts**: Do not introduce Assembly Definition Files (`.asmdef`), custom event buses, dependency injection frameworks, or broad abstractions unless explicitly assigned.
- **Inspect Relevant Domains First**: Prior to editing, thoroughly inspect the domain's existing models, authorities, and test suites.
- **Legacy Folders are Stubs**: Former top-level directories (e.g., `Economy/`, `World/`, `UI/`) exist only as empty folder/meta stubs. Never add new code to legacy root paths.

---

## Before Editing Checklist
Before writing or modifying any code in Land & Ledgers, verify:
- [ ] Checked `AUTHORITY_MAP.md` to identify the current authoritative manager for this domain.
- [ ] Confirmed the class/method actually exists in the codebase (did not rely solely on planning docs).
- [ ] Verified the target directory adheres to `REPOSITORY_MAP.md` and `DEPENDENCY_RULES.md`.
- [ ] Confirmed that companion `.meta` files and GUIDs will not be deleted or modified unintentionally.
- [ ] Ensured no serialized types, serialized fields, or namespaces are being casually renamed.
- [ ] Checked existing tests under `Tests/Editor/<Domain>` covering this feature area.
- [ ] Kept proposed modifications minimal, targeted, and free of scope creep.
