# TODO

## Planned work (design docs)

Each of these has a doc under `docs/` carrying its own **Status** header and **Phasing** table — those
are the source of truth for progress, not this list. The gaps below the line are unplanned known
defects; these are planned changes.

- [**Play by Link on the desktop**](docs/desktop-link-play.md) — the README's Play by Link feature says
  "(browser)"; the native app can't consume a game link at all. Phase 1 (paste/argv + reply link) needs
  no new plumbing — the codec, the wizard entry and the clipboard all already exist. Phase 2 adds a
  `chess://` scheme with a single-instance hand-off. *Not started.*
- [**Content→device transform**](docs/content-transform.md) — DPI and rotation unified as one
  constrained affine map, which is what the Android "across the table" flip is built on. *Phases 1a and
  2 done; WebGL compose and the CPU backend pending.*
- [**Setup-mode drag ghost**](docs/drag-ghost.md) — the dragged piece follows the cursor in setup
  mode. *All four phases **done**: `GameUI` holds the ghost and states its damage, and the terminal,
  the GUI, Android and the browser all feed it motion. Live-verified in the GUI (renderer inspector)
  and in a real browser (Playwright, the suite's only pixel-reading tests). **Not** verified on an
  Android device, and the terminal has no live route at all — Console.Lib's inspector cannot
  synthesize pointer motion, which is the one follow-up this left behind.*
- [**Second board game / game-library carve-out**](docs/game-library.md) — what would actually have to
  be extracted for a second game (Skat, Memory) to share this repo's turn model, wizard, frame and LAN
  lobby. *Design only.*

## Console Input

### ASCII mode requires a real terminal
`Console.KeyAvailable` throws `InvalidOperationException` when stdin is redirected
(e.g., piped or launched from a non-interactive context). The app builds and starts
correctly but crashes in `VirtualTerminal.InitAsync()`. Needs a guard or fallback
for redirected stdin scenarios.

## Missing Draw Rules

### Fifty-Move Rule
If 50 consecutive moves (100 half-moves/plies) pass without a pawn move or capture,
either player may claim a draw. At 75 moves (150 plies) it becomes automatic.
Needs a halfmove clock: reset on pawn moves and captures, incremented otherwise.
The halfmove clock is also part of the FEN standard (5th field).

### Threefold Repetition
If the same position occurs three times with the same side to move, castling rights,
and en passant square, either player may claim a draw. At fivefold repetition it
becomes automatic. Needs a position history keyed by board state + side + castling
rights + en passant.

### Insufficient Material
Automatic draw when neither side can deliver checkmate:
- King vs King
- King + Bishop vs King
- King + Knight vs King
- King + Bishop vs King + Bishop (same-colored bishops)

### Dead Position
A generalization of insufficient material — drawn if no sequence of legal moves can
lead to checkmate. Rare beyond the insufficient material cases.
