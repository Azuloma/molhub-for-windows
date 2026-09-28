using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace MolHub.Windows;

/// <summary>
/// Turns a picked image into an icon the server accepts: the image is decoded (EXIF orientation respected), scaled so
/// its short side is at most 128 pixels (never enlarged), cropped to the centered square and encoded as PNG. When the PNG
/// is over 32,768 bytes a smaller size is tried. Returns null when the file cannot be used; nothing is sent from here.
/// </summary>
internal static class AvatarConverter
{
    /// <summary>Files larger than this are refused before decoding.</summary>
    public const ulong MaxFileBytes = 20UL * 1024 * 1024;

    /// <summary>Upper bound for the scaled image before cropping (very long panoramas are refused).</summary>
    private const ulong MaxScaledPixels = 16_000_000;

    /// <summary>Square sizes tried after the largest one, until the PNG fits the byte limit.</summary>
    private static readonly uint[] SmallerSides = [112, 96, 80, 64, 48, 32, 24, 16];

    public static readonly string[] FileTypes = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"];

    /// <summary>The system file picker, owned by the given window.</summary>
    public static async Task<StorageFile?> PickAsync(IntPtr hwnd)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.Thumbnail,
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        foreach (var type in FileTypes) picker.FileTypeFilter.Add(type);
        InitializeWithWindow.Initialize(picker, hwnd);
        return await picker.PickSingleFileAsync();
    }

    public static async Task<byte[]?> ConvertAsync(StorageFile file)
    {
        try
        {
            var properties = await file.GetBasicPropertiesAsync();
            if (properties.Size == 0 || properties.Size > MaxFileBytes) return null;
            using var input = await file.OpenReadAsync();
            if (input.Size == 0 || input.Size > MaxFileBytes) return null;

            var decoder = await BitmapDecoder.CreateAsync(input);
            if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0) return null;
            var largest = Math.Min(Math.Min(decoder.PixelWidth, decoder.PixelHeight), (uint)ProfileModel.MaxAvatarSide);
            foreach (var side in SmallerSides.Where(size => size < largest).Prepend(largest))
            {
                var png = await EncodeAsync(decoder, side);
                if (png is null) return null;
                if (ProfileModel.IsAcceptableAvatar(png)) return png;
            }
            return null;
        }
        catch
        {
            // Unsupported format, missing codec, unreadable file: the caller explains that the image can't be used.
            return null;
        }
    }

    private static async Task<byte[]?> EncodeAsync(BitmapDecoder decoder, uint side)
    {
        // Scaling happens before the EXIF rotation, so it uses the stored (unrotated) size; the scale is uniform.
        var shortSide = (double)Math.Min(decoder.PixelWidth, decoder.PixelHeight);
        var width0 = Math.Round(decoder.PixelWidth * (double)side / shortSide);
        var height0 = Math.Round(decoder.PixelHeight * (double)side / shortSide);
        if (width0 * height0 > MaxScaledPixels) return null;
        var scaledWidth = Math.Max(side, (uint)width0);
        var scaledHeight = Math.Max(side, (uint)height0);

        var transform = new BitmapTransform
        {
            ScaledWidth = scaledWidth,
            ScaledHeight = scaledHeight,
            InterpolationMode = BitmapInterpolationMode.Fant
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
        var size = (int)Math.Min(side, (uint)Math.Min(width, height));
        if (size < 1) return null;

        var pixels = new byte[width * height * 4];
        bitmap.CopyToBuffer(pixels.AsBuffer());
        var square = new byte[size * size * 4];
        int left = (width - size) / 2, top = (height - size) / 2;
        for (var row = 0; row < size; row++)
        {
            pixels.AsSpan(((top + row) * width + left) * 4, size * 4).CopyTo(square.AsSpan(row * size * 4));
        }

        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)size, (uint)size, 96, 96, square);
        await encoder.FlushAsync();
        // A 128 x 128 PNG is far below this bound; anything larger is not an icon the server takes.
        if (output.Size == 0 || output.Size > 4UL * ProfileModel.MaxAvatarBytes) return [];
        var length = (uint)output.Size;
        output.Seek(0);
        var buffer = await output.ReadAsync(new global::Windows.Storage.Streams.Buffer(length), length, InputStreamOptions.None);
        return buffer.ToArray();
    }
}
