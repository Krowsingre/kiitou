# Kiitou Pass 2 Audit: Hosts Engine

Pass 2 delivers the hosts-file engine as tested library code plus the privileged helper
mode of `Kiitou.exe`. There is no user interface for it yet; that belongs to Pass 7.

## Implemented

| Requirement (task list 9, 10, 11) | Where |
| --- | --- |
| Read the Windows hosts file; identify Kiitou-managed lines | `HostsFileService.Read`, `ManagedHostsBlock.Analyze` |
| Apply selected mappings; remove only Kiitou-managed lines | `ManagedHostsBlock.Apply` |
| Preserve unrelated entries and modifications | Lines outside the managed section, and untagged lines inside it, are copied byte for byte including line endings |
| Back up before writing; first-change backup; restore | `HostsBackupStore` (permanent `hosts-original.bak` plus 20 rotating copies), `HostsApplyService.Restore` |
| Detect external edits | Hash recorded after each write (`HostsState.LastWrittenSha256`); hash re-checked immediately before writing |
| Replace safely | Temporary file plus `File.Replace`, with in-place rewrite as fallback; read-only attribute handled |
| Flush DNS after changes; verify written state | `DnsCache.Flush`; verification in the helper and again from the unelevated side |
| Configuration editing without elevation | Nothing outside the single write needs elevation |
| UAC only for the privileged hosts write | `ElevatedHostsWriter` starts `Kiitou.exe --hosts-helper <request>` with the `runas` verb |
| Cancelled or denied elevation leaves hosts untouched | Cancellation maps to `HostsWriteOutcome.Cancelled`; no write is attempted |
| Conflicts | `ManagedHostsBlock.FindConflicts` reports unmanaged lines that map the same hostname and family elsewhere |

The privileged helper accepts only two operations (apply a validated mapping list, or restore
supplied bytes), always targets the system hosts file, and rejects any mapping that is not a
strictly normalized IP address and hostname.

## Audit 1: Functional

78 new automated tests (96 total) ran on the Windows build runner, covering: block insertion,
idempotence, exact restoration of the original bytes, mixed and missing line endings, non-ASCII
bytes, damaged markers, foreign lines inside the block, conflicts, backups and rotation, restore,
read-only hosts files, stale-hash refusal, cancelled elevation, false success detection, and
request validation.

Against the **real** hosts file of the build runner (a disposable machine):
- a unit test applied a mapping, confirmed Windows resolved the name to it, and removed it;
- the smoke test ran the published `Kiitou.exe --hosts-helper` for a stale hash (refused, exit 3),
  an invalid mapping (refused, exit 2), a valid apply (exit 0, name resolved), and a removal.

## Audit 2: Technical

- No compiler warnings.
- Hosts text is decoded as Latin-1 so unrelated bytes always round-trip; UTF-16 files are refused.
- A missing end marker cannot delete user lines: only marker lines and `# Kiitou`-tagged lines
  inside the section are ever removed.
- The request file passed to the helper cannot make it write anything except validated mappings
  or the restore content the user chose.

## Audit 3: Scope

Only hosts-engine, permission and backup/restore items were implemented. No website, profile,
DNS, testing or UI behavior was added. Hostname and IP syntax rules were added here because the
privileged helper must validate what it writes; they are reused by Pass 3.

## Regression

All Pass 1 tests and the Pass 1 smoke checks still pass.

## Not verified

- The Windows elevation prompt itself. The build runner is already elevated, so no prompt
  appears there. Accepting and declining the real prompt must be checked by a person.
- Behavior with third-party security software that blocks hosts-file changes.

## Pass gate

**Passed on automated evidence**, with the interactive elevation prompt outstanding for manual
confirmation. No alteration to the approved scope is requested.
