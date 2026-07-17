using Chess.Lib.UI;
using SdlVulkan.Renderer;

namespace Chess.GUI;

/// <summary>
/// The desktop Vulkan game display. All layout/rendering lives in the renderer-agnostic
/// <see cref="PixelGameDisplay{TSurface}"/> (Chess.Lib.UI) — hoisted so Chess.Web can drive the
/// same board + history panel + status bar over WebGlContext/RgbaImage; this subclass only binds
/// the Vulkan surface type.
/// </summary>
public sealed class VkGameDisplay(VkRenderer renderer) : PixelGameDisplay<VulkanContext>(renderer);
