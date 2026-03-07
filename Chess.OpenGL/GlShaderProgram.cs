using Silk.NET.OpenGL;

namespace Chess.OpenGL;

/// <summary>
/// Compiles and links a vertex/fragment shader pair into an OpenGL program.
/// Disposes the intermediate shader objects after linking.
/// </summary>
internal sealed class GlShaderProgram : IDisposable
{
    private readonly GL _gl;

    /// <summary>The linked OpenGL program handle.</summary>
    public uint Handle { get; }

    private GlShaderProgram(GL gl, uint handle)
    {
        _gl = gl;
        Handle = handle;
    }

    /// <summary>
    /// Creates a shader program from vertex and fragment GLSL source strings.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when shader compilation or linking fails.</exception>
    public static GlShaderProgram Create(GL gl, string vertexSource, string fragmentSource)
    {
        var vs = CompileShader(gl, ShaderType.VertexShader, vertexSource);
        var fs = CompileShader(gl, ShaderType.FragmentShader, fragmentSource);

        var handle = gl.CreateProgram();
        gl.AttachShader(handle, vs);
        gl.AttachShader(handle, fs);
        gl.LinkProgram(handle);

        gl.GetProgram(handle, ProgramPropertyARB.LinkStatus, out var status);
        if (status == 0)
        {
            var log = gl.GetProgramInfoLog(handle);
            gl.DeleteProgram(handle);
            gl.DeleteShader(vs);
            gl.DeleteShader(fs);
            throw new InvalidOperationException($"Shader link failed: {log}");
        }

        gl.DetachShader(handle, vs);
        gl.DetachShader(handle, fs);
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);

        return new GlShaderProgram(gl, handle);
    }

    /// <summary>Activates this program for subsequent draw calls.</summary>
    public void Use() => _gl.UseProgram(Handle);

    /// <summary>Sets a uniform <c>mat4</c> value by name.</summary>
    public unsafe void SetMatrix4(string name, ReadOnlySpan<float> matrix)
    {
        var location = _gl.GetUniformLocation(Handle, name);
        if (location >= 0)
        {
            fixed (float* ptr = matrix)
            {
                _gl.UniformMatrix4(location, 1, false, ptr);
            }
        }
    }

    /// <summary>Sets a uniform <c>vec4</c> value by name.</summary>
    public void SetVector4(string name, float x, float y, float z, float w)
    {
        var location = _gl.GetUniformLocation(Handle, name);
        if (location >= 0)
        {
            _gl.Uniform4(location, x, y, z, w);
        }
    }

    /// <summary>Sets a uniform <c>int</c> value by name.</summary>
    public void SetInt(string name, int value)
    {
        var location = _gl.GetUniformLocation(Handle, name);
        if (location >= 0)
        {
            _gl.Uniform1(location, value);
        }
    }

    public void Dispose() => _gl.DeleteProgram(Handle);

    private static uint CompileShader(GL gl, ShaderType type, string source)
    {
        var shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);

        gl.GetShader(shader, ShaderParameterName.CompileStatus, out var status);
        if (status == 0)
        {
            var log = gl.GetShaderInfoLog(shader);
            gl.DeleteShader(shader);
            throw new InvalidOperationException($"{type} compile failed: {log}");
        }

        return shader;
    }
}
