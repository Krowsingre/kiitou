# Kiitou Pass 4 Audit: Profiles

Pass 4 delivers profile management and import/export as tested library code. The user
interface for it belongs to Pass 7.

## Implemented

| Requirement (task list 5, 11) | Where |
| --- | --- |
| Create, rename, duplicate, delete | `ProfileManager.Create`, `Rename`, `Duplicate`, `Delete` |
| Save enabled website combinations and selected IPs | `ProfileManager.SaveSelections` |
| Default profile with none enabled | Created with every new configuration; a new profile also starts with nothing enabled |
| Save to the active profile or to a new profile; no silent overwrite | Profiles change only through `SaveSelections` or `SaveAsNew`; edits are made on a separate working copy |
| Switch and apply exact profile mappings | `ProfileManager.SetActive`, `BuildMappings` (fed to the Pass 2 hosts engine) |
| Import and export profiles | `ProfileTransfer.ExportProfile`, `ImportProfile` |
| Collections import/export | `ProfileTransfer.ExportCollection`, `ImportCollection` |
| Configuration independent from applied hosts state | Import, export, save and switch never touch the hosts file |

Export files identify websites and addresses by value, so they work on another computer.
Import validates the whole file first and changes nothing if any part is invalid. It only adds
missing websites and candidate addresses; it never removes or overwrites saved data, and an
imported profile gets a unique name instead of replacing an existing one.

Rules chosen within scope: at least one profile always exists (the last one cannot be
deleted); names are unique ignoring case and at most 60 characters; deleting the selected
profile selects the first remaining one.

## Audit 1: Functional

Automated tests cover name rules, create/rename/duplicate/delete, that unsaved working changes
never alter a saved profile, save-as-new, exact mappings per profile when switching,
persistence, export then import into an empty configuration, merging into an existing one,
collection round trips, and eleven kinds of invalid import file.

## Audit 2: Technical

- Saving drops choices for websites or addresses that were deleted meanwhile.
- Deleting a profile clears the record of it being the last applied profile.
- Imported profile names are stripped of control characters.

## Audit 3: Scope

Only profile and import/export behavior was added. No DNS, testing or UI behavior was added.

## Regression

All earlier tests and smoke checks still run in the same build.

## Pass gate

**Passed on automated evidence** (see the build for this pass). No alteration to the approved
scope is requested.
