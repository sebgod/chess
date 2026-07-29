"""Proof: drive the real Chess.Console TUI over the debug inspector socket, with nobody at the keyboard.

Usage: python proof_inspector.py <port>
Exits non-zero on the first failed assertion.
"""
import json
import socket
import sys
import time


class Inspector:
    def __init__(self, port):
        self.sock = socket.create_connection(("127.0.0.1", port), timeout=15)
        self.f = self.sock.makefile("rw", encoding="utf-8", newline="\n")
        self.id = 0

    def call(self, method, **params):
        self.id += 1
        self.f.write(json.dumps({"id": self.id, "method": method, "params": params}) + "\n")
        self.f.flush()
        line = self.f.readline()
        if not line:
            raise RuntimeError("inspector closed the connection")
        reply = json.loads(line)
        assert reply["id"] == self.id, f"id mismatch: {reply}"
        if "error" in reply:
            raise RuntimeError(f"{method} failed: {reply['error']}")
        return reply["result"]


FAILURES = []


def check(label, ok, detail=""):
    status = "PASS" if ok else "FAIL"
    print(f"  [{status}] {label}{'  -- ' + str(detail) if detail else ''}")
    if not ok:
        FAILURES.append(label)


def find_row(rows, needle):
    for i, r in enumerate(rows):
        if needle in r:
            return i, r
    return -1, ""


def main():
    port = int(sys.argv[1])
    ins = Inspector(port)

    print("\n== transport ==")
    pong = ins.call("ping")
    check("ping answers", pong.get("ok") is True, pong)
    check("app identifies itself", pong.get("app") == "Chess.Console", pong.get("app"))

    print("\n== the screen is readable as text ==")
    size = ins.call("size")
    check("terminal is buffered", size.get("buffered") is True, size)
    print(f"       grid {size['columns']}x{size['rows']}, cell {size['cellWidth']}x{size['cellHeight']}")

    # Let the app settle and paint.
    screen = []
    for _ in range(40):
        screen = ins.call("screen")["rows"]
        if any(s.strip() for s in screen):
            break
        time.sleep(0.25)

    non_blank = [r for r in screen if r.strip()]
    check("something is painted", len(non_blank) > 0, f"{len(non_blank)} non-blank rows")
    for r in non_blank[:6]:
        print(f"       |{r}|")

    print("\n== app state ==")
    state = ins.call("appState")
    if "error" in state:
        # Still on the startup wizard: choose Player vs Computer with the keyboard.
        print(f"       {state['error']} -> driving the startup menu")
        ins.call("key", key="Enter")
        for _ in range(40):
            state = ins.call("appState")
            if "error" not in state:
                break
            time.sleep(0.25)

    if "error" in state:
        check("a game started", False, state)
    else:
        check("a game started", True)
        print(f"       {json.dumps(state)}")
        check("nothing is selected at the start", state.get("selected") is None, state.get("selected"))
        check("White is to move", state.get("sideToMove") == "White", state.get("sideToMove"))
        check("no plies yet", state.get("plies") == 0, state.get("plies"))

        print("\n== inject a move with the keyboard (file, rank, file, rank) ==")
        for k in ("e", "2"):
            ins.call("key", key=k)
        time.sleep(0.5)
        mid = ins.call("appState")
        check("selecting e2 selects it", mid.get("selected") == "e2", mid.get("selected"))

        for k in ("e", "4"):
            ins.call("key", key=k)

        moved = {}
        for _ in range(40):
            moved = ins.call("appState")
            if moved.get("plies", 0) >= 1:
                break
            time.sleep(0.25)

        check("the move was applied", moved.get("plies", 0) >= 1, moved.get("plies"))
        check("the selection cleared after moving", moved.get("selected") is None, moved.get("selected"))
        print(f"       {json.dumps(moved)}")

        print("\n== the move shows up on screen ==")
        rows = []
        for _ in range(40):
            rows = ins.call("screen")["rows"]
            if find_row(rows, "e2e4")[0] >= 0 or find_row(rows, "Move History")[0] >= 0:
                break
            time.sleep(0.25)
        hist_at, hist_row = find_row(rows, "Move History")
        check("the history panel is on screen", hist_at >= 0, hist_row.strip())
        idx, row = find_row(rows, "e2e4")
        check("the move is listed in the history", idx >= 0, row.strip() if idx >= 0 else "not found")

        print("\n== the input log records what the app received ==")
        events = ins.call("inputLog")["events"]
        check("input was logged", len(events) > 0, f"{len(events)} events")
        for e in events[-4:]:
            print(f"       {e}")
        motion_selects = [e for e in events if "MouseMove" in e and "=>" in e
                          and e.split("selected ")[1].split(" ")[0].split("=>")[0]
                          != e.split("selected ")[1].split(" ")[0].split("=>")[1]]
        check("no motion event ever changed the selection", not motion_selects, motion_selects[:2])

    print("\n== a cell reports its pen ==")
    cell = ins.call("cell", column=0, row=0)
    check("cell has a kind", cell.get("kind") in ("Text", "Opaque", "Image"), cell)

    print()
    if FAILURES:
        print(f"FAILED: {len(FAILURES)} -> {FAILURES}")
        return 1
    print("ALL PROOFS PASSED")
    return 0


if __name__ == "__main__":
    sys.exit(main())
