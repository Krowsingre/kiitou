# Kiitou Asset Transfer Procedure
The source art is supplied as a complete byte-preserving handoff archive titled Kiitou_Asset_Handoff.zip in the originating ChatGPT conversation. It is NOT yet in GitHub.

## Archive verification
Archive SHA-256:
159ca5a930df659f7ddd6dfa27daa9d1a668aab497c75ff6c08aa42917aca93d

Archive contents: 15 approved-icons items (7 ICO, 7 PNG, 1 README); 1 explicitly approved primary UI screenshot; 7 later themed exploratory concepts; 7 earlier concept images; 1 original approved icon ZIP; plus MANIFEST.json containing per-file SHA-256 checksums. Total 32 archive members. Original bytes are preserved, not resynthesized.

## Intended destinations
- approved-icons/* -> assets/icons/*
- approved-ui/* -> assets/ui/approved/*
- theme-concepts/* -> assets/ui/theme-concepts/*
- initial-concepts/* -> assets/ui/explorations/*
- archive/* -> assets/source-archives/*

## Handoff to a binary-capable operator
1. Obtain the exact archive from the project chat.
2. Confirm the whole-file SHA-256 above.
3. Extract the ZIP.
4. Copy its files into the destination paths above, preserving content.
5. Commit/push binaries via authenticated Git, GitHub Desktop, or another connector that supports local binary uploads.
6. Re-read each committed file, compare it with MANIFEST.json by SHA-256, and update this handoff document with the verified commit SHA.
7. Do not claim assets were uploaded until repository paths exist and the hashes match.

## Approval
The single primary original UI image is the approved layout. The separate themed and earlier UI images are merely visual references. The seven ICON images are approved. Do not interpret screenshot sizes as Kiitou's final window dimensions.
