using System;
using System.Runtime.InteropServices;
using Avalonia.OpenGL;
using MediaPlayer.Controls.Backends;
using static Avalonia.OpenGL.GlConsts;

namespace MediaPlayer.Controls.Rendering;

internal sealed class OpenGlVideoRenderer
{
    private const int GlTextureWrapS = 0x2802;
    private const int GlTextureWrapT = 0x2803;
    private const int GlClampToEdge = 0x812F;

    private int _program;
    private int _vertexShader;
    private int _fragmentShader;
    private int _vertexBuffer;
    private int _texture;
    private int _vertexArray;
    private int _samplerUniformLocation;
    private int _videoWidth;
    private int _videoHeight;
    private bool _initialized;
    private GlVersion _glVersion;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate void GlGenVertexArraysDelegate(int n, uint* arrays);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlBindVertexArrayDelegate(uint array);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate void GlDeleteVertexArraysDelegate(int n, uint* arrays);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlUniform1iDelegate(int location, int value);

    private GlGenVertexArraysDelegate? _glGenVertexArrays;
    private GlBindVertexArrayDelegate? _glBindVertexArray;
    private GlDeleteVertexArraysDelegate? _glDeleteVertexArrays;
    private GlUniform1iDelegate? _glUniform1i;

    public void Initialize(GlInterface gl, GlVersion glVersion)
    {
        if (_initialized)
        {
            return;
        }

        _glVersion = glVersion;
        _vertexShader = CompileShader(gl, GL_VERTEX_SHADER, GetVertexShaderSource(glVersion));
        _fragmentShader = CompileShader(gl, GL_FRAGMENT_SHADER, GetFragmentShaderSource(glVersion));

        _program = gl.CreateProgram();
        gl.BindAttribLocationString(_program, 0, "aPos");
        gl.BindAttribLocationString(_program, 1, "aTex");
        gl.AttachShader(_program, _vertexShader);
        gl.AttachShader(_program, _fragmentShader);

        var linkError = gl.LinkProgramAndGetError(_program);
        if (!string.IsNullOrWhiteSpace(linkError))
        {
            throw new InvalidOperationException($"Failed to link video shader program. {linkError}");
        }

        _samplerUniformLocation = gl.GetUniformLocationString(_program, "uTex");
        _glUniform1i = TryLoadDelegate<GlUniform1iDelegate>(gl, "glUniform1i");

        _vertexBuffer = gl.GenBuffer();
        _texture = gl.GenTexture();

        _glGenVertexArrays = TryLoadDelegate<GlGenVertexArraysDelegate>(gl, "glGenVertexArrays");
        _glBindVertexArray = TryLoadDelegate<GlBindVertexArrayDelegate>(gl, "glBindVertexArray");
        _glDeleteVertexArrays = TryLoadDelegate<GlDeleteVertexArraysDelegate>(gl, "glDeleteVertexArrays");

        if (_glGenVertexArrays is not null && _glBindVertexArray is not null)
        {
            unsafe
            {
                uint vao = 0;
                _glGenVertexArrays(1, &vao);
                _vertexArray = unchecked((int)vao);
                _glBindVertexArray(vao);
            }
        }

        gl.BindTexture(GL_TEXTURE_2D, _texture);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        gl.TexParameteri(GL_TEXTURE_2D, GlTextureWrapS, GlClampToEdge);
        gl.TexParameteri(GL_TEXTURE_2D, GlTextureWrapT, GlClampToEdge);
        gl.BindTexture(GL_TEXTURE_2D, 0);

        _initialized = true;
    }

    public void UploadFrame(GlInterface gl, in MediaFrameLease frame)
    {
        EnsureInitialized();

        _videoWidth = frame.Width;
        _videoHeight = frame.Height;

        gl.ActiveTexture(GL_TEXTURE0);
        gl.BindTexture(GL_TEXTURE_2D, _texture);
        var pixelFormat = frame.PixelFormat == MediaFramePixelFormat.Bgra32 ? GL_BGRA : GL_RGBA;
        gl.TexImage2D(
            GL_TEXTURE_2D,
            level: 0,
            internalFormat: GL_RGBA,
            width: frame.Width,
            height: frame.Height,
            border: 0,
            format: pixelFormat,
            type: GL_UNSIGNED_BYTE,
            data: frame.Data);
        gl.BindTexture(GL_TEXTURE_2D, 0);
    }

    public unsafe void Render(GlInterface gl, int framebuffer, int width, int height)
    {
        EnsureInitialized();

        gl.BindFramebuffer(GL_FRAMEBUFFER, framebuffer);
        gl.Viewport(0, 0, width, height);
        gl.ClearColor(0f, 0f, 0f, 1f);
        gl.Clear(GL_COLOR_BUFFER_BIT);

        if (_videoWidth <= 0 || _videoHeight <= 0)
        {
            return;
        }

        var vertices = BuildAspectFittedQuad(width, height, _videoWidth, _videoHeight);

        gl.BindBuffer(GL_ARRAY_BUFFER, _vertexBuffer);
        fixed (float* ptr = vertices)
        {
            gl.BufferData(
                GL_ARRAY_BUFFER,
                new IntPtr(vertices.Length * sizeof(float)),
                new IntPtr(ptr),
                GL_STATIC_DRAW);
        }

        gl.UseProgram(_program);
        if (_vertexArray != 0 && _glBindVertexArray is not null)
        {
            _glBindVertexArray(unchecked((uint)_vertexArray));
        }

        gl.ActiveTexture(GL_TEXTURE0);
        if (_samplerUniformLocation >= 0 && _glUniform1i is not null)
        {
            _glUniform1i(_samplerUniformLocation, 0);
        }

        gl.BindTexture(GL_TEXTURE_2D, _texture);

        var stride = 4 * sizeof(float);
        gl.VertexAttribPointer(index: 0, size: 2, type: GL_FLOAT, normalized: 0, stride: stride, pointer: IntPtr.Zero);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(index: 1, size: 2, type: GL_FLOAT, normalized: 0, stride: stride, pointer: new IntPtr(2 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.DrawArrays(GL_TRIANGLES, first: 0, count: new IntPtr(6));

        gl.BindTexture(GL_TEXTURE_2D, 0);
        gl.BindBuffer(GL_ARRAY_BUFFER, 0);
        if (_vertexArray != 0 && _glBindVertexArray is not null)
        {
            _glBindVertexArray(0);
        }

        gl.UseProgram(0);
    }

    public void Dispose(GlInterface gl)
    {
        if (!_initialized)
        {
            return;
        }

        if (_texture != 0)
        {
            gl.DeleteTexture(_texture);
            _texture = 0;
        }

        if (_vertexBuffer != 0)
        {
            gl.DeleteBuffer(_vertexBuffer);
            _vertexBuffer = 0;
        }

        if (_vertexArray != 0 && _glDeleteVertexArrays is not null)
        {
            unsafe
            {
                uint vao = unchecked((uint)_vertexArray);
                _glDeleteVertexArrays(1, &vao);
            }

            _vertexArray = 0;
        }

        if (_program != 0)
        {
            gl.DeleteProgram(_program);
            _program = 0;
        }

        if (_vertexShader != 0)
        {
            gl.DeleteShader(_vertexShader);
            _vertexShader = 0;
        }

        if (_fragmentShader != 0)
        {
            gl.DeleteShader(_fragmentShader);
            _fragmentShader = 0;
        }

        _videoWidth = 0;
        _videoHeight = 0;
        _initialized = false;
    }

    private static T? TryLoadDelegate<T>(GlInterface gl, string proc) where T : class
    {
        var address = gl.GetProcAddress(proc);
        if (address == IntPtr.Zero)
        {
            return null;
        }

        return Marshal.GetDelegateForFunctionPointer(address, typeof(T)) as T;
    }

    private static float[] BuildAspectFittedQuad(int viewportWidth, int viewportHeight, int videoWidth, int videoHeight)
    {
        var viewportAspect = (float)viewportWidth / viewportHeight;
        var videoAspect = (float)videoWidth / videoHeight;

        var scaleX = 1f;
        var scaleY = 1f;

        if (videoAspect > viewportAspect)
        {
            scaleY = viewportAspect / videoAspect;
        }
        else
        {
            scaleX = videoAspect / viewportAspect;
        }

        return
        [
            -scaleX, -scaleY, 0f, 1f,
             scaleX, -scaleY, 1f, 1f,
             scaleX,  scaleY, 1f, 0f,

            -scaleX, -scaleY, 0f, 1f,
             scaleX,  scaleY, 1f, 0f,
            -scaleX,  scaleY, 0f, 0f
        ];
    }

    private static int CompileShader(GlInterface gl, int shaderType, string source)
    {
        var shader = gl.CreateShader(shaderType);
        var compileError = gl.CompileShaderAndGetError(shader, source);
        if (!string.IsNullOrWhiteSpace(compileError))
        {
            throw new InvalidOperationException($"Failed to compile OpenGL shader. {compileError}");
        }

        return shader;
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("OpenGlVideoRenderer must be initialized before rendering.");
        }
    }

    private static string GetVertexShaderSource(GlVersion glVersion)
    {
        if (glVersion.Type == GlProfileType.OpenGLES)
        {
            return
                """
                attribute vec2 aPos;
                attribute vec2 aTex;
                varying vec2 vTex;

                void main()
                {
                    vTex = aTex;
                    gl_Position = vec4(aPos, 0.0, 1.0);
                }
                """;
        }

        return
            """
            #version 150
            in vec2 aPos;
            in vec2 aTex;
            out vec2 vTex;

            void main()
            {
                vTex = aTex;
                gl_Position = vec4(aPos, 0.0, 1.0);
            }
            """;
    }

    private static string GetFragmentShaderSource(GlVersion glVersion)
    {
        if (glVersion.Type == GlProfileType.OpenGLES)
        {
            return
                """
                precision mediump float;
                varying vec2 vTex;
                uniform sampler2D uTex;

                void main()
                {
                    vec4 c = texture2D(uTex, vTex);
                    gl_FragColor = vec4(c.rgb, 1.0);
                }
                """;
        }

        return
            """
            #version 150
            in vec2 vTex;
            out vec4 outColor;
            uniform sampler2D uTex;

            void main()
            {
                vec4 c = texture(uTex, vTex);
                outColor = vec4(c.rgb, 1.0);
            }
            """;
    }
}
