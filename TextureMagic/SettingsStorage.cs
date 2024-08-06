using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Media;
using ImageMagick;

namespace TextureMagic;

public class SettingsStorage
{
    
    // Generate magick colour
    [JsonIgnore]
    public MagickColor BackgroundColorMagick => new(BackgroundColor.R, BackgroundColor.G, BackgroundColor.B);
    
    
    public Color BackgroundColor { get; set; }

    public static async Task Save(SettingsStorage settingsStorage)
    {
        await File.WriteAllTextAsync("settings.json", JsonSerializer.Serialize(settingsStorage));
    }

    public static async Task<SettingsStorage> Load()
    {
        if (!File.Exists("settings.json"))
        {
            var settings = new SettingsStorage();

            await Save(settings);
            
            return settings;
        }

        var data = await File.ReadAllTextAsync("settings.json");

        return JsonSerializer.Deserialize<SettingsStorage>(data) ?? new SettingsStorage();
    }
}