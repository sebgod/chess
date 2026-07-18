// Chess web — the JS half of rendering. Blazor renders the board into an RgbaImage
// (tightly-packed RGBA byte[]) on the .NET side and hands it here; we wrap it in an
// ImageData and blit it to the <canvas> in one putImageData. No per-pixel JS work.
window.chessCanvas = (function () {
  function blit(canvasId, width, height, bytes) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    // `bytes` arrives as a Uint8Array (Blazor's byte[] marshalling). ImageData needs a
    // Uint8ClampedArray of exactly width*height*4; copy into one sized to the canvas.
    const rgba = new Uint8ClampedArray(width * height * 4);
    rgba.set(bytes.subarray(0, rgba.length));
    ctx.putImageData(new ImageData(rgba, width, height), 0, 0);
  }

  // Canvas keyboard capture. The startup wizard AND gameplay (playback nav, square entry) take
  // keyboard input on the canvas, so arrow/Enter/Space must not also scroll the page while it is
  // focused. A single keydown listener (idempotent) calls preventDefault for nav keys while
  // active; the app turns capture on at the menu and leaves it on for the whole session — only
  // the focused canvas is affected, so browser shortcuts elsewhere on the page are untouched.
  const NAV_KEYS = new Set(["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", " ", "Enter"]);
  function enableMenuKeys(canvasId, active) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    canvas._menuActive = active;
    if (!canvas._navKeysHooked) {
      canvas._navKeysHooked = true;
      canvas.addEventListener("keydown", (e) => {
        if (canvas._menuActive && NAV_KEYS.has(e.key)) e.preventDefault();
      });
    }
    if (active) canvas.focus({ preventScroll: true });
  }

  // ---- Play by Link helpers. The game travels in the URL fragment; these keep the address bar
  // canonical and hand the link to whatever share channel the platform offers.

  // history.replaceState with just the hash swapped — bookmarking (or copying) the URL then
  // always captures the live game. Blazor may surface this as a LocationChanged; the .NET side
  // dedupes against the fragment it just wrote.
  function replaceStateFragment(fragment) {
    const url = new URL(window.location.href);
    url.hash = fragment;
    window.history.replaceState(null, "", url.toString());
  }

  // Clipboard write, reported as success/failure so the button label can react. Requires a
  // secure context (HTTPS/localhost) — the guard turns an insecure-context miss into a clean
  // `false` instead of a TypeError.
  async function copyText(text) {
    if (!navigator.clipboard || !navigator.clipboard.writeText) return false;
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch (err) {
      console.error("[chess-web] clipboard write failed", err);
      return false;
    }
  }

  function isShareSupported() {
    return typeof navigator.share === "function";
  }

  // Native share sheet (mostly mobile). AbortError = the user dismissed the sheet — a normal
  // outcome, not a failure worth logging.
  async function shareLink(url, title, text) {
    if (typeof navigator.share !== "function") return false;
    try {
      await navigator.share({ title, text, url });
      return true;
    } catch (err) {
      if (err && err.name !== "AbortError") console.error("[chess-web] share failed", err);
      return false;
    }
  }

  return { blit, enableMenuKeys, replaceStateFragment, copyText, isShareSupported, shareLink };
})();
