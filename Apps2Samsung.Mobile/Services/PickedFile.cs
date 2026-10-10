namespace Apps2Samsung.Mobile.Services;

/// <summary>
/// A file the user picked, already copied into app cache so the platform handle (Android content
/// URI, iOS security-scoped URL) can be released. Returned by each platform's <c>SafFilePicker</c>.
/// </summary>
public sealed record PickedFile(string FileName, string LocalPath);
