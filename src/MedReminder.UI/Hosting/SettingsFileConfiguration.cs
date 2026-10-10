using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Serilog;

namespace MedReminder.UI.Hosting;

// The settings files under %LOCALAPPDATA%\MedReminder\ join the
// configuration through here. A file that cannot be loaded (malformed
// JSON, a root that is not an object, a lock or a permission error) is
// skipped and its section keeps its defaults, so one damaged file never
// stops the app; the next save rewrites it and the reload picks it up.
internal static class SettingsFileConfiguration
{
    public static IConfigurationBuilder AddSettingsFile(this IConfigurationBuilder configuration, string path,
        bool reloadOnChange)
    {
        var source = new SettingsFileSource
        {
            Path = path,
            Optional = true,
            ReloadOnChange = reloadOnChange,
            OnLoadException = context =>
            {
                Skip(context.Provider.Source.Path, context.Exception.InnerException ?? context.Exception);
                context.Ignore = true;
            },
        };
        source.ResolveFileProvider();
        return configuration.Add(source);
    }

    private static void Skip(string? path, Exception error)
        // The file name only: the full path holds the Windows user name,
        // and the parser message can quote the file content.
        => Log.Warning("Settings file {File} could not be loaded ({Error}); its defaults apply until it is saved again.",
            Path.GetFileName(path), error.GetType().Name);

    private sealed class SettingsFileSource : JsonConfigurationSource
    {
        public override IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            EnsureDefaults(builder);
            return new SettingsFileProvider(this);
        }
    }

    // The provider opens the file outside the guard that calls
    // OnLoadException, so a lock or permission error on the first load
    // would still throw. A failed reload already keeps the previous values.
    private sealed class SettingsFileProvider(JsonConfigurationSource source) : JsonConfigurationProvider(source)
    {
        public override void Load()
        {
            try
            {
                base.Load();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Skip(Source.Path, ex);
            }
        }
    }
}
