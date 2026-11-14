# Kiitou Asset Transfer Record

## What is in the repository
Verified on 2026-11-14 by extracting the uploaded `Kiitou_Asset_Handoff.zip` (commit fab1497) without altering any bytes:

- `assets/icons/`: 7 ICO files, 7 PNG masters and README.txt (the approved routed-K icon pack). Each ICO was confirmed to contain 7 images.
- `assets/ui/approved/kiitou_approved_primary_ui.png`: the approved primary UI screenshot, 1536 x 1024.

The uploaded archive had SHA-256
41758a226e7c15c90cae0b799061acb186b547f597cbadd70c9ba42f9df1cff8
and 16 members. After extraction it was removed from the repository root; it remains in git history at commit fab1497.

## What is still missing
The archive described in the original handoff (SHA-256 159ca5a930df659f7ddd6dfa27daa9d1a668aab497c75ff6c08aa42917aca93d, 32 members) is a different, larger file and has **not** been uploaded. Not present in the repository:

- 7 later themed exploratory concepts -> `assets/ui/theme-concepts/`
- 7 earlier concept images -> `assets/ui/explorations/`
- the original approved icon ZIP -> `assets/source-archives/`
- MANIFEST.json with per-file SHA-256 checksums

Because MANIFEST.json is absent, the committed files could not be compared against the original per-file checksums. These missing items are references only; none is needed to build Kiitou.

## Approval
The single primary original UI image is the approved layout. The separate themed and earlier UI images are merely visual references. The seven icon images are approved. Do not interpret screenshot sizes as Kiitou's final window dimensions.
