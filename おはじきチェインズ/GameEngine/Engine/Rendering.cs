using System.Drawing.Text;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Ohajiki.Engine;

[StructLayout(LayoutKind.Sequential)]
public struct DrawCommand
{
    public float X, Y, Width, Height, Rotation, R, G, B, A;
    public int Texture, Shape;
    public float Shear;
    public static DrawCommand Rect(float x, float y, float width, float height, Color color, float alpha = 1, int shape = 0) => new() { X = x, Y = y, Width = width, Height = height, R = color.R / 255f, G = color.G / 255f, B = color.B / 255f, A = color.A / 255f * alpha, Shape = shape };
}

public sealed class D3D12Renderer : IDisposable
{
    const string Library = "OhajikiD3D12.dll";
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr GE_Create(IntPtr hwnd, int w, int h, int debug);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void GE_Destroy(IntPtr ptr);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr GE_LastError();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr GE_Adapter(IntPtr ptr);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int GE_Resize(IntPtr ptr, int w, int h);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int GE_Render(IntPtr ptr, [In] DrawCommand[] commands, int count, float w, float h);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int GE_Texture(IntPtr ptr, int id, [In] byte[] pixels, int w, int h);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int GE_ReleaseTexture(IntPtr ptr, int id);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int GE_Capture(IntPtr ptr, [Out] byte[] pixels, int count);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int GE_DebugErrorCount(IntPtr ptr);
    IntPtr handle;
    public string Adapter { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    readonly Dictionary<string, (int Id, long Stamp)> textureCache = new();
    public D3D12Renderer(IntPtr hwnd, int w, int h, bool debug = false)
    {
        handle = GE_Create(hwnd, Math.Max(1, w), Math.Max(1, h), debug ? 1 : 0);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("DirectX 12の初期化に失敗：" + Marshal.PtrToStringUTF8(GE_LastError()));
        Width = Math.Max(1, w); Height = Math.Max(1, h); Adapter = Marshal.PtrToStringUTF8(GE_Adapter(handle)) ?? "";
    }
    static int Check(int code) => code < 0 ? throw new InvalidOperationException("DirectX 12：" + Marshal.PtrToStringUTF8(GE_LastError())) : code;
    public void Resize(int w, int h) { if (w <= 0 || h <= 0 || (w == Width && h == Height)) return; Check(GE_Resize(handle, w, h)); Width = w; Height = h; }
    public void Render(List<DrawCommand> commands, float w, float h) => Check(GE_Render(handle, commands.ToArray(), commands.Count, w, h));
    public int Upload(TexturePixels pixels, int id = -1) => Check(GE_Texture(handle, id, pixels.Rgba, pixels.Width, pixels.Height));
    public int Texture(EngineProject project, string asset, int displayEdge = 0)
    {
        if (asset.Length == 0) return 0; var path = project.Resolve(asset);
        int edge=0;if(displayEdge>0){edge=64;while(edge<displayEdge&&edge<8192)edge*=2;}
        string key=path+(edge>0?":size:"+edge:"");
        var stamp = File.GetLastWriteTimeUtc(path).Ticks;
        if (textureCache.TryGetValue(key, out var found) && found.Stamp == stamp) return found.Id;
        var pixels = AssetIO.ReadTexture(path);
        if(edge>0&&Math.Max(pixels.Width,pixels.Height)>edge)
        {
            float scale=edge/(float)Math.Max(pixels.Width,pixels.Height);using var source=pixels.ToBitmap();using var reduced=new Bitmap(Math.Max(1,(int)(pixels.Width*scale)),Math.Max(1,(int)(pixels.Height*scale)));
            using(var g=Graphics.FromImage(reduced)){g.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;using var attributes=new System.Drawing.Imaging.ImageAttributes();attributes.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);g.DrawImage(source,new Rectangle(0,0,reduced.Width,reduced.Height),0,0,source.Width,source.Height,GraphicsUnit.Pixel,attributes);}
            pixels=TexturePixels.FromBitmap(reduced);
        }
        var id = Upload(pixels, textureCache.ContainsKey(key) ? found.Id : -1); textureCache[key] = (id, stamp); return id;
    }
    public int Text(string content, int width, int height, float fontSize, bool bold)
    {
        width = Math.Clamp(width, 1, 2048); height = Math.Clamp(height, 1, 2048); fontSize = Math.Clamp(fontSize, 4, 500);
        var key = $"text:{width}:{height}:{fontSize}:{bold}:{content}";
        if (textureCache.TryGetValue(key, out var found)) return found.Id;
        using var bitmap = new Bitmap(width, height); using var graphics = Graphics.FromImage(bitmap); graphics.Clear(Color.Transparent); graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Yu Gothic UI", fontSize, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        graphics.DrawString(content, font, Brushes.White, new RectangleF(0, 0, width, height), format);
        var id = Upload(TexturePixels.FromBitmap(bitmap)); textureCache[key] = (id, 0); return id;
    }
    public void ClearTextures() { foreach (var id in textureCache.Values.Select(v => v.Id).Distinct()) Check(GE_ReleaseTexture(handle, id)); textureCache.Clear(); }
    public void BeginFrame() { if (textureCache.Count > 1200) ClearTextures(); }
    public int DebugErrorCount => GE_DebugErrorCount(handle);
    public TexturePixels Capture() { var data = new byte[checked(Width * Height * 4)]; Check(GE_Capture(handle, data, data.Length)); return new(Width, Height, data); }
    public void Dispose() { if (handle != IntPtr.Zero) { GE_Destroy(handle); handle = IntPtr.Zero; } GC.SuppressFinalize(this); }
}

public static class SceneRenderer
{
    public static Color ParseColor(string value, Color? fallback = null) { try { return ColorTranslator.FromHtml(value); } catch { return fallback ?? Color.Magenta; } }
    public static DrawCommand ObjectDraw(SceneData scene, GameObject obj, Color color, float opacity = 1)
    {
        var m = scene.Matrix(obj); var sx = new Vector2(m.M11, m.M12).Length(); var sy = sx > .00001f ? m.GetDeterminant() / sx : 0;
        var command = DrawCommand.Rect(m.M31, m.M32, obj.Transform.Width * sx, obj.Transform.Height * sy, color, opacity);
        command.Rotation = MathF.Atan2(m.M12, m.M11) * 180 / MathF.PI;
        command.Shear = Math.Abs(sy) > .00001f && sx > .00001f ? (m.M11 * m.M21 + m.M12 * m.M22) / (sx * sy) : 0; return command;
    }
    public static bool Hit(SceneData scene, GameObject obj, Vector2 point)
    {
        if (!scene.IsActive(obj) || !Matrix3x2.Invert(scene.Matrix(obj), out var inverse)) return false;
        var p = Vector2.Transform(point, inverse); return Math.Abs(p.X) <= Math.Abs(obj.Transform.Width) / 2 && Math.Abs(p.Y) <= Math.Abs(obj.Transform.Height) / 2;
    }
    public static GameObject? Pick(SceneData scene, Vector2 point) => scene.Objects.OrderByDescending(o => o.Order).ThenByDescending(o => scene.Objects.IndexOf(o)).FirstOrDefault(o => Hit(scene, o, point) && (o.GetComponent("Sprite") != null || o.GetComponent("Text") != null));
    public static List<DrawCommand> Build(SceneData scene, EngineProject project, D3D12Renderer renderer, GameObject? selected, bool grid, RuntimeWorld? world, Action<string>? log = null)
    {
        renderer.BeginFrame();
        var commands = new List<DrawCommand> { DrawCommand.Rect(scene.Width / 2f, scene.Height / 2f, scene.Width, scene.Height, ParseColor(scene.Background)) };
        if (grid)
        {
            for (int x = 0; x <= scene.Width; x += 50) commands.Add(DrawCommand.Rect(x, scene.Height / 2f, 1, scene.Height, Color.FromArgb(34, 180, 210, 220)));
            for (int y = 0; y <= scene.Height; y += 50) commands.Add(DrawCommand.Rect(scene.Width / 2f, y, scene.Width, 1, Color.FromArgb(34, 180, 210, 220)));
        }
        foreach (var obj in scene.Objects.OrderBy(o => o.Order))
        {
            if (!scene.IsActive(obj)) continue;
            foreach (var c in obj.Components.Where(c => c.Enabled && (c.Type == "Sprite" || c.Type == "Text")))
            {
                var d = ObjectDraw(scene, obj, ParseColor(c.Get("Color", "#FFFFFF")), Math.Clamp(c.Number("Opacity", 1), 0, 1));
                try
                {
                    if (c.Type == "Sprite") { d.Texture = renderer.Texture(project, c.Get("Texture"),(int)Math.Ceiling(Math.Max(Math.Abs(d.Width),Math.Abs(d.Height))*Math.Min(renderer.Width/(float)scene.Width,renderer.Height/(float)scene.Height))); d.Shape = c.Get("Shape") switch { "円" => 1, "リング" => 2, _ => 0 }; }
                    else d.Texture = renderer.Text(c.Get("Text"), (int)Math.Abs(obj.Transform.Width), (int)Math.Abs(obj.Transform.Height), c.Number("FontSize", 32), c.Flag("Bold"));
                }
                catch (Exception ex) { d.R = 1; d.G = 0; d.B = 1; log?.Invoke($"{obj.Name}：{ex.Message}"); }
                commands.Add(d);
            }
        }
        if (world != null)
        {
            foreach (var p in world.Particles)
            {
                float t = p.Age / p.Lifetime; var d = DrawCommand.Rect(p.Position.X, p.Position.Y, p.Size + (p.EndSize - p.Size) * t, p.Size + (p.EndSize - p.Size) * t, p.Color, 1 - t, 1);
                if (p.Texture.Length > 0) { try { d.Texture = renderer.Texture(project, p.Texture); d.Shape = 0; } catch { } } commands.Add(d);
            }
            if (world.Aim is { } aim) AddLine(commands, aim.Start, aim.End, Color.FromArgb(210, 255, 219, 124), 4);
            if (world.Fade > 0) commands.Add(DrawCommand.Rect(scene.Width / 2f, scene.Height / 2f, scene.Width, scene.Height, Color.Black, world.Fade));
        }
        if (selected != null && scene.Objects.Contains(selected))
        {
            var m = scene.Matrix(selected); float hw = selected.Transform.Width / 2, hh = selected.Transform.Height / 2;
            Vector2[] points = [new(-hw, -hh), new(hw, -hh), new(hw, hh), new(-hw, hh)]; points = points.Select(p => Vector2.Transform(p, m)).ToArray();
            for (int i = 0; i < 4; i++) { AddLine(commands, points[i], points[(i + 1) % 4], Color.FromArgb(255, 207, 109), 2); commands.Add(DrawCommand.Rect(points[i].X, points[i].Y, 8, 8, Color.FromArgb(255, 207, 109))); }
            var center = scene.Position(selected); AddLine(commands, center, center + new Vector2(45, 0), Color.Salmon, 3); AddLine(commands, center, center + new Vector2(0, -45), Color.LightGreen, 3);
        }
        return commands;
    }
    public static void AddLine(List<DrawCommand> list, Vector2 from, Vector2 to, Color color, float width)
    {
        var delta = to - from; var d = DrawCommand.Rect((from.X + to.X) / 2, (from.Y + to.Y) / 2, delta.Length(), width, color); d.Rotation = MathF.Atan2(delta.Y, delta.X) * 180 / MathF.PI; list.Add(d);
    }
}
