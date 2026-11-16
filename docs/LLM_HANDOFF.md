# KIITOU: READ THIS FIRST (LLM HANDOFF)

**Purpose:** A comprehensive, source-grounded transfer of the product, locked requirements, pass plan, observed implementation, build failures, and visual-asset approval hierarchy to another LLM.

## Read in this order
1. [Product specification](PRODUCT_SPECIFICATION.md): what Kiitou is and what approved behavior is required.
2. [Approved 13-section task list](APPROVED_TASK_LIST.md): checklist, locked scope.
3. [Nine-pass implementation plan](IMPLEMENTATION_PLAN.md): sequencing, gates, audits, alteration approval, deliverables.
4. [Implementation status and build history](IMPLEMENTATION_STATUS.md): source files committed vs not implemented and actual verification limitations.
5. [Asset manifest](ASSET_MANIFEST.md): approved UI, approved seven icon family files, later concepts, light/dark requirement.
6. Pass audits `PASS_01_AUDIT.md` to `PASS_08_AUDIT.md` and the [verification matrix](VERIFICATION_MATRIX.md): what was built, checked and left open.
7. [Workflow](../.github/workflows/windows-build.yml) and [source tree](../Kiitou/): confirm current repository code rather than relying exclusively on prose.

## Priority rules
- User-approved requirements outrank exploratory concept images and implementation shortcuts.
- Do **not** reinterpret later theme concept screens as approved layout redesigns.
- The original approved UI screenshot has a generic K illustration; the later approved routed-K icon archive supersedes this placeholder **for icon artwork only**.
- Color themes blue, gold, green, purple, red, silver, teal must each have light and dark appearances. A matching icon should change when the color changes.
- Passes 1 to 8 are implemented and verified on automated evidence; Pass 9 is not done. Claim only what [VERIFICATION_MATRIX.md](VERIFICATION_MATRIX.md) records.
- User receives only the Windows executable when a pass is actually built; no ZIP as per-pass software delivery.
- Do not alter the approved pass count (9), task sequence, functional scope, or approved layout without explicit user approval.
- After every pass perform functional, technical and scope audits, report unresolved failures and obtain approval for substantive scope changes.

## Critical unresolved items
1. Pass 9 (release verification by a person on a clean Windows machine) has not been done. In particular the Windows elevation prompt has never been accepted or declined by hand.
2. The user amended many requirements on 2026-11-15 and 2026-11-16. Read [AMENDMENTS.md](AMENDMENTS.md) first; it overrides the specification, task list and pass audits where they differ.
3. Reputation checks: Quad9 is built in; AbuseIPDB and VirusTotal use the user's own keys and were tested only against simulated replies.
4. Window size is fixed at 1040 x 622.
5. The concept images, original icon ZIP and MANIFEST.json are still not in the repository; see [ASSET_TRANSFER.md](ASSET_TRANSFER.md). The approved icons and approved UI screenshot are in `assets/`.
6. No pass gate has been formally closed by the user.

## Recommended immediate handoff activity
Read [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md) and [VERIFICATION_MATRIX.md](VERIFICATION_MATRIX.md). Support the user through Pass 9, and resolve the open decisions listed in the status document with them. Do not add features.

No software scope changes are authorized by this documentation alone.
