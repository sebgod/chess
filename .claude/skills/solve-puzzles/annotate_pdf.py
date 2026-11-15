"""Parse a chess-diagram worksheet PDF and write the solutions back onto it as PDF annotations.

    python -I annotate_pdf.py parse    WORKSHEET.pdf
    python -I annotate_pdf.py annotate WORKSHEET.pdf SOLUTIONS.json -o OUT.pdf

`parse` prints one JSON object per diagram: number, side, FEN, and where the board sits on the page.
`annotate` copies the PDF (the original is never written to) and adds, per puzzle:
  * one /Line annotation per ply, an arrow from the piece's square to its target, numbered when the
    line is longer than one move; the defender's replies are dashed;
  * one /FreeText annotation left of the puzzle's title with the key move in SAN.
Both carry the whole line in /Contents (the hover/comment text), and both have a real appearance
stream, so they show in every viewer and stay editable/deletable as ordinary annotations.

SOLUTIONS.json maps the puzzle number to its line, UCI for geometry and SAN for the words:
    {"1": {"uci": ["f7a7"], "san": ["Qxa7#"]},
     "7": {"uci": ["h2h8", "f8g7", "d8f6"], "san": ["Rh8+", "Kg7", "Qf6#"]}}

Needs pypdf only.
"""
import argparse
import json
import re
import sys
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (ArrayObject, ContentStream, DecodedStreamObject, DictionaryObject, FloatObject,
                           NameObject, NumberObject, TextStringObject)

PIECE_CHARS = set('KQRBNPLkqrbnpl')
EMPTY_CHARS = set('+-')
BORDER_CHARS = set("()'&%$#\"!")

# Diagram-font metrics in em, used when the PDF's own font dictionary cannot be read. A square is
# 1 em wide; the frame glyph left of it is narrower; the square's ink spans Descent..Ascent.
DEFAULT_BORDER_EM, DEFAULT_ASCENT_EM, DEFAULT_DESCENT_EM = 0.65, 0.725, -0.323

INK = (0.0, 0.45, 0.15)      # #007326: 5.9:1 on white, so the 11pt label passes AA as body text
AUTHOR = 'chess-mcp'


# ---------------------------------------------------------------- parsing

def parse_rank(line):
    """'8-+-+-+-Rt (' -> ('8', [None, ..., 'R']). Anything that is not a square is a marker."""
    line = line.strip()
    inner = line[1:].rstrip()
    while inner and inner[-1] in BORDER_CHARS:
        inner = inner[:-1].rstrip()
    squares = [None if c in EMPTY_CHARS else c for c in inner if c in EMPTY_CHARS or c in PIECE_CHARS]
    if len(squares) != 8:
        raise ValueError(f'rank {line[0]}: got {len(squares)} squares from {inner!r}')
    return line[0], squares


def fen_rank(squares):
    out, empty = '', 0
    for s in squares:
        if s is None:
            empty += 1
            continue
        if empty:
            out += str(empty)
            empty = 0
        out += {'L': 'B', 'l': 'b'}.get(s, s)
    return out + (str(empty) if empty else '')


def font_metrics(font):
    """(border, ascent, descent) in em from the diagram font's own dictionary, else the defaults."""
    try:
        cid = font['/DescendantFonts'][0].get_object()
        desc = cid['/FontDescriptor'].get_object()
        widths = []
        w = cid['/W']
        i = 0
        while i < len(w):                       # /W is  c [w1 w2 ...]  or  c_first c_last w
            if isinstance(w[i + 1], list):
                widths += [float(x) for x in w[i + 1]]
                i += 2
            else:
                widths.append(float(w[i + 2]))
                i += 3
        # The frame glyphs are the only ones narrower than a square and wider than a marker (0).
        border = min(x for x in widths if 0 < x < 990) / 1000
        return border, float(desc['/Ascent']) / 1000, float(desc['/Descent']) / 1000
    except Exception:
        return DEFAULT_BORDER_EM, DEFAULT_ASCENT_EM, DEFAULT_DESCENT_EM


def _mul(m, n):
    """PDF matrix product m x n, both as (a, b, c, d, e, f)."""
    return (m[0] * n[0] + m[1] * n[2], m[0] * n[1] + m[1] * n[3],
            m[2] * n[0] + m[3] * n[2], m[2] * n[1] + m[3] * n[3],
            m[4] * n[0] + m[5] * n[2] + n[4], m[4] * n[1] + m[5] * n[3] + n[5])


def text_lines(page, reader):
    """[(font resource name, x, y, size)] for every text line the page shows, in stream order, in
    page points. Read from the operators themselves: pypdf's visitor cannot be used for this, its
    `tm` multiplies the leading by the font size on every T* (6.9) or lags a line behind
    (PyPDF2 3.0), so the y it reports is wrong either way."""
    ident = (1.0, 0.0, 0.0, 1.0, 0.0, 0.0)
    ctm, stack, tlm, leading, font, size = ident, [], ident, 0.0, None, 0.0
    lines, fresh = [], False                    # fresh: the line origin moved since the last show

    def move(tx, ty):
        nonlocal tlm, fresh
        tlm, fresh = _mul((1.0, 0.0, 0.0, 1.0, tx, ty), tlm), True

    for operands, op in ContentStream(page.get_contents(), reader).operations:
        if op == b'q':
            stack.append(ctm)
        elif op == b'Q' and stack:
            ctm = stack.pop()
        elif op == b'cm':
            ctm = _mul(tuple(float(v) for v in operands), ctm)
        elif op == b'BT':
            tlm, fresh = ident, True
        elif op == b'Tf':
            font, size = str(operands[0]), float(operands[1])
        elif op == b'TL':
            leading = float(operands[0])
        elif op == b'Tm':
            tlm, fresh = tuple(float(v) for v in operands), True
        elif op == b'Td':
            move(float(operands[0]), float(operands[1]))
        elif op == b'TD':
            leading = -float(operands[1])
            move(float(operands[0]), float(operands[1]))
        elif op == b'T*':
            move(0.0, -leading)
        if op in (b"'", b'"'):
            move(0.0, -leading)
        if op in (b'Tj', b'TJ', b"'", b'"') and fresh:
            m = _mul(tlm, ctm)
            lines.append((font, m[4], m[5], size * (m[0] ** 2 + m[1] ** 2) ** 0.5))
            fresh = False
    return lines


def find_diagrams(reader):
    """Every diagram in the PDF, in text order, with its FEN and its board geometry in page points."""
    diagrams = []
    for page_index, page in enumerate(reader.pages):
        chunks = []                             # (text, font dict): WHAT is shown, never where

        def visit(text, cm, tm, font, size, chunks=chunks):
            if text.strip():
                chunks.append((text.strip(), font))

        page.extract_text(visitor_text=visit)

        number, current = None, None
        page_diagrams = []
        for text, font in chunks:
            m = re.match(r'Puzzle\s+(\d+)', text)
            if m:
                number = int(m.group(1))
            if re.fullmatch(r'X[A-H]{8}Y', text):
                current = {'number': number, 'page': page_index, 'lines': []}
            if current is not None and len(current['lines']) < 10:
                current['lines'].append(text)
                current['font'] = font
                if len(current['lines']) == 10:
                    diagrams.append(current)
                    page_diagrams.append(current)
                continue
            m = re.search(r'(White|Black) to play', text)
            if m and diagrams and 'side' not in diagrams[-1]:
                diagrams[-1]['side'] = m.group(1).lower()
            m = re.search(r'\((\d+) move', text)
            if m and diagrams:
                diagrams[-1]['mateIn'] = int(m.group(1))
        if current is not None and len(current['lines']) != 10:
            raise ValueError(f'page {page_index + 1}: diagram for puzzle {current["number"]} is not '
                             '10 separate text lines; this layout is not supported for annotation')

        # WHERE: the diagram font's lines, ten per diagram, in the same order as the text.
        fonts = page['/Resources']['/Font']
        names = {k for k in fonts for d in page_diagrams
                 if fonts[k].get_object().get('/BaseFont') == d['font'].get('/BaseFont')}
        where = [l for l in text_lines(page, reader) if l[0] in names]
        # A diagram is ten lines in one column, top to bottom. Anything else in that font (Word
        # leaves a stray blank in it between diagrams) is not part of a board and is skipped.
        boards, i = [], 0
        while i + 10 <= len(where):
            run = where[i:i + 10]
            if all(abs(l[1] - run[0][1]) < 0.01 for l in run) and \
                    all(a[2] > b[2] for a, b in zip(run, run[1:])):
                boards.append(run)
                i += 10
            else:
                i += 1
        if len(boards) != len(page_diagrams):
            raise ValueError(f'page {page_index + 1}: placed {len(boards)} boards for '
                             f'{len(page_diagrams)} diagrams; cannot annotate')
        for d, run in zip(page_diagrams, boards):
            d['where'] = run

    for d in diagrams:
        lines, where, font = d.pop('lines'), d.pop('where'), d.pop('font')
        header, (_, x, _, size) = lines[0], where[0]
        border, ascent, descent = font_metrics(font)
        ys = [w[2] for w in where]              # baselines, header first, footer last
        ranks = {}
        for text, y in zip(lines[1:9], ys[1:9]):
            label, squares = parse_rank(text)
            ranks[label] = (squares, y)
        files = header[1:9].lower()             # 'abcdefgh', or 'hgfedcba' on a flipped diagram
        d['fen'] = '/'.join(fen_rank(ranks[r][0][::1 if files[0] == 'a' else -1]) for r in '87654321')
        d['geometry'] = {
            'left': x + border * size,          # left edge of the first square
            'cell': size,                       # a square is 1 em wide
            'files': files,
            'rankCentreY': {r: by + (ascent + descent) / 2 * size for r, (_, by) in ranks.items()},
            'titleY': ys[0] + 0.89 * size,      # baseline of the "Puzzle N" line above the header
        }
    return diagrams


# ---------------------------------------------------------------- annotating

def square_centre(geometry, square):
    g = geometry
    return (g['left'] + (g['files'].index(square[0]) + 0.5) * g['cell'], g['rankCentreY'][square[1]])


def _floats(values):
    return ArrayObject(FloatObject(round(v, 2)) for v in values)


def _appearance(rect, ops, resources=None):
    stream = DecodedStreamObject()
    stream.set_data(ops.encode('latin-1'))
    stream.update({
        NameObject('/Type'): NameObject('/XObject'),
        NameObject('/Subtype'): NameObject('/Form'),
        NameObject('/BBox'): _floats(rect),     # page coordinates: the stream draws where it is
        NameObject('/Resources'): resources or DictionaryObject(),
    })
    return stream


def _annotation(writer, subtype, rect, contents, ops, extra, resources=None):
    annot = DictionaryObject({
        NameObject('/Type'): NameObject('/Annot'),
        NameObject('/Subtype'): NameObject(subtype),
        NameObject('/Rect'): _floats(rect),
        NameObject('/Contents'): TextStringObject(contents),
        NameObject('/T'): TextStringObject(AUTHOR),
        NameObject('/F'): NumberObject(4),      # print
        NameObject('/C'): _floats(INK),
        NameObject('/AP'): DictionaryObject(
            {NameObject('/N'): writer._add_object(_appearance(rect, ops, resources))}),
    })
    annot.update(extra)
    return annot


def arrow_annotation(writer, geometry, uci, contents, number=None, dashed=False):
    cell = geometry['cell']
    (x1, y1), (x2, y2) = square_centre(geometry, uci[0:2]), square_centre(geometry, uci[2:4])
    length = ((x2 - x1) ** 2 + (y2 - y1) ** 2) ** 0.5
    ux, uy = (x2 - x1) / length, (y2 - y1) / length
    # Leave the moving piece and the target's centre readable: start off-centre, stop just short.
    sx, sy = x1 + ux * cell * 0.30, y1 + uy * cell * 0.30
    tx, ty = x2 - ux * cell * 0.10, y2 - uy * cell * 0.10
    width, head, half = cell * 0.11, cell * 0.42, cell * 0.20
    bx, by = tx - ux * head, ty - uy * head     # base of the arrow head
    r, g, b = INK
    ops = (f'q {r} {g} {b} RG {r} {g} {b} rg {width:.2f} w 1 J '
           + (f'[{cell * 0.2:.2f} {cell * 0.18:.2f}] 0 d ' if dashed else '')
           + f'{sx:.2f} {sy:.2f} m {bx:.2f} {by:.2f} l S '
           f'{tx:.2f} {ty:.2f} m {bx - uy * half:.2f} {by + ux * half:.2f} l '
           f'{bx + uy * half:.2f} {by - ux * half:.2f} l f Q ')
    resources = None
    if number is not None:                      # a filled badge on the shaft, digit knocked out white
        mx, my, rad = (sx + bx) / 2, (sy + by) / 2, cell * 0.27
        if length < cell * 1.6:                 # a one-square move: the badge would hide the arrow
            mx, my = mx - uy * rad * 1.5, my + ux * rad * 1.5
        k = rad * 0.5523
        ops += (f'q {r} {g} {b} rg {mx + rad:.2f} {my:.2f} m '
                f'{mx + rad:.2f} {my + k:.2f} {mx + k:.2f} {my + rad:.2f} {mx:.2f} {my + rad:.2f} c '
                f'{mx - k:.2f} {my + rad:.2f} {mx - rad:.2f} {my + k:.2f} {mx - rad:.2f} {my:.2f} c '
                f'{mx - rad:.2f} {my - k:.2f} {mx - k:.2f} {my - rad:.2f} {mx:.2f} {my - rad:.2f} c '
                f'{mx + k:.2f} {my - rad:.2f} {mx + rad:.2f} {my - k:.2f} {mx + rad:.2f} {my:.2f} c f '
                f'1 1 1 rg BT /Helv {rad * 1.5:.2f} Tf {mx - rad * 0.42 * len(str(number)):.2f} '
                f'{my - rad * 0.53:.2f} Td ({number}) Tj ET Q ')
        resources = _helvetica_bold()
    pad = cell * 0.8                            # room for the head and a badge set beside the shaft
    rect = (min(x1, x2) - pad, min(y1, y2) - pad, max(x1, x2) + pad, max(y1, y2) + pad)
    return _annotation(writer, '/Line', rect, contents, ops, {
        NameObject('/L'): _floats((sx, sy, tx, ty)),
        NameObject('/LE'): ArrayObject([NameObject('/None'), NameObject('/ClosedArrow')]),
        NameObject('/IC'): _floats(INK),
        NameObject('/BS'): DictionaryObject({NameObject('/W'): FloatObject(round(width, 2))}),
    }, resources)


def _helvetica_bold():
    return DictionaryObject({NameObject('/Font'): DictionaryObject({NameObject('/Helv'): DictionaryObject({
        NameObject('/Type'): NameObject('/Font'), NameObject('/Subtype'): NameObject('/Type1'),
        NameObject('/BaseFont'): NameObject('/Helvetica-Bold'),
        NameObject('/Encoding'): NameObject('/WinAnsiEncoding')})})})


def label_annotation(writer, geometry, text, contents):
    """The key move, on the puzzle's title line, starting at the board's left edge (the title is
    centred and short, so that stretch is free; under the board the caption can run long)."""
    size = 11.0
    x, y = geometry['left'], geometry['titleY']
    rect = (x - 1, y - size * 0.3, x + size * 0.62 * len(text) + 2, y + size)
    r, g, b = INK
    safe = text.replace('\\', r'\\').replace('(', r'\(').replace(')', r'\)')
    ops = f'q BT {r} {g} {b} rg /Helv {size} Tf {x:.2f} {y:.2f} Td ({safe}) Tj ET Q '
    return _annotation(writer, '/FreeText', rect, contents, ops, {
        NameObject('/DA'): TextStringObject(f'{r} {g} {b} rg /Helv {size} Tf'),
        NameObject('/BS'): DictionaryObject({NameObject('/W'): NumberObject(0)}),
    }, _helvetica_bold())


def san_line(sans, black_starts):
    """['Rh8+', 'Kg7', 'Qf6#'] -> '1.Rh8+ Kg7 2.Qf6#' (or '1...Qxe4#' when Black starts)."""
    out = []
    for i, san in enumerate(sans):
        ply = i + (1 if black_starts else 0)
        if ply % 2 == 0:
            out.append(f'{ply // 2 + 1}.{san}')
        else:
            out.append(f'{ply // 2 + 1}...{san}' if i == 0 else san)
    return ' '.join(out)


def annotate(pdf, solutions, out):
    reader = PdfReader(pdf)
    writer = PdfWriter(clone_from=reader)
    done = []
    for d in find_diagrams(reader):
        sol = solutions.get(str(d['number']))
        if not sol:
            continue
        g, ucis, sans = d['geometry'], sol['uci'], sol['san']
        line = san_line(sans, d.get('side') == 'black')
        contents = f'Puzzle {d["number"]}: {line}'
        page = writer.pages[d['page']]
        many = len(ucis) > 1
        for i, uci in enumerate(ucis):
            writer.add_annotation(page, arrow_annotation(
                writer, g, uci, contents, number=i // 2 + 1 if many else None, dashed=i % 2 == 1))
        # Only the key move fits left of the centred title; the whole line is in /Contents.
        writer.add_annotation(page, label_annotation(writer, g, sans[0], contents))
        done.append(d['number'])
    missing = sorted(set(map(int, solutions)) - set(done))
    if missing:
        raise SystemExit(f'no diagram found for puzzle(s) {missing}; nothing written')
    with open(out, 'wb') as f:
        writer.write(f)
    return done


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest='cmd', required=True)
    p = sub.add_parser('parse')
    p.add_argument('pdf')
    a = sub.add_parser('annotate')
    a.add_argument('pdf')
    a.add_argument('solutions')
    a.add_argument('-o', '--out', required=True)
    args = ap.parse_args()
    if args.cmd == 'parse':
        for d in find_diagrams(PdfReader(args.pdf)):
            print(json.dumps(d))
    else:
        with open(args.solutions, encoding='utf-8') as f:
            done = annotate(args.pdf, json.load(f), args.out)
        print(f'{args.out}: annotated puzzles {done}')


if __name__ == '__main__':
    main()
