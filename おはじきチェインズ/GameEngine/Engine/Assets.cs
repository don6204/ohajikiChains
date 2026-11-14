using System.Drawing.Imaging;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Ohajiki.Engine;

public sealed record TexturePixels(int Width, int Height, byte[] Rgba)
{
    public static TexturePixels FromBitmap(Bitmap image)
    {
        using var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
        // DrawImageUnscaled still applies the source image's physical DPI on GDI+.
        // Use pixel rectangles so 300-DPI artwork retains its full canvas.
        using (var graphics = Graphics.FromImage(bitmap)) { graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy; graphics.DrawImage(image,new Rectangle(0,0,image.Width,image.Height),0,0,image.Width,image.Height,GraphicsUnit.Pixel); }
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height); var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[checked(bitmap.Width * bitmap.Height * 4)];
            for (int y = 0; y < bitmap.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * bitmap.Width * 4, bitmap.Width * 4);
            for (int i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
            return new(bitmap.Width, bitmap.Height, pixels);
        }
        finally { bitmap.UnlockBits(data); }
    }
    public Bitmap ToBitmap()
    {
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb); var bgra = (byte[])Rgba.Clone();
        for (int i = 0; i < bgra.Length; i += 4) (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);
        var data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { for (int y = 0; y < Height; y++) Marshal.Copy(bgra, y * Width * 4, data.Scan0 + y * data.Stride, Width * 4); }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }
}

public static class AssetIO
{
    public static readonly string[] TextureExtensions = [".png", ".jpg", ".jpeg", ".psd"];
    public static bool IsTexture(string path) => TextureExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    public static TexturePixels ReadTexture(string path)
    {
        if (Path.GetExtension(path).Equals(".psd", StringComparison.OrdinalIgnoreCase)) return ReadPsd(File.ReadAllBytes(path));
        using var file = File.OpenRead(path); using var bitmap = new Bitmap(file);
        if (bitmap.Width > 8192 || bitmap.Height > 8192 || (long)bitmap.Width * bitmap.Height > 32_000_000) throw new InvalidDataException("画像サイズ上限：一辺8192、合計3200万画素。");
        return TexturePixels.FromBitmap(bitmap);
    }
    public static TexturePixels ReadPsd(byte[] file)
    {
        using var stream = new MemoryStream(file); using var reader = new BinaryReader(stream);
        ushort U16() { var b = reader.ReadBytes(2); if (b.Length != 2) throw new EndOfStreamException(); return (ushort)((b[0] << 8) | b[1]); }
        uint U32() => ((uint)U16() << 16) | U16();
        void SkipBlock() { var length = U32(); if (length > stream.Length - stream.Position) throw new InvalidDataException("PSDブロックが破損しています。"); stream.Position += length; }
        if (System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4)) != "8BPS" || U16() != 1) throw new InvalidDataException("PSD形式（PSB非対応）を指定してください。");
        reader.ReadBytes(6); var channels = U16(); var height = checked((int)U32()); var width = checked((int)U32()); var depth = U16(); var mode = U16();
        if (depth != 8 || (mode != 3 && mode != 1)) throw new InvalidDataException("PSDは8bit RGBまたはグレースケールに対応しています。Photoshopで変換して保存してください。");
        if (width < 1 || height < 1 || width > 8192 || height > 8192 || (long)width * height > 32_000_000 || channels < (mode == 3 ? 3 : 1) || channels > 16) throw new InvalidDataException("PSDのサイズ・チャンネル数が対応範囲外です。");
        SkipBlock(); SkipBlock(); SkipBlock(); var compression = U16(); var planeSize = checked(width * height); var planes = new byte[checked(planeSize * channels)];
        if (compression == 0) stream.ReadExactly(planes);
        else if (compression == 1)
        {
            var lengths = new ushort[checked(channels * height)]; for (int i = 0; i < lengths.Length; i++) lengths[i] = U16();
            for (int row = 0; row < lengths.Length; row++)
            {
                var packed = reader.ReadBytes(lengths[row]); if (packed.Length != lengths[row]) throw new EndOfStreamException();
                int src = 0, dest = row * width, end = dest + width;
                while (src < packed.Length)
                {
                    var n = (sbyte)packed[src++];
                    if (n >= 0) { var length = n + 1; if (src + length > packed.Length || dest + length > end) throw new InvalidDataException("PSDのRLEデータが破損しています。"); Array.Copy(packed, src, planes, dest, length); src += length; dest += length; }
                    else if (n != -128) { var length = 1 - n; if (src >= packed.Length || dest + length > end) throw new InvalidDataException("PSDのRLEデータが破損しています。"); Array.Fill(planes, packed[src++], dest, length); dest += length; }
                }
                if (dest != end) throw new InvalidDataException("PSDのRLE行サイズが一致しません。");
            }
        }
        else if (compression is 2 or 3)
        {
            using var zlib = new ZLibStream(stream, CompressionMode.Decompress); zlib.ReadExactly(planes);
            if (compression == 3) for (int row = 0; row < channels * height; row++) for (int x = 1; x < width; x++) { int i = row * width + x; planes[i] = unchecked((byte)(planes[i] + planes[i - 1])); }
        }
        else throw new InvalidDataException("未対応のPSD圧縮形式です。");
        var rgba = new byte[checked(planeSize * 4)]; var baseChannels = mode == 3 ? 3 : 1;
        for (int i = 0; i < planeSize; i++) { rgba[i * 4] = planes[i]; rgba[i * 4 + 1] = planes[(mode == 3 ? planeSize : 0) + i]; rgba[i * 4 + 2] = planes[(mode == 3 ? 2 * planeSize : 0) + i]; rgba[i * 4 + 3] = channels > baseChannels ? planes[baseChannels * planeSize + i] : (byte)255; }
        return new(width, height, rgba);
    }
    public static string Import(EngineProject project, string source, string targetFolder)
    {
        var folder = project.Resolve(targetFolder); Directory.CreateDirectory(folder);
        if (!folder.Equals(project.Assets, StringComparison.OrdinalIgnoreCase) && !folder.StartsWith(project.Assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Assets内を指定してください。");
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("リンクやジャンクションは取り込めません。");
        if (Directory.Exists(source))
        {
            source = Path.GetFullPath(source);
            if (folder.Equals(source, StringComparison.OrdinalIgnoreCase) || folder.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("取り込み先を含むフォルダは取り込めません。");
            var destination = Unique(Path.Combine(folder, Path.GetFileName(source)), true); Directory.CreateDirectory(destination);
            foreach (var item in Directory.EnumerateFileSystemEntries(source)) Import(project, item, project.Relative(destination)); return project.Relative(destination);
        }
        if (Path.GetFullPath(source).Equals(Path.Combine(folder, Path.GetFileName(source)), StringComparison.OrdinalIgnoreCase)) return project.Relative(source);
        if (IsTexture(source)) ReadTexture(source);
        var path = Unique(Path.Combine(folder, Path.GetFileName(source)), false); File.Copy(source, path); return project.Relative(path);
    }
    public static string Unique(string path, bool directory = false)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var folder = Path.GetDirectoryName(path)!; var ext = directory ? "" : Path.GetExtension(path); var name = directory ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        for (int i = 2; ; i++) { var next = Path.Combine(folder, name + "_" + i + ext); if (!File.Exists(next) && !Directory.Exists(next)) return next; }
    }
}
