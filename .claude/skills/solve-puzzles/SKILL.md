---
name: solve-puzzles
description: Solve chess puzzles from a PDF file attached to the conversation. Parses XABCDEFGHY diagram notation to FEN, solves with the chess MCP tools, and renders solution PNGs/GIFs. Use when the user attaches a puzzle worksheet PDF or asks to solve puzzles.
argument-hint: [--output DIR]
---

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

5. **Order multi-move frames by PLY, never by filename.** `chess-render_sequence` names files `{baseName}-move{round}{w|b}.png` where `{round}` is the full-move number. A plain alphabetical sort is **wrong**: within a round `b` sorts before `w`, but **White plays first**. So a White-to-move line `move1w, move1b, move2w` mis-sorts to `move1b, move1w, move2w` (black's reply jumps ahead of white's first move); a Black-to-move line `move1b, move2w, move2b` mis-sorts to `move1b, move2b, move2w`.

   The returned JSON array is already in play order — **drive ordering off its `file`/`ply` fields, never `sorted(os.listdir())`.** If you keep the frames on disk for browsing, rename with a zero-padded ply index so name-sort == play order: `{baseName}-ply{ply:02d}{w|b}.png`.

6. **Render solution images — output format depends on mate length:**

   **Mate-in-1 → ONE static PNG** (board + answer arrow). Do NOT animate single-move puzzles.
   ```
   chess-render_board_png(fen=FEN, move=KEY_UCI, annotation="Puzzle N: <SAN>", savePath="OUT/puzzle-NN.png")
   ```

   **Mate-in-N (N≥2) → frames for an animated GIF.** `chess-render_sequence` writes one PNG per ply (position *before* each move, with that move's arrow):
   ```
   chess-render_sequence(fen=FEN, startingSide="white", moves="h2h8,f8g7,d8f6",
                         outDir="OUT", baseName="puzzle-07", annotationPrefix="Puzzle 7")
   ```
   Those frames END on "arrow not yet played", so add a closing frame that shows the executed mate: `chess-play_moves(fen, startingSide, moves)` → take its final FEN → `chess-render_board_png(finalFEN, move=LAST_UCI, annotation="Puzzle N: <SAN> — mate", savePath="OUT/pNN-final.png")`.

   Optional: a single-board summary with every arrow overlaid — `chess-render_board_png(fen, moves="u1,u2,...")`.

7. **Assemble the GIF (multi-move puzzles only).** Windows Explorer/Photos does NOT animate APNG — use **GIF**. With Pillow (installed; `pdftoppm`/`pymupdf` are NOT):
   ```python
   from PIL import Image
   frames = [Image.open(p).convert("RGB") for p in ordered_paths]   # ply order + final mate frame
   w, h = frames[0].size                                            # ONE shared palette across all
   montage = Image.new("RGB", (w, h*len(frames)))                   # frames (same colour set, only
   for i, f in enumerate(frames): montage.paste(f, (0, i*h))        # positions move) => no flicker
   pal = montage.quantize(colors=256, method=Image.MEDIANCUT, dither=Image.NONE)
   pf = [f.quantize(palette=pal, dither=Image.NONE) for f in frames]
   durations = [1300]*(len(pf)-1) + [2600]                          # ms; hold longer on the mate
   pf[0].save(out_gif, save_all=True, append_images=pf[1:], duration=durations, loop=0, disposal=1)
   ```

8. **Output layout.** Default to `C:/temp/chess/<pdf-stem>/` (or `--output DIR`); confirm the folder name with the user — they tweak it. Deliverables at top level: `puzzle-01.png … ` (mate-in-1 stills) and `puzzle-NN.gif` (mate-in-N animations). Tuck the per-ply stills + overlay summaries into a `frames/` subfolder.

9. **Validate every parse** with `chess-solve_mate_in`: a *forced* mate of exactly the stated length confirms the diagram parsed correctly. If no mate is found, re-check the parse (or fall back to `chess-find_best_move` at depth 6+). Then report all solutions in a table:
   | # | Side | Mate in | Key Move | Sequence |
   |---|------|---------|----------|----------|
   Include UCI and algebraic notation, plus a brief explanation. Present all solutions together at the end.
