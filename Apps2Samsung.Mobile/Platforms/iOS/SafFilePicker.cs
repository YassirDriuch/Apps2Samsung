namespace Apps2Samsung.Mobile.Services;

/// <summary>
/// iOS counterpart of the Android Storage Access Framework picker, under the same name and
/// signature so the pages call one API. The Android class exists to dodge a main-thread network
/// crash in MAUI's picker; iOS has no such problem, so this wraps <see cref="FilePicker"/> (the
/// system document picker) and copies the pick into app cache like the Android version does.
/// </summary>
public static class SafFilePicker
{
    /// <summary>
    /// Shows the document picker and returns the chosen file, copied into app cache, or
    /// <c>null</c> if the user cancelled.
    /// </summary>
    /// <param name="mimeTypes">MIME filter, e.g. <c>image/*</c>. Empty means every file.</param>
    public static async Task<PickedFile?> PickAsync(params string[] mimeTypes)
    {
        var options = new PickOptions
        {
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.iOS] = ToUniformTypes(mimeTypes),
            }),
        };

        var result = await FilePicker.Default.PickAsync(options);
        if (result is null)
            return null;

        return await Task.Run(async () =>
        {
            var name = Sanitize(result.FileName);

            // One file per pick: clearing first keeps a re-pick from reusing a stale copy.
            var dir = Path.Combine(FileSystem.CacheDirectory, "picked");
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            Directory.CreateDirectory(dir);

            var dest = Path.Combine(dir, name);
            await using (var src = await result.OpenReadAsync())
            await using (var dst = File.Create(dest))
                await src.CopyToAsync(dst);

            return new PickedFile(name, dest);
        });
    }

    // The document picker filters by Uniform Type Identifier, not MIME type. A .wgt/.tpk has no
    // system type, so "every file" has to be public.item.
    private static string[] ToUniformTypes(string[] mimeTypes)
    {
        if (mimeTypes.Length == 0)
            return ["public.item"];

        var types = new List<string>();
        foreach (var mime in mimeTypes)
        {
            switch (mime)
            {
                case "image/*":
                    types.Add("public.image");
                    break;
                case "application/zip":
                    types.Add("public.zip-archive");
                    break;
                default:
                    types.Add("public.data");
                    break;
            }
        }
        return types.Distinct().ToArray();
    }

    // The name comes from another app, so it decides neither the directory nor the extension.
    private static string Sanitize(string? name)
    {
        name = Path.GetFileName(name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
            return "picked-file";

        var cleaned = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        return cleaned.Length > 128 ? cleaned[^128..] : cleaned;
    }
}
