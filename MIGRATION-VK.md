# Vulkan Migration Guide: Silk.NET OpenGL → Vortice.Vulkan + SDL3-CS

This document provides a detailed function-by-function migration reference for the Chess.OpenGL project, migrated from Silk.NET (OpenGL 3.3 + GLFW) to Vortice.Vulkan + SDL3-CS.

---

## Table of Contents

1. [Package Changes](#1-package-changes)
2. [File Mapping](#2-file-mapping)
3. [Windowing & Input: Silk.NET → SDL3-CS](#3-windowing--input-silknet--sdl3-cs)
4. [Shader Programs: GlShaderProgram → VkPipelineSet](#4-shader-programs-glshaderprogram--vkpipelineset)
5. [Renderer: GlRenderer → VkRenderer](#5-renderer-glrenderer--vkrenderer)
6. [Font Atlas: GlFontAtlas → VkFontAtlas](#6-font-atlas-glfontatlas--vkfontatlas)
7. [Game Display: OpenGLGameDisplay → VkGameDisplay](#7-game-display-openglgamedisplay--vkgamedisplay)
8. [Startup Menu: OpenGLStartupMenu → VkStartupMenu](#8-startup-menu-openglstartupmenu--vkstartupmenu)
9. [Human Player: Silk.NET Input → SDL3 Events](#9-human-player-silknet-input--sdl3-events)
10. [Program.cs: Callback-Driven → Explicit Poll Loop](#10-programcs-callback-driven--explicit-poll-loop)
11. [Vulkan Infrastructure (New)](#11-vulkan-infrastructure-new)
12. [Coordinate System Differences](#12-coordinate-system-differences)
13. [Gotchas & Lessons Learned](#13-gotchas--lessons-learned)
14. [AOT Publishing](#14-aot-publishing)

---

## 1. Package Changes

### Directory.Packages.props

| Removed (Silk.NET)               | Added (Vulkan + SDL3)                              |
|----------------------------------|----------------------------------------------------|
| `Silk.NET.Input` 2.22.0         | `SDL3-CS` 3.5.0-preview.20260213-150035            |
| `Silk.NET.OpenGL` 2.22.0        | `SDL3-CS.Native` 3.5.0-preview.20260205-174353     |
| `Silk.NET.Windowing` 2.22.0     | `Vortice.Vulkan` 3.2.1                             |
|                                  | `Vortice.VulkanMemoryAllocator` 1.7.0              |
|                                  | `Vortice.ShaderCompiler` 1.9.0                     |

### Chess.OpenGL.csproj

| Removed                          | Added                              |
|----------------------------------|------------------------------------|
| `Silk.NET.Input`                 | `SDL3-CS`                          |
| `Silk.NET.OpenGL`                | `SDL3-CS.Native`                   |
| `Silk.NET.Windowing`             | `Vortice.Vulkan`                   |
|                                  | `Vortice.VulkanMemoryAllocator`    |
|                                  | `Vortice.ShaderCompiler`           |

The `TrimmerSingleWarn` property was removed (no longer needed — Silk.NET's reflection-based platform discovery was the reason for it).

---

## 2. File Mapping

| Old File (Silk.NET)        | New File (Vulkan)        | Role                         |
|---------------------------|--------------------------|------------------------------|
| `GlShaderProgram.cs`      | `VkPipelineSet.cs`       | Shader compilation & pipelines |
| `GlRenderer.cs`           | `VkRenderer.cs`          | Draw calls (Renderer<T>)    |
| `GlFontAtlas.cs`          | `VkFontAtlas.cs`         | Glyph rasterization & texture |
| `OpenGLGameDisplay.cs`    | `VkGameDisplay.cs`       | IGameDisplay implementation  |
| `OpenGLStartupMenu.cs`    | `VkStartupMenu.cs`       | Startup menu UI              |
| `HumanPlayer.cs`          | `HumanPlayer.cs`         | Input handling (rewritten)   |
| `Program.cs`              | `Program.cs`             | Main loop (rewritten)        |
| *(none)*                  | `VulkanContext.cs`        | **NEW** — Vulkan device lifecycle |
| *(none)*                  | `SdlVulkanWindow.cs`     | **NEW** — SDL window + VK instance |

---

## 3. Windowing & Input: Silk.NET → SDL3-CS

### Namespace & Imports

```csharp
// OLD
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using Silk.NET.Input;
using Silk.NET.Input.Glfw;
using Silk.NET.OpenGL;
using Silk.NET.Maths;

// NEW
using static SDL3.SDL;           // All SDL functions as static imports
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
```

### Window Creation

```csharp
// OLD — Silk.NET
GlfwWindowing.RegisterPlatform();    // Required for AOT
GlfwInput.RegisterPlatform();
var options = WindowOptions.Default with {
    Title = "Chess",
    Size = new Vector2D<int>(1050, 830),
    API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core,
        ContextFlags.ForwardCompatible, new APIVersion(3, 3))
};
var window = Window.Create(options);

// NEW — SDL3
Init(InitFlags.Video | InitFlags.Events);
VulkanLoadLibrary(null);
vkInitialize().CheckResult();    // MUST call before any Vulkan functions
var window = CreateWindow("Chess", 1050, 830,
    WindowFlags.Vulkan | WindowFlags.Resizable);
```

### Window Size

```csharp
// OLD
window.Size.X, window.Size.Y            // logical size
// NEW
GetWindowSizeInPixels(handle, out w, out h);  // pixel size (HiDPI-aware)
```

### Fullscreen Toggle

```csharp
// OLD
window.WindowState = window.WindowState == WindowState.Fullscreen
    ? WindowState.Normal : WindowState.Fullscreen;

// NEW
var flags = GetWindowFlags(handle);
SetWindowFullscreen(handle, (flags & WindowFlags.Fullscreen) == 0);
```

### Input: Key Enum Mapping

| Silk.NET `Key`     | SDL3 `Scancode`        |
|--------------------|-----------------------|
| `Key.Number1`–`Key.Number8` | `Scancode.Alpha1`–`Scancode.Alpha8` |
| `Key.A`–`Key.H`   | `Scancode.A`–`Scancode.H`   |
| `Key.Enter`        | `Scancode.Return`            |
| `Key.Up/Down/Left/Right` | `Scancode.Up/Down/Left/Right` |
| `Key.PageUp`       | `Scancode.Pageup`            |
| `Key.PageDown`     | `Scancode.Pagedown`          |
| `Key.Escape`       | `Scancode.Escape`            |
| `Key.F1`           | `Scancode.F1`                |
| `Key.F9`           | `Scancode.F9`                |
| `Key.F11`          | `Scancode.F11`               |
| `Key.Tab`          | `Scancode.Tab`               |
| `Key.Delete`       | `Scancode.Delete`            |
| `Key.Backspace`    | `Scancode.Backspace`         |
| `Key.ControlLeft/Right` check | `(keymod & Keymod.Ctrl) != 0` |

### Input: Event Model

```csharp
// OLD — Silk.NET callback-based
keyboard.KeyDown += (_, key, _) => { ... };
mouse.MouseDown += (m, button) => { ... };
mouse.Scroll += (m, scroll) => { ... };

// NEW — SDL3 poll-based (in main loop)
case EventType.KeyDown:
    var scancode = evt.Key.Scancode;
    var keymod = evt.Key.Mod;
    break;
case EventType.MouseButtonDown:
    var mx = (int)evt.Button.X;
    var my = (int)evt.Button.Y;
    break;
case EventType.MouseWheel:
    var delta = (int)evt.Wheel.Y;
    break;
```

### HumanPlayer: Callback → Enqueue Pattern

```csharp
// OLD — Silk.NET callbacks via Attach()
public void Attach(IInputContext input) {
    foreach (var kb in input.Keyboards) kb.KeyDown += OnKeyDown;
    foreach (var m in input.Mice) { m.MouseDown += OnMouseDown; m.Scroll += OnMouseScroll; }
}
private void OnKeyDown(IKeyboard kb, Key key, int scancode) { ... }
private void OnMouseDown(IMouse mouse, MouseButton button) { ... }
private void OnMouseScroll(IMouse mouse, ScrollWheel scroll) { ... }

// NEW — Public enqueue methods called from SDL event loop in Program.cs
public void EnqueueKeyDown(Scancode scancode, Keymod keymod) { ... }
public void EnqueueMouseDown(int x, int y) { ... }
public void EnqueueScroll(int delta) { ... }
```

The `InputEvent` record changed from `Key` to `Scancode`:
```csharp
// OLD
record struct InputEvent(Key Key, bool IsCtrl, int MouseX, int MouseY, bool IsClick, bool IsScroll, int ScrollDelta);
// NEW
record struct InputEvent(Scancode Scancode, bool IsCtrl, int MouseX, int MouseY, bool IsClick, bool IsScroll, int ScrollDelta);
```

---

## 4. Shader Programs: GlShaderProgram → VkPipelineSet

### Architecture Change

| Aspect          | OpenGL (GlShaderProgram)               | Vulkan (VkPipelineSet)                        |
|-----------------|----------------------------------------|-----------------------------------------------|
| Shader language | GLSL 330 core                          | GLSL 450 (compiled to SPIR-V at runtime)      |
| Compilation     | `gl.CreateShader` + `gl.CompileShader` | `Vortice.ShaderCompiler.Compiler.Compile()`   |
| Linking         | `gl.CreateProgram` + `gl.LinkProgram`  | `vkCreateGraphicsPipeline` (per-pipeline)     |
| Parameters      | Uniforms (`uProjection`, `uColor`)     | Push constants (80 bytes: mat4 + vec4)        |
| Binding         | `shader.Use()` per draw call           | `vkCmdBindPipeline()` per draw call           |
| Count           | 3 programs (flat, texture, ellipse)    | 3 pipelines (flat, texture, ellipse)          |

### Shader Source Changes

```glsl
// OLD (GLSL 330)
#version 330 core
layout(location = 0) in vec2 aPos;
uniform mat4 uProjection;
uniform vec4 uColor;

// NEW (GLSL 450)
#version 450
layout(location = 0) in vec2 aPos;
layout(push_constant) uniform PC { mat4 proj; vec4 color; } pc;
```

All `uniform` declarations become `layout(push_constant) uniform PC { ... } pc;`.
All references to `uProjection` become `pc.proj`, `uColor` becomes `pc.color`.
Texture sampler uses `layout(set = 0, binding = 0)` instead of `uniform sampler2D`.

### Uniform Setting → Push Constants

```csharp
// OLD
shader.Use();
shader.SetMatrix4("uProjection", _projection);    // gl.UniformMatrix4()
shader.SetVector4("uColor", r, g, b, a);          // gl.Uniform4()

// NEW — single 80-byte push constant block
// _pushConstants[0..15] = projection matrix (set once per resize)
// _pushConstants[16..19] = color (set per draw call)
fixed (float* pPC = _pushConstants)
    api.vkCmdPushConstants(cmd, pipelineLayout,
        VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 80, pPC);
```

### Shader Compilation

```csharp
// OLD
var shader = gl.CreateShader(ShaderType.VertexShader);
gl.ShaderSource(shader, source);
gl.CompileShader(shader);
gl.GetShader(shader, ShaderParameterName.CompileStatus, out var status);

// NEW
var options = new CompilerOptions {
    TargetEnv = TargetEnvironmentVersion.Vulkan_1_0,
    ShaderStage = ShaderKind.VertexShader
};
var result = compiler.Compile(source, "shader.vert", options);  // 3 args
var spirv = result.Bytecode;                                     // not GetBytes()
fixed (byte* pSpirv = spirv) {
    VkShaderModuleCreateInfo createInfo = new() {
        codeSize = (nuint)spirv.Length,
        pCode = (uint*)pSpirv
    };
    deviceApi.vkCreateShaderModule(&createInfo, null, out var module).CheckResult();
}
```

### Pipeline State (new in Vulkan)

Each pipeline bundles what OpenGL sets globally:

| OpenGL Global State                        | Vulkan Pipeline State                    |
|-------------------------------------------|------------------------------------------|
| `gl.Enable(EnableCap.Blend)`              | `VkPipelineColorBlendAttachmentState`    |
| `gl.BlendFunc(SrcAlpha, OneMinusSrcAlpha)` | `srcColorBlendFactor`, `dstColorBlendFactor` |
| Input layout (`VertexAttribPointer`)       | `VkVertexInputBindingDescription` + `VkVertexInputAttributeDescription` |
| Primitive topology (Triangles)             | `VkPipelineInputAssemblyStateCreateInfo` |
| Viewport/Scissor                           | Dynamic state (`VkDynamicState.Viewport`, `VkDynamicState.Scissor`) |

---

## 5. Renderer: GlRenderer → VkRenderer

### Class Signature

```csharp
// OLD
public sealed class GlRenderer : Renderer<GL>
// NEW
public sealed unsafe class VkRenderer : Renderer<VulkanContext>
```

### Constructor

```csharp
// OLD
public GlRenderer(GL gl, uint width, uint height) : base(gl) {
    _flatShader = GlShaderProgram.Create(gl, ...);
    _textureShader = GlShaderProgram.Create(gl, ...);
    _ellipseShader = GlShaderProgram.Create(gl, ...);
    _fontAtlas = new GlFontAtlas(gl);
    _vao = gl.GenVertexArray();
    _vbo = gl.GenBuffer();
}

// NEW
public VkRenderer(VulkanContext ctx, uint width, uint height) : base(ctx) {
    _pipelines = VkPipelineSet.Create(ctx);
    _fontAtlas = new VkFontAtlas(ctx);
}
// No VAO/VBO — vertex data is written to VulkanContext's mapped staging buffer
```

### Frame Lifecycle (new in Vulkan)

OpenGL has no explicit frame begin/end — you just issue draw calls. Vulkan requires explicit frame management:

```csharp
// NEW — caller must bracket draw calls
renderer.BeginFrame(clearColor);   // acquires swapchain image, begins command buffer & render pass
// ... draw calls ...
renderer.EndFrame();               // ends render pass, submits command buffer, presents
```

### Vertex Upload & Draw

```csharp
// OLD — per-draw-call upload via GL buffer
private void UploadAndDraw(ReadOnlySpan<float> vertices, int components, int vertexCount) {
    Surface.BindVertexArray(_vao);
    Surface.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
    Surface.BufferData<float>(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.StreamDraw);
    Surface.EnableVertexAttribArray(0);
    Surface.VertexAttribPointer(0, components, GLEnum.Float, false, ...);
    Surface.DrawArrays(PrimitiveType.Triangles, 0, (uint)vertexCount);
}

// NEW — append to persistent mapped buffer, bind at offset
var offset = Surface.WriteVertices(vertices);              // memcpy to mapped buffer
var buffer = Surface.VertexBuffer;
var vkOffset = (ulong)offset;
api.vkCmdBindVertexBuffers(cmd, 0, 1, &buffer, &vkOffset);
api.vkCmdDraw(cmd, 6, 1, 0, 0);
```

### FillRectangle

```csharp
// OLD
_flatShader.Use();
_flatShader.SetMatrix4("uProjection", _projection);
SetColor(_flatShader, fillColor);
UploadAndDraw(vertices, 2, 6);

// NEW
SetColor(fillColor);
var offset = Surface.WriteVertices(vertices);
api.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _pipelines.FlatPipeline);
fixed (float* pPC = _pushConstants)
    api.vkCmdPushConstants(cmd, Surface.PipelineLayout,
        VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 80, pPC);
var buffer = Surface.VertexBuffer;
var vkOffset = (ulong)offset;
api.vkCmdBindVertexBuffers(cmd, 0, 1, &buffer, &vkOffset);
api.vkCmdDraw(cmd, 6, 1, 0, 0);
```

### FillEllipse — Same pattern but uses `_pipelines.EllipsePipeline`

### DrawText

```csharp
// OLD
_textureShader.Use();
_textureShader.SetMatrix4("uProjection", _projection);
SetColor(_textureShader, fontColor);
_textureShader.SetInt("uTexture", 0);
Surface.ActiveTexture(TextureUnit.Texture0);
Surface.BindTexture(TextureTarget.Texture2D, _fontAtlas.TextureHandle);
// ... per-glyph: UploadAndDrawInterleaved(...)
_fontAtlas.Flush();     // uploads dirty region via glTexSubImage2D

// NEW
SetColor(fontColor);
api.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _pipelines.TexturedPipeline);
var descriptorSet = Surface.DescriptorSet;
api.vkCmdBindDescriptorSets(cmd, VkPipelineBindPoint.Graphics,
    Surface.PipelineLayout, 0, 1, &descriptorSet, 0, null);
// ... per-glyph: WriteVertices + vkCmdDraw
// Flush happens in BeginFrame, BEFORE the render pass (staging buffer → image copy)
```

### Clear

```csharp
// OLD
public void Clear(RGBAColor32 color) {
    Surface.ClearColor(r, g, b, a);
    Surface.Clear(ClearBufferMask.ColorBufferBit);
}
// NEW — Clear is part of the render pass (VkClearValue in BeginRenderPass)
// No separate Clear() method needed
```

### Resize

```csharp
// OLD
public override void Resize(uint w, uint h) {
    Surface.Viewport(0, 0, w, h);
    UpdateProjection();
}
// NEW
public override void Resize(uint w, uint h) {
    Surface.RecreateSwapchain(w, h);    // destroys & recreates swapchain + framebuffers
    UpdateProjection();
}
```

### SetColor

```csharp
// OLD — uniform per shader
private static void SetColor(GlShaderProgram shader, RGBAColor32 color) {
    shader.SetVector4("uColor", r/255f, g/255f, b/255f, a/255f);
}
// NEW — writes to push constant float array
private void SetColor(RGBAColor32 color) {
    _pushConstants[16] = color.Red / 255f;
    _pushConstants[17] = color.Green / 255f;
    _pushConstants[18] = color.Blue / 255f;
    _pushConstants[19] = color.Alpha / 255f;
}
```

### Dispose

```csharp
// OLD
_fontAtlas?.Dispose(); _flatShader?.Dispose(); _textureShader?.Dispose(); _ellipseShader?.Dispose();
if (_vbo != 0) Surface.DeleteBuffer(_vbo);
if (_vao != 0) Surface.DeleteVertexArray(_vao);

// NEW
_fontAtlas?.Dispose();
_pipelines?.Dispose();
// No VAO/VBO to clean up — owned by VulkanContext
```

---

## 6. Font Atlas: GlFontAtlas → VkFontAtlas

### Constructor

```csharp
// OLD
public GlFontAtlas(GL gl, int initialWidth = 2048, int initialHeight = 2048) {
    _textureHandle = gl.GenTexture();
    gl.BindTexture(TextureTarget.Texture2D, _textureHandle);
    gl.TexParameter(...);   // min/mag filter, wrap mode
    gl.TexImage2D<byte>(..., ReadOnlySpan<byte>.Empty);  // allocate empty GPU texture
}

// NEW
public VkFontAtlas(VulkanContext ctx, int initialWidth = 512, int initialHeight = 512) {
    CreateImage(initialWidth, initialHeight);     // VkImage + VkImageView
    CreateSampler();                               // VkSampler
    ctx.UpdateDescriptorSet(_imageView, _sampler); // bind to descriptor set
}
```

Note: Initial size is 512x512 (not 2048) with dynamic `Grow()` up to 2048.

### Flush — Texture Upload

```csharp
// OLD — direct GL texture sub-image upload
public void Flush() {
    gl.BindTexture(TextureTarget.Texture2D, _textureHandle);
    gl.TexSubImage2D<byte>(..., rgba.AsSpan());
}

// NEW — staging buffer + command buffer copy
public void Flush(VkCommandBuffer cmd) {
    EnsureUploadBuffer(bufferSize);
    // Map staging buffer, memcpy RGBA data
    vkMapMemory(_uploadMemory, ...);
    Buffer.MemoryCopy(pRgba, mapped, ...);
    vkUnmapMemory(_uploadMemory);
    // Transition image layout for transfer
    TransitionImageLayout(cmd, _image, ShaderReadOnlyOptimal, TransferDstOptimal);
    // Copy buffer to image
    vkCmdCopyBufferToImage(cmd, _uploadBuffer, _image, TransferDstOptimal, ...);
    // Transition back for shader reading
    TransitionImageLayout(cmd, _image, TransferDstOptimal, ShaderReadOnlyOptimal);
}
```

### Flush Timing

```
OLD:  DrawText() → GetGlyph() → ... → Flush() at end of DrawText  (during render)
NEW:  DrawText() → GetGlyph() → ...  (during render pass)
      BeginFrame() → _fontAtlas.Flush(cmd)  (BEFORE render pass, in command buffer)
      After EndFrame() → check FontAtlasDirty → schedule extra frame if dirty
```

New glyphs are rasterized during `DrawText` (which runs inside the render pass), but the upload must happen outside the render pass. So `Flush` is called in `BeginFrame` before the render pass starts. If new glyphs were added during the frame, `FontAtlasDirty` triggers a follow-up frame.

### Grow (atlas resize)

```csharp
// OLD — just call gl.TexImage2D with new size (GL handles old data discard)
// (old code didn't have Grow — atlas was fixed at 2048x2048)

// NEW — must destroy and recreate Vulkan image + image view + re-bind descriptor set
private void Grow() {
    _atlasWidth = Math.Min(_atlasWidth * 2, MaxAtlasSize);
    _atlasHeight = Math.Min(_atlasHeight * 2, MaxAtlasSize);
    // Re-create MagickImage staging, composite old content
    // Re-scale UV coordinates in glyph cache
    // Destroy old VkImage, VkImageView, VkDeviceMemory
    // Create new image at new size
    // Re-bind descriptor set
    // Mark entire atlas dirty for re-upload
}
```

### Image Layout Transitions (new in Vulkan)

OpenGL has no concept of image layouts. Vulkan requires explicit transitions:

```csharp
private void TransitionImageLayout(VkCommandBuffer cmd, VkImage image,
    VkImageLayout oldLayout, VkImageLayout newLayout) {
    // VkImageMemoryBarrier with appropriate srcAccessMask/dstAccessMask
    // and srcStageMask/dstStageMask
    vkCmdPipelineBarrier(cmd, srcStage, dstStage, ...);
}
```

Supported transitions:
- `Undefined` → `ShaderReadOnlyOptimal` (initial)
- `Undefined` → `TransferDstOptimal` (for upload)
- `ShaderReadOnlyOptimal` → `TransferDstOptimal` (before upload)
- `TransferDstOptimal` → `ShaderReadOnlyOptimal` (after upload)

### Texture Binding

```csharp
// OLD — GL texture handle bound per-frame
Surface.ActiveTexture(TextureUnit.Texture0);
Surface.BindTexture(TextureTarget.Texture2D, _fontAtlas.TextureHandle);

// NEW — descriptor set bound once, updated when atlas changes
api.vkCmdBindDescriptorSets(cmd, VkPipelineBindPoint.Graphics,
    pipelineLayout, 0, 1, &descriptorSet, 0, null);
// UpdateDescriptorSet() called only when image view changes (atlas grow)
```

---

## 7. Game Display: OpenGLGameDisplay → VkGameDisplay

### Constructor

```csharp
// OLD — owns window reference, subscribes to window events
public OpenGLGameDisplay(IWindow window, GlRenderer renderer) {
    _window = window;
    _window.Render += OnRender;
    _window.Resize += OnResize;
}

// NEW — render-only, no window reference
public VkGameDisplay(VkRenderer renderer) {
    _renderer = renderer;
}
```

### Render Triggering

```csharp
// OLD — callback from window.Render event
private void OnRender(double deltaTime) { ... }

// NEW — called explicitly from main loop
public void Render() { ... }
```

### Resize Handling

```csharp
// OLD — callback from window.Resize event
private void OnResize(Vector2D<int> size) {
    _renderer.Resize((uint)size.X, (uint)size.Y);
    ...
}

// NEW — called from main loop on SDL resize event
public void OnResize(int width, int height) {
    // Only updates GameUI layout — renderer resize is done in Program.cs
    ...
}
```

### Cross-Thread Update Signaling (new in Vulkan version)

```csharp
// OLD — Silk.NET's render callback always fires, so no signaling needed

// NEW — demand-driven rendering needs dirty flag
private volatile bool _hasPendingUpdate;
public bool HasPendingUpdate { get { var val = _hasPendingUpdate; _hasPendingUpdate = false; return val; } }

// RenderInitial, RenderMove, ResetGame all set _hasPendingUpdate = true
// Main loop checks: if (display is { HasPendingUpdate: true }) needsRedraw = true;
```

### HandleResize

```csharp
// OLD — handled by Silk.NET Resize callback
public void HandleResize(Game game) { /* no-op */ }

// NEW — also no-op, but for a different reason: SDL events handle resize via OnResize
public void HandleResize(Game game) { }
// IMPORTANT: must be no-op, otherwise GameLoop calls it every 16ms causing 7%+ GPU usage
```

---

## 8. Startup Menu: OpenGLStartupMenu → VkStartupMenu

Minimal changes — only input types differ:

```csharp
// OLD
public void HandleKey(Key key) {
    switch (key) {
        case Key.Up: ...
        case Key.Down: ...
        case Key.Enter: ...
        default: var digit = key switch { Key.Number1 => 0, Key.Number2 => 1, Key.Number3 => 2, ... };
    }
}

// NEW
public void HandleKey(Scancode key) {
    switch (key) {
        case Scancode.Up: ...
        case Scancode.Down: ...
        case Scancode.Return: ...
        default: var digit = key switch { Scancode.Alpha1 => 0, Scancode.Alpha2 => 1, Scancode.Alpha3 => 2, ... };
    }
}
```

Render method signature changed from `GlRenderer` to `VkRenderer` but the rendering logic is identical. The `renderer.Clear()` call was removed (clear is now part of the render pass).

---

## 9. Human Player: Silk.NET Input → SDL3 Events

### Key Differences

| Aspect               | Old (Silk.NET)                              | New (SDL3)                           |
|----------------------|---------------------------------------------|--------------------------------------|
| Event source         | Silk.NET callbacks via `Attach(IInputContext)` | Public `Enqueue*` methods called from Program.cs |
| Key type             | `Silk.NET.Input.Key`                        | `SDL3.SDL.Scancode`                  |
| Ctrl detection       | `keyboard.IsKeyPressed(Key.ControlLeft)`    | `(keymod & Keymod.Ctrl) != 0`       |
| Mouse position       | `mouse.Position` (float)                    | `evt.Button.X/Y` (float, cast to int) |
| Scroll               | `ScrollWheel.Y`                             | `evt.Wheel.Y`                        |
| Digit keys           | `Key.Number1`–`Key.Number8`                 | `Scancode.Alpha1`–`Scancode.Alpha8`  |
| Page keys            | `Key.PageUp`, `Key.PageDown`                | `Scancode.Pageup`, `Scancode.Pagedown` |

---

## 10. Program.cs: Callback-Driven → Explicit Poll Loop

### Architecture Change

```
OLD (Silk.NET):
  window.Load += () => { ... };     // init GL context
  window.Render += (_) => { ... };  // draw each frame
  window.Resize += (size) => { ... };
  window.Closing += () => { ... };
  while (!window.IsClosing) {
      window.DoEvents();
      window.DoUpdate();
      window.DoRender();             // fires Render callback
  }

NEW (SDL3 + Vulkan):
  var sdlWindow = SdlVulkanWindow.Create(...);
  var ctx = VulkanContext.Create(...);
  var renderer = new VkRenderer(ctx, ...);
  while (running) {
      var hadEvent = needsRedraw
          ? PollEvent(out evt)            // non-blocking when already dirty
          : WaitEventTimeout(out evt, 16); // block up to 16ms when idle
      // ... process events, set needsRedraw ...
      if (!needsRedraw) continue;
      renderer.BeginFrame(clearColor);
      // ... render ...
      renderer.EndFrame();
      if (renderer.FontAtlasDirty) needsRedraw = true;
  }
```

### Demand-Driven Rendering

The old Silk.NET loop rendered every frame unconditionally. The new loop uses a `needsRedraw` flag:

- **Set to true** by: SDL events (resize, keydown, mouse, expose), `display.HasPendingUpdate`, `renderer.FontAtlasDirty`
- **When idle**: `WaitEventTimeout(out evt, 16)` blocks the thread — GPU usage drops to ~0%
- **When dirty**: `PollEvent(out evt)` drains events without blocking, then renders immediately

### Swapchain Out-of-Date Handling

```csharp
if (!renderer.BeginFrame(bgColor)) {
    // Swapchain out of date — query current size and resize
    sdlWindow.GetSizeInPixels(out var sw, out var sh);
    if (sw > 0 && sh > 0) renderer.Resize((uint)sw, (uint)sh);
    needsRedraw = true;
    continue;
}
```

---

## 11. Vulkan Infrastructure (New)

### VulkanContext — Central Device Manager

No OpenGL equivalent. Owns the entire Vulkan device lifecycle:

| Component                    | Purpose                                           |
|------------------------------|---------------------------------------------------|
| `VkInstance` + `VkInstanceApi` | Vulkan instance + instance-level function pointers |
| `VkPhysicalDevice`          | Selected GPU                                       |
| `VkDevice` + `VkDeviceApi`  | Logical device + device-level function pointers    |
| `VkQueue`                   | Graphics + present queue                           |
| `VkCommandPool`             | Command buffer allocation                          |
| `VkRenderPass`              | Single-subpass, clear-on-load, present-on-store    |
| `VkDescriptorPool/Set/Layout` | Font atlas texture binding                       |
| `VkPipelineLayout`          | Push constants (80B) + 1 descriptor set            |
| `VkSwapchainKHR`            | Double-buffered presentation                       |
| Per-frame sync objects       | 2× semaphores (image available, render finished) + fence |
| Per-frame vertex buffers     | 2× 4MB host-visible mapped buffers                |

### Vortice.Vulkan API Pattern

```csharp
// Instance-level functions (physical device enumeration, surface queries)
var instanceApi = Vulkan.GetApi(instance);
instanceApi.vkEnumeratePhysicalDevices(&count, null);

// Device-level functions (all rendering operations)
var deviceApi = Vulkan.GetApi(instance, device);
deviceApi.vkCmdDraw(cmd, vertexCount, 1, 0, 0);
```

**Critical**: Must call `vkInitialize()` before any Vulkan calls, or you get a segfault.

### Per-Frame Vertex Buffers

```csharp
// Why: With double-buffered frames, a single vertex buffer causes race conditions
// when content changes between frames (manifests as brief triangle artifacts)
private readonly VkBuffer[] _vertexBuffers = new VkBuffer[MaxFramesInFlight];     // 2 buffers
private readonly VkDeviceMemory[] _vertexMemories = new VkDeviceMemory[MaxFramesInFlight];
private readonly float*[] _vertexMapped = new float*[MaxFramesInFlight];          // persistently mapped

public uint WriteVertices(ReadOnlySpan<float> data) {
    var byteOffset = (uint)(_vertexOffset * sizeof(float));
    data.CopyTo(new Span<float>(_vertexMapped[_currentFrame] + _vertexOffset, data.Length));
    _vertexOffset += data.Length;
    return byteOffset;
}
```

### Frame Synchronization

```
BeginFrame:
  1. Wait for in-flight fence (ensures previous frame using this index is complete)
  2. Acquire next swapchain image (with image-available semaphore)
  3. Reset fence, reset command buffer
  4. Begin command buffer (one-time submit)
  5. Reset vertex offset for this frame

EndFrame:
  1. End render pass, end command buffer
  2. Submit with wait=imageAvailable, signal=renderFinished
  3. Present with wait=renderFinished
  4. Advance frame index: (current + 1) % 2
```

### SdlVulkanWindow — Window + Instance Lifecycle

```csharp
// Creates SDL window with Vulkan flag
// Creates VkInstance with SDL-required extensions
// Creates VkSurfaceKHR via SDL_Vulkan_CreateSurface
// Provides ToggleFullscreen, GetSizeInPixels

// Validation layers enabled in DEBUG builds only:
#if DEBUG
    using var validationLayers = new VkStringArray(["VK_LAYER_KHRONOS_validation"]);
    instanceCI.enabledLayerCount = validationLayers.Length;
    instanceCI.ppEnabledLayerNames = validationLayers;
#endif
```

---

## 12. Coordinate System Differences

### Clip Space Y-Axis

```
OpenGL: Y points UP    (bottom = -1, top = +1)
Vulkan: Y points DOWN  (top = -1, bottom = +1)
```

### Orthographic Projection Matrix

```csharp
// OLD (OpenGL) — must flip Y to get (0,0) at top-left
_projection[0]  = 2f / w;     // m00
_projection[5]  = -2f / h;    // m11  ← NEGATIVE (flips Y)
_projection[10] = -1f;        // m22
_projection[12] = -1f;        // m30
_projection[13] = 1f;         // m31  ← POSITIVE
_projection[15] = 1f;         // m33

// NEW (Vulkan) — Y already points down, no flip needed
_pushConstants[0]  = 2f / w;  // m00
_pushConstants[5]  = 2f / h;  // m11  ← POSITIVE (no flip)
_pushConstants[10] = -1f;     // m22
_pushConstants[12] = -1f;     // m30
_pushConstants[13] = -1f;     // m31  ← NEGATIVE
_pushConstants[15] = 1f;      // m33
```

The key differences are `m11` and `m31`:
- OpenGL: `m11 = -2/h`, `m31 = +1` (flip Y from bottom-up to top-down)
- Vulkan: `m11 = +2/h`, `m31 = -1` (Y already top-down, just scale and translate)

---

## 13. Gotchas & Lessons Learned

### 1. `vkInitialize()` is mandatory
Must call `Vulkan.vkInitialize()` before any Vulkan API calls. Without it: segfault at `vkCreateInstance`.

### 2. SDL3-CS Scancode names differ from Silk.NET Key names
- Digits: `Alpha1`–`Alpha8` (not `Number1`–`Number8`, not `_1`–`_8`)
- Page keys: `Pageup`/`Pagedown` (not `PageUp`/`PageDown`)
- Enter: `Return` (not `Enter`)

### 3. Font atlas flush timing
Glyphs are rasterized during `DrawText` (inside render pass) but must be uploaded to the GPU image outside the render pass. Solution: flush in `BeginFrame` before the render pass, then check `FontAtlasDirty` after `EndFrame` to schedule a follow-up render.

### 4. Per-frame vertex buffers
A single vertex buffer shared across double-buffered frames causes brief triangle artifacts when content changes. Each frame-in-flight needs its own vertex buffer.

### 5. `HandleResize` must be a no-op
`GameLoop` calls `HandleResize` every 16ms. If it sets a dirty flag, the GPU never goes idle (7%+ usage). SDL window resize events handle actual resizes.

### 6. Demand-driven rendering
Use `WaitEventTimeout` when idle (GPU at 0%) and `PollEvent` when already dirty (responsive rendering). Checking `FontAtlasDirty` after `EndFrame` ensures newly rasterized glyphs appear on the next frame.

### 7. Vortice.ShaderCompiler API
- `Compile(source, fileName, options)` — 3 arguments
- Result: `result.Bytecode` (not `result.GetBytes()`)
- `CompilerOptions`: set `TargetEnv` and `ShaderStage` properties
- Must manually call `vkCreateShaderModule` with the SPIR-V bytes

### 8. Vortice.Vulkan API dispatch
Use `VkInstanceApi` (from `GetApi(instance)`) for instance-level calls and `VkDeviceApi` (from `GetApi(instance, device)`) for device-level calls. Global static `vk*` functions only work for loader-level functions like `vkInitialize()` and `vkCreateInstance()`.

---

## 14. AOT Publishing

Both the old and new versions support `PublishAot`. The main change:

```xml
<!-- REMOVED: Silk.NET needed this to suppress reflection warnings -->
<TrimmerSingleWarn>true</TrimmerSingleWarn>

<!-- REMOVED: Silk.NET needed explicit platform registration for AOT -->
GlfwWindowing.RegisterPlatform();
GlfwInput.RegisterPlatform();
```

SDL3-CS and Vortice.Vulkan are AOT-compatible out of the box. No reflection-based discovery, no platform registration needed.

### Build Command

```bash
# Note: vswhere.exe must be on PATH for the native linker
export PATH="/c/Program Files (x86)/Microsoft Visual Studio/Installer:$PATH"
dotnet publish Chess.OpenGL/Chess.OpenGL.csproj -c Release
```

### Output Sizes (ARM64)

| Binary             | Size   |
|--------------------|--------|
| Chess.OpenGL.exe   | ~4.1 MB |
| chess-engine.exe   | ~1.7 MB |
| SDL3.dll           | ~2.4 MB |
| Magick.Native      | ~19 MB  |
| shaderc_shared.dll | ~6 MB   |
| vma.dll            | ~0.2 MB |
