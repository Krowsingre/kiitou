# KIITOU: READ THIS FIRST (LLM HANDOFF)

**Purpose:** A comprehensive, source-grounded transfer of the product, locked requirements, pass plan, observed implementation, build failures, and visual-asset approval hierarchy to another LLM.

## Read in this order
1. [Product specification](PRODUCT_SPECIFICATION.md): what Kiitou is and what approved behavior is required.
2. [Approved 13-section task list](APPROVED_TASK_LIST.md): checklist, locked scope.
3. [Nine-pass implementation plan](IMPLEMENTATION_PLAN.md): sequencing, gates, audits, alteration approval, deliverables.
4. [Implementation status and build history](IMPLEMENTATION_STATUS.md): source files committed vs not implemented and actual verification limitations.
5. [Asset manifest](ASSET_MANIFEST.md): approved UI, approved seven icon family files, later concepts, light/dark requirement.
6. [Pass 1 audit](PASS_01_AUDIT.md): original phase audit and Windows verification caveat.
7. [Workflow](../.github/workflows/windows-build.yml) and [source tree](../Kiitou/): confirm current repository code rather than relying exclusively on prose.

## Priority rules
- User-approved requirements outrank exploratory concept images and implementation shortcuts.
- Do **not** reinterpret later theme concept screens as approved layout redesigns.
- The original approved UI screenshot has a generic K illustration; the later approved routed-K icon archive supersedes this placeholder **for icon artwork only**.
- Color themes blue, gold, green, purple, red, silver, teal must each have light and dark appearances. A matching icon should change when the color changes.
- Current source is only Pass 1 bootstrap; do not claim completed operations, tests or deliverable exe.
- User receives only the Windows executable when a pass is actually built; no ZIP as per-pass software delivery.
- Do not alter the approved pass count (9), task sequence, functional scope, or approved layout without explicit user approval.
- After every pass perform functional, technical and scope audits, report unresolved failures and obtain approval for substantive scope changes.

## Critical unresolved items
1. Windows build failures: obtain actual Actions run/job logs; previous connector queries didn't surface push-run logs, so do not guess the cause.
2. Pass 1 Windows startup/config/storage testing incomplete.
3. Whisper Light maximum-size reference unknown. Ghostblood minimum reference cited as 720x450.
4. Original binary assets were prepared as a separate exact-byte handoff package in the conversation. Their binary bytes are **not uploaded to this GitHub tree** by the available connector. Manifest documents sources and intended paths, not verified repository presence.
5. Four security statuses must not conflate reachability with authenticity or safety.

## Recommended immediate handoff activity
Inspect repo, retrieve failed CI run logs, repair within locked scope, build and validate the initial self-contained Kiitou.exe, report results. Upload exact original art using a binary-capable authenticated Git client/workflow when available, then verify SHA-256 checksums. Pass 2 remains blocked until the Pass 1 gate is cleared.

No software scope changes are authorized by this documentation alone.
