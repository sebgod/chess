# TODO

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
