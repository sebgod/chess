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

  return { blit };
})();
