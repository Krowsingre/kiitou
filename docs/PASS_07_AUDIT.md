# Kiitou Pass 7 Audit: Windows UI, Themes and Icons

Pass 7 replaces the bootstrap window with the approved layout and connects it to the logic
delivered in Passes 2 to 6 through a session layer (`KiitouSession`).

## Implemented

| Requirement (task list 2, plus UI for 3 to 11) | Where |
| --- | --- |
| Approved layout: header with icon, name, profile picker, Apply Selected Profile and state indicator | `MainWindow.xaml` header |
| Left navigation: Home, Websites, Profiles, Discovery & Test, Verification, Hosts File, Settings | `Nav` list; one view each |
| Home toolbar: Add Website, Edit, Remove, Enable All, Disable All, Discover DNS, Test Addresses | Home view |
| Website table: enabled checkbox, hostname, selected-address drop-down, address count, status | `HomeGrid` |
| Bottom profile summary with Save to Current Profile, Save as New Profile, Import, Export | Home card |
| Status footer: hosts state, profile, last applied | Footer |
| Not fullscreen; 720 x 450 lower bound | Opens at 960 x 620; cannot be made smaller than 720 x 450 |
| Search and filter | Search box and "Show" filter above the Home table |
| Entry statuses | The four statuses with icon and color everywhere an address is listed; "(stale)" after 24 hours |
| Optional tray | Settings: notification-area icon that hides the window when minimized; off by default |
| Seven color families x dark/light = 14 themes | `ThemeManager`; chosen in Settings, saved in the configuration |
| Icon changes with the color family; dark/light keeps it | Window, taskbar, header and notification-area icons use the approved ICO of the active family |

The seven approved ICO files are embedded in `Kiitou.exe`. The executable's own file icon is the
purple one and does not change, as the specification anticipates.

## Decisions made within scope

- **One address drop-down per row on Home**, as in the approved layout. It shows the IPv4
  choice, or the IPv6 choice when there is no IPv4 one. Picking an address sets it for that
  address's family; "Not Assigned" clears both. When both families are chosen the row says
  "also IPv6 ...". Separate IPv4 and IPv6 drop-downs are on the Websites view.
- **The badge beside the profile name reads "Selected" or "Unsaved changes"**; the header
  indicator reads "Profile Active" only when the hosts file holds exactly the saved profile's
  mappings, otherwise "Not Applied" or "No Mappings".
- **Apply uses the profile as saved.** With unsaved changes, Kiitou asks whether to save first.
- **Upper size reference (Whisper Light) is still unknown**, so no maximum size is enforced.
- Icons in buttons use the Segoe MDL2 Assets font that ships with Windows 10 and 11.

## Audit 1: Functional

On the Windows build runner the published `Kiitou.exe` was driven through UI Automation:

- every one of the seven views was opened and its main control confirmed visible;
- the window was asked to shrink to 300 x 200 and stopped at exactly 720 x 450;
- the application was started and closed cleanly in all fourteen theme combinations, and once
  with the notification-area icon enabled;
- the configuration was unchanged after the tour.

Screenshots of every view and theme were captured from the runner and reviewed. Defects found
in that review and corrected in this pass: light scroll bars in dark themes, toolbar wrapping
at the default size, truncated status text without a tooltip, and the missing search and filter.

Eleven session tests cover unsaved versus saved choices, apply using the saved profile,
switching, removal and restore, outside edits and conflicts, discovery and testing never
selecting or applying, theme persistence and all fourteen combinations, and import/export.

## Audit 2: Technical

- No compiler warnings.
- Windows Forms is referenced only for the notification-area icon.
- Long operations run in the background with a Cancel button; changes are blocked while one runs.

## Audit 3: Scope

No feature outside the task list was added. Decisions listed above are interpretations of
existing requirements, recorded for review.

## Regression

All earlier tests and smoke checks still pass in the same build.

## Not verified

- Clicking through dialogs, applying to the hosts file from the UI, and the UAC prompt. The
  apply flow from the UI is exercised in Pass 8.
- Appearance at display scaling other than 100% (the runner's setting).
- How closely colors match the approved screenshot by eye; the approved layout and controls
  are reproduced, the exact shades are approximations.

## Pass gate

**Passed on automated evidence and screenshot review.** No alteration to the approved scope is
requested.
