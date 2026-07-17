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

  return { blit, enableMenuKeys };
})();
