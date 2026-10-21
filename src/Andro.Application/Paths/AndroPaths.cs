namespace Andro.Application.Paths;

/// <summary>Everything Andro writes lives under <see cref="Root"/>; the MSI never touches it.</summary>
public sealed record AndroPaths(string Root)
{
    public string Sdk => Path.Combine(Root, "sdk");
    public string AvdHome => Path.Combine(Root, "avd");

    /// <summary>ANDROID_USER_HOME and ANDROID_EMULATOR_HOME: keeps the emulator out of %USERPROFILE%\.android.</summary>
    public string UserHome => Path.Combine(Root, "user");

    public string Downloads => Path.Combine(Root, "downloads");
    public string Logs => Path.Combine(Root, "logs");
    public string SettingsFile => Path.Combine(Root, "settings.json");

    public static AndroPaths Default() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Deve1ne", "Andro"));
}
