Solve chess puzzles from a PDF file attached to the conversation.

Usage: /solve-puzzles [--output DIR]
(Attach a PDF containing chess diagrams to the message)

Note: MCP supports drawing boards — use `chess-render_board_png` with `savePath` to save images directly.

Steps:

1. **Extract PDF text** — Use `pdftotext` or `python -c "import PyPDF2; ..."` to get plaintext:
   ```bash
   pdftotext "file.pdf" -
   # or:
   python -c "import PyPDF2; r = PyPDF2.PdfReader('file.pdf'); print('\n'.join(p.extract_text() or '' for p in r.pages))"
   ```

2. **Parse XABCDEFGHY notation** — Convert board diagrams to FEN. The format is:

   **Board lines:** Each line is `RANK_LABEL + INNER + BORDER_CHAR(s)` where `INNER` contains 8 squares' worth of glyphs, possibly mixed with decorative markers and spaces.
   - `+` and `-` are empty squares (alternate for visual contrast)
   - Pieces are encoded as a piece letter (`KQRBNPLkqrbnpl`) plus 0+ decorative markers (any of `mwtvz` and ASCII space) appearing before AND/OR after the piece letter. Piece tokens may even straddle a space (e.g. `pz k` = pawn + space + king markers).

   **Piece decoding:**
   - `K/k`=King, `Q/q`=Queen, `R/r`=Rook, `L/l`=Bishop (German "Läufer" → FEN B/b), `N/n`=Knight, `P/p`=Pawn
   - Uppercase = White, lowercase = Black. Decorative markers (m, w, t, v, z, space) carry no meaning — discard them.

   **Parsing algorithm — keep it dumb and filter:**

   The original "tokenize markers" approach is fragile because pieces have markers as both prefix (`mK`, `vL`, `tR`) and suffix (`pz`, `kv`), and tokens can be split by spaces (`pz k`). The robust algorithm: **filter the inner string to keep only `+`, `-`, and piece chars; everything else is a marker.** Each kept char is exactly one square.

   ```python
   PIECE_CHARS = set('KQRBNPLkqrbnpl')
   EMPTY_CHARS = set('+-')
   BORDER_CHARS = set("()'&%$#\"!")  # one per rank: 8→( 7→' 6→& 5→% 4→$ 3→# 2→" 1→!

   def parse_rank(line):
       """line is like '8-+-+-+-Rt (' — extract 8 squares."""
       line = line.strip()
       rank_label = line[0]
       inner = line[1:].rstrip()
       # drop trailing border char(s)
       while inner and inner[-1] in BORDER_CHARS:
           inner = inner[:-1].rstrip()
       squares = []
       for c in inner:
           if c in EMPTY_CHARS:
               squares.append(None)
           elif c in PIECE_CHARS:
               squares.append(c)
           # else: marker, ignore
       assert len(squares) == 8, f"rank {rank_label}: got {len(squares)} squares from {inner!r}"
       return rank_label, squares

   def squares_to_fen_rank(squares):
       out, empty = '', 0
       for s in squares:
           if s is None:
               empty += 1
           else:
               if empty: out += str(empty); empty = 0
               out += {'L':'B','l':'b'}.get(s, s)
       if empty: out += str(empty)
       return out
   ```

   PDFs often emit all 8 ranks of a diagram on one line. Split with regex `([1-8][^()'&%$#"!]*[()'&%$#"!])` to recover individual rank chunks.

3. **Extract puzzle metadata** — From surrounding text, find:
   - Puzzle number (`Puzzle (\d+)`)
   - Side to move (`(White|Black) to play`)
   - Mate-in-N count (`\((\d+) move`)

4. **Solve each puzzle** — Use MCP tools efficiently:
   a. `chess-solve_mate_in` with the parsed mateIn value, side, and FEN
   b. If no mate found, try `chess-find_best_move` at depth 6+

   `solve_mate_in` returns the human-readable text plus a `---`-delimited JSON tail. Parse it by normalizing line endings and splitting:
   ```python
   import re, json
   m = re.search(r'\n---\n(.+)$', text.replace('\r\n', '\n'), re.DOTALL)
   sequence = json.loads(m.group(1)) if m else []
   ```
   Each entry is `{ply, side, uci, san, status}`. Use `uci` for rendering, `san` for display.

5. **Render and save solution images** — One call per puzzle:

   **Mate-in-1:** use `chess-render_board_png` with `move` for a single arrow. Omit `annotation` to auto-derive SAN.
   ```
   chess-render_board_png(fen=FEN, move=KEY_UCI, savePath="OUT/puzzle-NN.png")
   ```

   **Mate-in-N (N≥2):** use the new `chess-render_sequence` tool — one call writes all N PNGs.
   ```
   chess-render_sequence(
       fen=FEN, startingSide="white", moves="h2h8,f8g7,d8f6",
       outDir="OUT", baseName="puzzle-07",
       annotationPrefix="Puzzle 7")
   ```
   Returns JSON `[{ply, file, san, fenBefore}, ...]`. File naming: `{baseName}-move{round}{w|b}.png` where the `w/b` suffix marks which side made the move in the sequence ordering — for "Black to play" the first ply is `move1b`.

   You can also overlay multiple arrows on a *single* board with `chess-render_board_png(moves="...")` (comma-separated UCI list). Useful for a summary thumbnail of a whole tactical line.

6. **Report solutions** in a formatted table:
   | # | Side | Mate in | Key Move | Sequence |
   |---|------|---------|----------|----------|
   Include UCI and algebraic notation, plus brief explanation.

Present all solutions together at the end in a clear summary.
