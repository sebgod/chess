"""Map a chess square (e2) to the terminal CELL the TUI inspector should click.

Usage:
    square_cell.py <port> --where e2 e4        # print cells, click nothing
    square_cell.py <port> e2 e4                # click each in order (a move is two clicks)
    square_cell.py <port> --verify e2          # click, then ASSERT app_state.selected == e2

--verify is the important one: it is what makes the arithmetic below safe to trust. The mapping
replicates layout code that lives in C#, so it CAN drift; a self-check that names the square it
actually hit turns drift into a loud failure instead of a silent mis-click.
"""
import json
import math
import socket
import sys

HISTORY_COLUMNS = 24        # ConsoleGameDisplayBase.HistoryColumns
MIN_STACKED_HISTORY_ROWS = 5  # ConsoleGameDisplayBase.MinStackedHistoryRows
SQUARES_X = 9.5             # GameUI.SquaresNeededX
SQUARES_Y_STRIPS = 10.5     # GameUI.SquaresNeededY
SQUARES_Y_EXTERNAL = 9.2    # GameUI.SquaresNeededYNoStrips
BOARD_ASPECT = 10.5 / 9.5   # GameFrameLayout.BoardAspect


class Inspector:
    def __init__(self, port):
        self.f = socket.create_connection(("127.0.0.1", port), timeout=15).makefile(
            "rw", encoding="utf-8", newline="\n")
        self.n = 0

    def call(self, method, **params):
        self.n += 1
        self.f.write(json.dumps({"id": self.n, "method": method, "params": params}) + "\n")
        self.f.flush()
        reply = json.loads(self.f.readline())
        if "error" in reply:
            raise RuntimeError(f"{method}: {reply['error']}")
        return reply["result"]


def align_down(value, unit):
    return (value // unit) * unit


def captured_cell_height(square):
    """GameUI.CapturedCellHeight."""
    return int(round(square * 0.4 * 1.4))


def calculate_square_size(ui_x, ui_y, external):
    """GameUI.CalculateSquareSize — the board is whichever of width and height binds first."""
    needed_y = SQUARES_Y_EXTERNAL if external else SQUARES_Y_STRIPS
    return int(min(ui_x / SQUARES_X, ui_y / needed_y))


def choose_shape(cols, rows, cw, ch):
    """GameFrameLayout.ChooseShape, costed in board squares. The terminal allows off-centre."""
    total_w, total_h = cols * cw, rows * ch
    status_h, panel_w, gutter_w = ch, cw * HISTORY_COLUMNS, cw * HISTORY_COLUMNS
    stacked_h = ch * MIN_STACKED_HISTORY_ROWS

    flanked = calculate_square_size(
        max(0, total_w - 2 * gutter_w), max(0, total_h - 2 * status_h), external=True)
    stacked = calculate_square_size(
        total_w, max(0, total_h - status_h - stacked_h), external=False)
    side_by_side = calculate_square_size(
        max(0, total_w - panel_w), max(0, total_h - status_h), external=False)

    # Ties go to the shape that spends least: side-by-side outright, then stacked over flanked.
    if side_by_side >= flanked and side_by_side >= stacked:
        return "SideBySide"
    return "Flanked" if flanked > stacked else "Stacked"


def board_slot(shape, cols, rows, cw, ch):
    """The board slot in CELLS, as ConsoleGameDisplayBase.ArrangeFrame arranges it.

    Returns (x_cells, y_cells, w_cells, h_cells, external). The captured gutter is what puts the
    board right of the canvas origin in the flanked shape — the canvas spans BOTH, which is why
    probing for kind=Image finds the canvas and tells you nothing about the board.
    """
    if shape == "Flanked":
        # [spacer row] / [captured gutter | board | history gutter] / [status row]
        return HISTORY_COLUMNS, 1, cols - 2 * HISTORY_COLUMNS, rows - 2, True
    if shape == "SideBySide":
        # [board | history] / [status row] — no mirror band, board flush to the left.
        return 0, 0, cols - HISTORY_COLUMNS, rows - 1, False
    # Stacked: board on top, history below, status last.
    avail_h = rows * ch - ch
    board_h = min(avail_h, cols * cw * BOARD_ASPECT)
    return 0, 0, cols, int(board_h // ch), False


def geometry(size):
    """Everything needed to place a square, from the inspector's own `size` reply."""
    cols, rows = size["columns"], size["rows"]
    cw, ch = size["cellWidth"], size["cellHeight"]

    shape = choose_shape(cols, rows, cw, ch)
    bx, by, bw, bh, external = board_slot(shape, cols, rows, cw, ch)
    ui_x, ui_y = bw * cw, bh * ch
    left_inset, top_inset = bx * cw, by * ch

    # GameUI's constructor, aligned branch: the console passes alignment=(cellWidth, cellHeight)
    # so square boundaries land on cell boundaries.
    unit = math.lcm(cw, ch)
    square = align_down(calculate_square_size(ui_x, ui_y, external), unit)
    margin = align_down(square // 2, unit)

    strip = captured_cell_height(square) if not external else 0
    min_top = 0
    if strip > 0:
        min_top = -(-max(square // 2, strip) // unit) * unit  # AlignUp
    content_h = 8 * square + 2 * margin + 2 * strip
    top_margin = max(min_top, align_down((ui_y - content_h) // 2 + strip, unit)) + top_inset

    content_w = square * 8 + 2 * margin
    left_centering = (ui_x - content_w) // 2
    left_offset = left_inset + (align_down(left_centering, unit) if left_centering > 0 else 0)

    return {"shape": shape, "square": square, "margin": margin,
            "left_offset": left_offset, "top_margin": top_margin,
            "cell_w": cw, "cell_h": ch}


def square_to_cell(name, geo, flip):
    """A square name to the terminal cell at its CENTRE. Inverse of GameUI.SquareRect."""
    file_i = "abcdefgh".index(name[0].lower())
    rank_i = int(name[1]) - 1
    col, row_from_top = (7 - file_i, rank_i) if flip else (file_i, 7 - rank_i)

    sq, m = geo["square"], geo["margin"]
    x = col * sq + m + geo["left_offset"] + sq // 2
    y = row_from_top * sq + m + geo["top_margin"] + sq // 2
    return x // geo["cell_w"], y // geo["cell_h"]


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2

    port = int(sys.argv[1])
    args = sys.argv[2:]
    mode = "click"
    if args[0] in ("--where", "--verify"):
        mode, args = args[0][2:], args[1:]

    ins = Inspector(port)
    geo = geometry(ins.call("size"))
    flip = ins.call("appState").get("flipBoard", False)
    print(f"shape={geo['shape']} square={geo['square']}px margin={geo['margin']} "
          f"leftOffset={geo['left_offset']} topMargin={geo['top_margin']} flip={flip}")

    failures = []
    for name in args:
        col, row = square_to_cell(name, geo, flip)
        if mode == "where":
            print(f"  {name} -> cell ({col},{row})")
            continue

        ins.call("click", column=col, row=row)
        state = ins.call("appState")
        print(f"  {name} -> cell ({col},{row})  selected={state.get('selected')} "
              f"plies={state.get('plies')}")

        if mode == "verify":
            got = state.get("selected")
            if got != name:
                # Either the layout chain drifted, or the piece has no legal move (a start-position
                # king reports None for a perfectly correct click). Both are worth saying out loud.
                failures.append(f"{name}: clicked ({col},{row}) but selected={got}")

    if failures:
        print("\nMISMATCH — the mapping did not land where intended:")
        for f in failures:
            print("  " + f)
        print("\nEither the piece has no legal move (selection is refused, and that is correct),\n"
              "or the layout changed: re-derive against GameFrameLayout.ChooseShape,\n"
              "ConsoleGameDisplayBase.ArrangeFrame and the GameUI constructor.")
        return 1

    print("\nOK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
