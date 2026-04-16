Check GitHub Actions CI status across all SharpAstro repos and the chess project.

Use `gh run list` to show the latest CI run status for each repo:
- `SharpAstro/DIR.Lib`
- `SharpAstro/Console.Lib`
- `SharpAstro/SdlVulkan.Renderer`
- `sebgod/chess`

For each repo, show:
1. Latest run status (success/failure/in_progress)
2. Run conclusion and duration
3. Commit message that triggered it
4. If failed, show the failing step name

Use: `gh run list --repo <owner>/<name> --limit 1`

Format as a compact summary. Flag any failing or in-progress runs.
