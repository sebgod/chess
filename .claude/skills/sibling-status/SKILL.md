---
name: sibling-status
description: Show git status, current branch, and last commit across all SharpAstro sibling repos and the chess project. Use when the user asks "what's the state of the siblings", "any uncommitted sibling changes", or before a release chain.
---

Show git status, current branch, and last commit across all SharpAstro sibling repos and the chess project.

Check each of these repos:
- `../../sharpastro/Fonts.Lib` (SharpAstro.Fonts)
- `../../sharpastro/Codecs` (SharpAstro.Codecs, SharpAstro.Png, … — default branch is `master`)
- `../../sharpastro/DIR.Lib` (DIR.Lib)
- `../../sharpastro/Console.Lib` (Console.Lib)
- `../../sharpastro/SdlVulkan.Renderer` (SdlVulkan.Renderer)
- `.` (chess - current repo)

For each repo, show:
1. Current branch name
2. Commits ahead/behind remote (if tracking; `git fetch` first so behind counts are real)
3. Any uncommitted changes (short status)
4. Last commit message (one line)
5. The VersionPrefix from the main .csproj (or from Directory.Packages.props for chess)

Format as a compact table. Flag any repo that has uncommitted changes or is ahead of remote.
