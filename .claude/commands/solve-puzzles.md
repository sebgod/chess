Solve chess puzzles from a PDF file attached to the conversation.

Usage: /solve-puzzles
(Attach a PDF containing chess diagrams to the message)

Steps:

1. **Parse the PDF** — Extract chess positions from the PDF. Look for:
   - XABCDEFGHY diagram notation (common in chess puzzle books)
   - FEN strings if provided directly
   - Diagram images with piece placement indicators
   - Side-to-move indicators (e.g. "White to play", "Black to play", "w" or "b")
   - Puzzle type indicators (e.g. "Mate in 3", "Checkmating Nets")

2. **Convert to FEN** — For each puzzle, construct the FEN placement string:
   - XABCDEFGHY notation: ranks go from 8 (top) to 1 (bottom)
   - Piece codes: K=king, Q=queen, R=rook, B=bishop, N=knight, P=pawn
   - In XABCDEFGHY: uppercase = White, lowercase = Black (but `0` prefix = White, no prefix = Black in some encodings)
   - Verify piece counts are reasonable (max 16 per side, max 8 pawns)
   - Note: puzzle positions do NOT need to be reachable from a legal game

3. **Determine puzzle parameters**:
   - Which side moves first (from PDF context or explicit marking)
   - Puzzle goal: mate-in-N, best move, mating net, etc.
   - If unclear, default to "find best move" analysis

4. **Solve each puzzle** using the chess MCP tools in this order:
   a. `chess-display_board` — show the position visually for verification
   b. `chess-analyze_position` — get initial assessment (checks, threats)
   c. `chess-solve_mate_in` — if puzzle is "mate in N", try solving directly (mateIn: N)
   d. `chess-find_best_move` — find the engine's best move at depth 5+
   e. If mate not found at expected depth, try deeper (up to depth 8)
   f. For "mating net" puzzles: the first move may set up an unstoppable promotion
      or forced sequence — analyze the resulting position after the best move

5. **Report solutions** in a formatted table:
   | # | Position (White/Black) | Side | Solution | Type |
   Include:
   - The key first move in algebraic notation (e.g. "Nf6", "exf7")
   - UCI format in parentheses (e.g. "d5f6", "e6f7")
   - Brief explanation of the mating mechanism
   - Evaluation score or "Mate in N"

6. **Handle edge cases**:
   - If engine can't find forced mate, report best move with evaluation
   - For underpromotion puzzles (f8=N#, etc.), the engine now generates all
     promotion types — verify the mating line includes the correct piece
   - If a position seems invalid, flag it but still attempt to solve

7. **Verify solutions** — For each "mate in N" solution:
   - Use `chess-make_move` to play the first move
   - Confirm the resulting position maintains the mating threat
   - For mating nets, verify the opponent cannot prevent the threat

Present all solutions together at the end in a clear summary.
