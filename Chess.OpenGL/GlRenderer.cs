using DIR.Lib;
using Silk.NET.OpenGL;

namespace Chess.OpenGL;

/// <summary>
/// OpenGL-based implementation of <see cref="Renderer{TSurface}"/>.
/// Uses a flat-color shader for rectangles/ellipses and a textured shader with a font atlas for text.
/// The <typeparamref name="GL"/> surface provides the OpenGL API context.
/// </summary>
public sealed class GlRenderer : Renderer<GL>
{
    private GlShaderProgram? _flatShader;
    private GlShaderProgram? _textureShader;
    private GlShaderProgram? _ellipseShader;
    private GlFontAtlas? _fontAtlas;
    private uint _vao;
    private uint _vbo;
    private uint _width;
    private uint _height;

    // Orthographic projection matrix (column-major for OpenGL)
    private readonly float[] _projection = new float[16];

    #region Shader sources

    private const string FlatVertexSource = """
        #version 330 core
        layout(location = 0) in vec2 aPos;
        uniform mat4 uProjection;
        void main() {
            gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
        }
        """;

    private const string FlatFragmentSource = """
        #version 330 core
        uniform vec4 uColor;
        out vec4 FragColor;
        void main() {
            FragColor = uColor;
        }
        """;

    private const string TextureVertexSource = """
        #version 330 core
        layout(location = 0) in vec2 aPos;
        layout(location = 1) in vec2 aTexCoord;
        uniform mat4 uProjection;
        out vec2 vTexCoord;
        void main() {
            gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
            vTexCoord = aTexCoord;
        }
        """;

    private const string TextureFragmentSource = """
        #version 330 core
        in vec2 vTexCoord;
        uniform sampler2D uTexture;
        uniform vec4 uColor;
        out vec4 FragColor;
        void main() {
            float alpha = texture(uTexture, vTexCoord).r;
            // Use RGBA channel from atlas (white text pre-rendered)
            vec4 texel = texture(uTexture, vTexCoord);
            FragColor = vec4(uColor.rgb, uColor.a * texel.a);
        }
        """;

    private const string EllipseVertexSource = """
        #version 330 core
        layout(location = 0) in vec2 aPos;
        layout(location = 1) in vec2 aLocalPos;
        uniform mat4 uProjection;
        out vec2 vLocal;
        void main() {
            gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
            vLocal = aLocalPos;
        }
        """;

    private const string EllipseFragmentSource = """
        #version 330 core
        in vec2 vLocal;
        uniform vec4 uColor;
        out vec4 FragColor;
        void main() {
            float dist = dot(vLocal, vLocal);
            if (dist > 1.0) discard;
            FragColor = uColor;
        }
        """;

    #endregion

    /// <summary>
    /// Creates a new OpenGL renderer with the given initial dimensions.
    /// </summary>
    /// <param name="gl">The Silk.NET OpenGL API context.</param>
    /// <param name="width">Initial framebuffer width in pixels.</param>
    /// <param name="height">Initial framebuffer height in pixels.</param>
    public GlRenderer(GL gl, uint width, uint height) : base(gl)
    {
        _width = width;
        _height = height;

        _flatShader = GlShaderProgram.Create(gl, FlatVertexSource, FlatFragmentSource);
        _textureShader = GlShaderProgram.Create(gl, TextureVertexSource, TextureFragmentSource);
        _ellipseShader = GlShaderProgram.Create(gl, EllipseVertexSource, EllipseFragmentSource);
        _fontAtlas = new GlFontAtlas(gl);

        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();

        UpdateProjection();
    }

    /// <inheritdoc />
    public override uint Width => _width;

    /// <inheritdoc />
    public override uint Height => _height;

    /// <summary>The font atlas used for text rendering. Allows pre-loading fonts.</summary>
    internal GlFontAtlas? FontAtlas => _fontAtlas;

    /// <inheritdoc />
    public override void Resize(uint width, uint height)
    {
        _width = width;
        _height = height;
        Surface.Viewport(0, 0, width, height);
        UpdateProjection();
    }

    /// <inheritdoc />
    public override void FillRectangle(in RectInt rect, RGBAColor32 fillColor)
    {
        if (_flatShader is null) return;

        var x0 = (float)rect.UpperLeft.X;
        var y0 = (float)rect.UpperLeft.Y;
        var x1 = (float)rect.LowerRight.X;
        var y1 = (float)rect.LowerRight.Y;

        ReadOnlySpan<float> vertices =
        [
            x0, y0,
            x1, y0,
            x1, y1,
            x0, y0,
            x1, y1,
            x0, y1
        ];

        _flatShader.Use();
        _flatShader.SetMatrix4("uProjection", _projection);
        SetColor(_flatShader, fillColor);

        UploadAndDraw(vertices, 2, 6);
    }

    /// <inheritdoc />
    public override void FillRectangles(ReadOnlySpan<(RectInt Rect, RGBAColor32 Color)> rectangles)
    {
        // Batch by color for fewer state changes, fallback to individual calls
        foreach (var (rect, color) in rectangles)
        {
            FillRectangle(rect, color);
        }
    }

    /// <inheritdoc />
    public override void DrawRectangle(in RectInt rect, RGBAColor32 strokeColor, int strokeWidth)
    {
        var x0 = (float)rect.UpperLeft.X;
        var y0 = (float)rect.UpperLeft.Y;
        var x1 = (float)rect.LowerRight.X;
        var y1 = (float)rect.LowerRight.Y;
        var sw = (float)strokeWidth;

        // Four border rectangles: top, bottom, left, right
        FillRectangle(new RectInt((rect.LowerRight.X, (int)(y0 + sw)), (rect.UpperLeft.X, rect.UpperLeft.Y)), strokeColor);      // top
        FillRectangle(new RectInt((rect.LowerRight.X, rect.LowerRight.Y), ((int)x0, (int)(y1 - sw))), strokeColor);              // bottom
        FillRectangle(new RectInt(((int)(x0 + sw), (int)(y1 - sw)), (rect.UpperLeft.X, (int)(y0 + sw))), strokeColor);           // left
        FillRectangle(new RectInt((rect.LowerRight.X, (int)(y1 - sw)), ((int)(x1 - sw), (int)(y0 + sw))), strokeColor);          // right
    }

    /// <inheritdoc />
    public override void FillEllipse(in RectInt rect, RGBAColor32 fillColor)
    {
        if (_ellipseShader is null) return;

        var x0 = (float)rect.UpperLeft.X;
        var y0 = (float)rect.UpperLeft.Y;
        var x1 = (float)rect.LowerRight.X;
        var y1 = (float)rect.LowerRight.Y;

        // Vertices: position (x,y) + local coords (-1..1, -1..1)
        ReadOnlySpan<float> vertices =
        [
            x0, y0, -1f, -1f,
            x1, y0,  1f, -1f,
            x1, y1,  1f,  1f,
            x0, y0, -1f, -1f,
            x1, y1,  1f,  1f,
            x0, y1, -1f,  1f
        ];

        _ellipseShader.Use();
        _ellipseShader.SetMatrix4("uProjection", _projection);
        SetColor(_ellipseShader, fillColor);

        UploadAndDrawInterleaved(vertices, stride: 4, vertexCount: 6, posComponents: 2, extraComponents: 2);
    }

    /// <inheritdoc />
    public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize, RGBAColor32 fontColor,
        in RectInt layout, TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near)
    {
        if (_textureShader is null || _fontAtlas is null || text.IsEmpty)
            return;

        // Handle multi-line text
        var textStr = text.ToString();
        var lines = textStr.Split('\n');

        var lineHeight = fontSize * 1.3f;
        var totalHeight = lines.Length * lineHeight;

        var layoutX = (float)layout.UpperLeft.X;
        var layoutY = (float)layout.UpperLeft.Y;
        var layoutW = (float)layout.Width;
        var layoutH = (float)layout.Height;

        var startY = vertAlignment switch
        {
            TextAlign.Center => layoutY + (layoutH - totalHeight) / 2f,
            TextAlign.Far => layoutY + layoutH - totalHeight,
            _ => layoutY
        };

        _textureShader.Use();
        _textureShader.SetMatrix4("uProjection", _projection);
        SetColor(_textureShader, fontColor);
        _textureShader.SetInt("uTexture", 0);

        Surface.ActiveTexture(TextureUnit.Texture0);
        Surface.BindTexture(TextureTarget.Texture2D, _fontAtlas.TextureHandle);
        Surface.Enable(EnableCap.Blend);
        Surface.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        for (var lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            var line = lines[lineIdx];
            if (string.IsNullOrEmpty(line)) continue;

            var (textWidth, _) = _fontAtlas.MeasureText(fontFamily, fontSize, line);

            var penX = horizAlignment switch
            {
                TextAlign.Center => layoutX + (layoutW - (float)textWidth) / 2f,
                TextAlign.Far => layoutX + layoutW - (float)textWidth,
                _ => layoutX
            };
            var penY = startY + lineIdx * lineHeight;

            foreach (var ch in line)
            {
                var glyph = _fontAtlas.GetGlyph(fontFamily, fontSize, ch);
                if (glyph.Width == 0) continue;

                var gx0 = penX;
                var gy0 = penY + (lineHeight - glyph.Height) / 2f;
                var gx1 = gx0 + glyph.Width;
                var gy1 = gy0 + glyph.Height;

                ReadOnlySpan<float> vertices =
                [
                    gx0, gy0, glyph.U0, glyph.V0,
                    gx1, gy0, glyph.U1, glyph.V0,
                    gx1, gy1, glyph.U1, glyph.V1,
                    gx0, gy0, glyph.U0, glyph.V0,
                    gx1, gy1, glyph.U1, glyph.V1,
                    gx0, gy1, glyph.U0, glyph.V1
                ];

                UploadAndDrawInterleaved(vertices, stride: 4, vertexCount: 6, posComponents: 2, extraComponents: 2);

                penX += glyph.AdvanceX;
            }
        }

        // Flush atlas after all glyphs requested this frame
        _fontAtlas.Flush();
    }

    /// <summary>
    /// Clears the framebuffer with the given colour. Call at the start of each frame.
    /// </summary>
    public void Clear(RGBAColor32 color)
    {
        Surface.ClearColor(color.Red / 255f, color.Green / 255f, color.Blue / 255f, color.Alpha / 255f);
        Surface.Clear(ClearBufferMask.ColorBufferBit);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _fontAtlas?.Dispose();
        _fontAtlas = null;
        _flatShader?.Dispose();
        _flatShader = null;
        _textureShader?.Dispose();
        _textureShader = null;
        _ellipseShader?.Dispose();
        _ellipseShader = null;

        if (_vbo != 0) { Surface.DeleteBuffer(_vbo); _vbo = 0; }
        if (_vao != 0) { Surface.DeleteVertexArray(_vao); _vao = 0; }
    }

    private void UpdateProjection()
    {
        // Orthographic projection: (0,0) top-left, (width,height) bottom-right
        var w = (float)_width;
        var h = (float)_height;

        Array.Clear(_projection);
        _projection[0] = 2f / w;     // m00
        _projection[5] = -2f / h;    // m11 (flip Y so 0 is top)
        _projection[10] = -1f;       // m22
        _projection[12] = -1f;       // m30
        _projection[13] = 1f;        // m31
        _projection[15] = 1f;        // m33
    }

    private static void SetColor(GlShaderProgram shader, RGBAColor32 color)
    {
        shader.SetVector4("uColor",
            color.Red / 255f,
            color.Green / 255f,
            color.Blue / 255f,
            color.Alpha / 255f);
    }

    private unsafe void UploadAndDraw(ReadOnlySpan<float> vertices, int components, int vertexCount)
    {
        Surface.BindVertexArray(_vao);
        Surface.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        fixed (float* ptr = vertices)
        {
            Surface.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), ptr, BufferUsageARB.StreamDraw);
        }

        Surface.EnableVertexAttribArray(0);
        Surface.VertexAttribPointer(0, components, GLEnum.Float, false, (uint)(components * sizeof(float)), null);

        Surface.DrawArrays(PrimitiveType.Triangles, 0, (uint)vertexCount);
    }

    private unsafe void UploadAndDrawInterleaved(ReadOnlySpan<float> vertices, int stride, int vertexCount, int posComponents, int extraComponents)
    {
        Surface.BindVertexArray(_vao);
        Surface.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        fixed (float* ptr = vertices)
        {
            Surface.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), ptr, BufferUsageARB.StreamDraw);
        }

        var strideBytes = (uint)(stride * sizeof(float));

        Surface.EnableVertexAttribArray(0);
        Surface.VertexAttribPointer(0, posComponents, GLEnum.Float, false, strideBytes, null);

        Surface.EnableVertexAttribArray(1);
        Surface.VertexAttribPointer(1, extraComponents, GLEnum.Float, false, strideBytes, (void*)(posComponents * sizeof(float)));

        Surface.DrawArrays(PrimitiveType.Triangles, 0, (uint)vertexCount);
    }
}
