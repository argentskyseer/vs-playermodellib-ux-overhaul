using System;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace PlayerModelLibUxOverhaul;

/// <summary>
/// See-through, blurred background for the character preview.
///
/// Each frame, before the dialog draws, the screen area behind the preview (the world) is copied into a
/// mipmapped texture. After the dialog background is drawn, that copy is drawn back over the preview area
/// through a blur shader, so the preview looks like frosted glass instead of a dark panel.
/// </summary>
public sealed class PreviewBackdrop : IDisposable
{
    private const string ShaderName = "playermodellib-ux-overhaul-previewblur";

    // Drawn just above the dialog background (z 50) and well below the character.
    private const float QuadZ = 55f;

    // Blur strength 1 samples this mip level; each level halves the resolution.
    private const float MaxLod = 5f;

    private const string VertexCode = @"#version 330 core
layout(location = 0) in vec3 vertexPositionIn;
uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
void main()
{
    gl_Position = projectionMatrix * modelViewMatrix * vec4(vertexPositionIn, 1.0);
}
";

    // UVs come from gl_FragCoord so they match the copied screen rectangle exactly, whatever the quad's orientation.
    private const string FragmentCode = @"#version 330 core
uniform sampler2D tex;
uniform vec2 rectOrigin;
uniform vec2 rectSize;
uniform float lod;
uniform vec4 tint;
out vec4 outColor;
void main()
{
    vec2 uv = (gl_FragCoord.xy - rectOrigin) / rectSize;
    vec2 step = exp2(lod) / rectSize;
    vec2 lo = step * 0.5;
    vec2 hi = vec2(1.0) - step * 0.5;
    vec3 sum = vec3(0.0);
    float weights = 0.0;
    for (int x = -3; x <= 3; x++)
    {
        for (int y = -3; y <= 3; y++)
        {
            float w = exp(-float(x * x + y * y) / 8.0);
            sum += textureLod(tex, clamp(uv + vec2(x, y) * step, lo, hi), lod).rgb * w;
            weights += w;
        }
    }
    vec3 color = mix(sum / weights, tint.rgb, tint.a);
    outColor = vec4(color, 1.0);
}
";

    private readonly ICoreClientAPI _api;
    private IShaderProgram? _program;
    private MeshRef? _quad;
    private int _texture;
    private int _textureWidth;
    private int _textureHeight;
    private bool _captured;
    private bool _failed;
    private int _captureX;
    private int _captureY;
    private int _captureWidth;
    private int _captureHeight;

    public PreviewBackdrop(ICoreClientAPI api)
    {
        _api = api;
        _api.Event.ReloadShader += LoadShader;
        LoadShader();
        _quad = _api.Render.UploadMesh(QuadMeshUtil.GetQuad());
    }

    /// <summary>True when the see-through preview is enabled and the shader compiled.</summary>
    public bool Active => UxOverhaulModSystem.Config.PreviewBackground != PreviewBackgroundMode.Panel && _program != null && !_failed;

    private bool LoadShader()
    {
        IShaderProgram program = _api.Shader.NewShaderProgram();
        program.VertexShader = _api.Shader.NewShader(EnumShaderType.VertexShader);
        program.VertexShader.Code = VertexCode;
        program.FragmentShader = _api.Shader.NewShader(EnumShaderType.FragmentShader);
        program.FragmentShader.Code = FragmentCode;
        _api.Shader.RegisterMemoryShaderProgram(ShaderName, program);

        if (!program.Compile())
        {
            _api.Logger.Warning("[playermodellib-ux-overhaul] Preview blur shader failed to compile, using the plain preview panel.");
            _program = null;
            return false;
        }
        _program = program;
        return true;
    }

    /// <summary>Copy the screen behind the preview. Call before the dialog renders.</summary>
    public void Capture(ElementBounds preview)
    {
        _captured = false;
        if (!Active) return;
        try
        {
            CaptureInternal(preview);
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    /// <summary>
    /// Draw the blurred copy over the preview, with a thin outline. Call after the dialog background has
    /// rendered. If the capture failed the preview gets a plain dark fill instead.
    /// </summary>
    public void Draw(ElementBounds preview)
    {
        UxOverhaulConfig config = UxOverhaulModSystem.Config;
        // Always on top of the dialog background, and leaves the depth buffer alone for the character.
        bool depthTest = GL.IsEnabled(EnableCap.DepthTest);
        if (depthTest) GL.Disable(EnableCap.DepthTest);
        try
        {
            if (_captured && _program != null && _quad != null)
            {
                try
                {
                    float lod = config.PreviewBackground == PreviewBackgroundMode.Clear ? 0f : config.BlurStrength * MaxLod;
                    DrawBlur(lod, config.PreviewDarken);
                }
                catch (Exception exception)
                {
                    Fail(exception);
                }
            }
            if (!_captured)
            {
                _api.Render.RenderRectangle((float)preview.renderX, (float)preview.renderY, QuadZ, (float)preview.OuterWidth, (float)preview.OuterHeight, ColorUtil.ToRgba(235, 30, 30, 30));
            }
            DrawOutline(preview);
        }
        finally
        {
            if (depthTest) GL.Enable(EnableCap.DepthTest);
        }
    }

    /// <summary>A broken backdrop should never take the game down; fall back to a plain background instead.</summary>
    private void Fail(Exception exception)
    {
        _failed = true;
        _captured = false;
        _api.Logger.Error("[playermodellib-ux-overhaul] Preview blur disabled after an error, the preview will have a plain background until restart: {0}", exception);
    }

    private void CaptureInternal(ElementBounds area)
    {
        int frameWidth = _api.Render.FrameWidth;
        int frameHeight = _api.Render.FrameHeight;
        int left = Math.Max(0, (int)area.renderX);
        int top = Math.Max(0, (int)area.renderY);
        int right = Math.Min(frameWidth, (int)(area.renderX + area.OuterWidth));
        int bottom = Math.Min(frameHeight, (int)(area.renderY + area.OuterHeight));
        if (right - left < 2 || bottom - top < 2) return;

        _captureX = left;
        _captureWidth = right - left;
        _captureHeight = bottom - top;
        _captureY = frameHeight - bottom; // GL counts rows from the bottom.

        EnsureTexture(_captureWidth, _captureHeight);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _texture);
        GL.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, _captureX, _captureY, _captureWidth, _captureHeight);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        _captured = true;
    }

    /// <summary>Draw the captured copy back over itself, blurred at <paramref name="lod"/> and darkened by <paramref name="darken"/>.</summary>
    private void DrawBlur(float lod, float darken)
    {
        if (_program == null || _quad == null) return;

        float x = _captureX;
        float y = _api.Render.FrameHeight - _captureY - _captureHeight;
        float width = _captureWidth;
        float height = _captureHeight;

        float[] modelView = (float[])_api.Render.CurrentModelviewMatrix.Clone();
        Mat4f.Translate(modelView, modelView, x + width / 2f, y + height / 2f, QuadZ);
        Mat4f.Scale(modelView, modelView, width / 2f, height / 2f, 1f);

        // The engine refuses to switch shaders while one is active, so park the GUI shader and restore it after.
        IShaderProgram? previous = _api.Render.CurrentActiveShader;
        previous?.Stop();
        bool cullFace = GL.IsEnabled(EnableCap.CullFace);
        if (cullFace) GL.Disable(EnableCap.CullFace);
        try
        {
            _program.Use();
            _program.BindTexture2D("tex", _texture, 0);
            _program.Uniform("rectOrigin", new Vec2f(_captureX, _captureY));
            _program.Uniform("rectSize", new Vec2f(_captureWidth, _captureHeight));
            _program.Uniform("lod", lod);
            _program.Uniform("tint", new Vec4f(0f, 0f, 0f, darken));
            _program.UniformMatrix("projectionMatrix", _api.Render.CurrentProjectionMatrix);
            _program.UniformMatrix("modelViewMatrix", modelView);
            _api.Render.RenderMesh(_quad);
        }
        finally
        {
            _program.Stop();
            if (cullFace) GL.Enable(EnableCap.CullFace);
            previous?.Use();
        }
    }

    /// <summary>Thin dark outline so the preview's edge stays readable against bright scenes.</summary>
    private void DrawOutline(ElementBounds bounds)
    {
        float x = (float)bounds.renderX;
        float y = (float)bounds.renderY;
        float width = (float)bounds.OuterWidth;
        float height = (float)bounds.OuterHeight;
        int border = ColorUtil.ToRgba(150, 0, 0, 0);
        _api.Render.RenderRectangle(x, y, QuadZ + 1, width, 1, border);
        _api.Render.RenderRectangle(x, y + height - 1, QuadZ + 1, width, 1, border);
        _api.Render.RenderRectangle(x, y, QuadZ + 1, 1, height, border);
        _api.Render.RenderRectangle(x + width - 1, y, QuadZ + 1, 1, height, border);
    }

    private void EnsureTexture(int width, int height)
    {
        if (_texture != 0 && width == _textureWidth && height == _textureHeight) return;

        if (_texture == 0) _texture = GL.GenTexture();
        _textureWidth = width;
        _textureHeight = height;

        GL.BindTexture(TextureTarget.Texture2D, _texture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    public void Dispose()
    {
        _api.Event.ReloadShader -= LoadShader;
        if (_texture != 0)
        {
            GL.DeleteTexture(_texture);
            _texture = 0;
        }
        _quad?.Dispose();
        _quad = null;
    }
}
