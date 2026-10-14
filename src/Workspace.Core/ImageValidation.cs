using SkiaSharp;

namespace Workspace.Core;

public static class ImageValidation
{
    public const int MaxImages = 5;
    public const int MaxImageBytes = 10 * 1024 * 1024;
    public const int MaxTotalBytes = 25 * 1024 * 1024;
    public const long MaxPixels = 20_000_000;

    public static UploadImage[] Normalize(IReadOnlyList<UploadImage> images)
    {
        if (images.Count > MaxImages) throw new WorkspaceException("too_many_images", "Attach no more than five images.");
        if (images.Sum(i => (long)i.Bytes.Length) > MaxTotalBytes)
            throw new WorkspaceException("images_too_large", "The combined upload must be at most 25 MiB.");
        var result = new List<UploadImage>();
        foreach (var image in images)
        {
            var extension = Path.GetExtension(image.Name).ToLowerInvariant();
            if (image.Bytes.Length == 0 || image.Bytes.Length > MaxImageBytes)
                throw new WorkspaceException("image_too_large", "Each image must contain data and be at most 10 MiB.");
            bool png = image.Bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            bool jpeg = image.Bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 });
            if (!(png && extension == ".png" && image.MimeType == "image/png"
                || jpeg && extension is ".jpg" or ".jpeg" && image.MimeType == "image/jpeg"))
                throw new WorkspaceException("image_format_invalid", "Use a valid JPEG or PNG with a matching file extension.");
            using var data = SKData.CreateCopy(image.Bytes);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0
                || (long)codec.Info.Width * codec.Info.Height > MaxPixels)
                throw new WorkspaceException("image_dimensions_invalid", "The image is unreadable or exceeds 20 million pixels.");
            using var bitmap = new SKBitmap(codec.Info);
            if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
                throw new WorkspaceException("image_unreadable", "The image is damaged or incomplete.");
            using var normalized = SKImage.FromBitmap(bitmap);
            using var encoded = normalized.Encode(png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, 95);
            var bytes = encoded.ToArray();
            if (bytes.Length > MaxImageBytes) throw new WorkspaceException("normalized_image_too_large", "The metadata-free image exceeds 10 MiB.");
            result.Add(new($"image-{result.Count + 1}{(png ? ".png" : ".jpg")}", png ? "image/png" : "image/jpeg", bytes));
        }
        if (result.Sum(i => (long)i.Bytes.Length) > MaxTotalBytes)
            throw new WorkspaceException("normalized_images_too_large", "The normalized images exceed 25 MiB.");
        return result.ToArray();
    }
}
