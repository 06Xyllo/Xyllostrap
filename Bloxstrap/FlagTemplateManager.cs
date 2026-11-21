namespace Bloxstrap
{
    /// <summary>
    /// A saved set of FastFlags that can be applied in one click.
    /// </summary>
    public class FlagTemplate
    {
        public string Name { get; set; } = "";

        public string Description { get; set; } = "";

        /// <summary>
        /// Fluent icon name shown on the template tile, e.g. "Rocket24".
        /// </summary>
        public string Icon { get; set; } = "Flag24";

        [JsonIgnore]
        public Wpf.Ui.Common.SymbolRegular Symbol => Enum.TryParse(Icon, out Wpf.Ui.Common.SymbolRegular symbol) ? symbol : Wpf.Ui.Common.SymbolRegular.Flag24;

        public Dictionary<string, string> Flags { get; set; } = new();

        /// <summary>
        /// Optional Roblox settings (GlobalBasicSettings presets), e.g. "UI.Transparency": "3".
        /// </summary>
        public Dictionary<string, string>? RobloxSettings { get; set; }

        [JsonIgnore]
        public string FilePath { get; set; } = "";

        [JsonIgnore]
        public string Summary => $"{Flags.Count} flag{(Flags.Count == 1 ? "" : "s")}"
            + (RobloxSettings is { Count: > 0 } ? $" + {RobloxSettings.Count} Roblox setting{(RobloxSettings.Count == 1 ? "" : "s")}" : "")
            + (String.IsNullOrWhiteSpace(Description) ? "" : $" · {Description}");
    }

    /// <summary>
    /// Xyllostrap FastFlag templates. Each template is a JSON file in the FlagTemplates folder,
    /// so deleting a template is just deleting its file.
    /// </summary>
    public static class FlagTemplateManager
    {
        private const string LOG_IDENT = "FlagTemplateManager";

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static string Folder => Path.Combine(Paths.Base, "FlagTemplates");

        private static string SeededMarker => Path.Combine(Folder, ".seeded");

        public static IReadOnlyList<FlagTemplate> DefaultTemplates => new List<FlagTemplate>
        {
            new()
            {
                Name = "Potato Graphics",
                Icon = "FoodPizza24",
                Description = "Lowest possible graphics for maximum FPS on weak PCs",
                Flags = new()
                {
                    ["FFlagDebugSkyGray"] = "True",
                    ["DFIntDebugFRMQualityLevelOverride"] = "1",
                    ["FIntDebugForceMSAASamples"] = "0",
                    ["DFFlagTextureQualityOverrideEnabled"] = "True",
                    ["DFIntTextureQualityOverride"] = "0",
                    ["FIntDebugTextureManagerSkipMips"] = "8",
                    ["FFlagDisablePostFx"] = "True",
                    ["FIntRenderShadowIntensity"] = "0",
                    ["FIntFRMMinGrassDistance"] = "0",
                    ["FIntFRMMaxGrassDistance"] = "0",
                    ["FIntRenderGrassDetailStrands"] = "0",
                    ["DFIntCSGLevelOfDetailSwitchingDistance"] = "0",
                    ["DFIntCSGLevelOfDetailSwitchingDistanceL12"] = "0",
                    ["DFIntCSGLevelOfDetailSwitchingDistanceL23"] = "0",
                    ["DFIntCSGLevelOfDetailSwitchingDistanceL34"] = "0"
                }
            },
            new()
            {
                Name = "Rivals",
                Icon = "Target24",
                Description = "Grey sky with UI transparency set to 3",
                Flags = new()
                {
                    ["FFlagDebugSkyGray"] = "True"
                },
                RobloxSettings = new()
                {
                    ["UI.Transparency"] = "3"
                }
            },
            new()
            {
                Name = "Competitive / No Clutter",
                Icon = "Trophy24",
                Description = "Removes shadows, grass and post effects but keeps textures sharp",
                Flags = new()
                {
                    ["FFlagDisablePostFx"] = "True",
                    ["FIntRenderShadowIntensity"] = "0",
                    ["FIntFRMMinGrassDistance"] = "0",
                    ["FIntFRMMaxGrassDistance"] = "0",
                    ["FIntRenderGrassDetailStrands"] = "0",
                    ["FIntDebugForceMSAASamples"] = "0"
                }
            },
            new()
            {
                Name = "Max Quality",
                Icon = "Sparkle24",
                Description = "Best looking settings for strong PCs",
                Flags = new()
                {
                    ["DFIntDebugFRMQualityLevelOverride"] = "21",
                    ["FIntDebugForceMSAASamples"] = "4",
                    ["DFFlagTextureQualityOverrideEnabled"] = "True",
                    ["DFIntTextureQualityOverride"] = "3"
                }
            }
        };

        /// <summary>
        /// Creates the default templates the first time. Deleted defaults stay deleted
        /// unless the user presses "Restore default templates".
        /// </summary>
        public static void EnsureDefaults()
        {
            try
            {
                Directory.CreateDirectory(Folder);

                if (File.Exists(SeededMarker))
                {
                    // upgrade from the first version of the templates
                    string oldGreySky = Path.Combine(Folder, "Transparent Grey Sky.json");
                    if (File.Exists(oldGreySky))
                    {
                        File.Delete(oldGreySky);
                        foreach (var template in DefaultTemplates.Where(x => x.Name is "Rivals" or "Potato Graphics"))
                            Save(template);
                    }

                    // give existing default templates their tile icons (only rewrites files that are still default templates)
                    foreach (var template in DefaultTemplates)
                    {
                        string file = Path.Combine(Folder, String.Concat(template.Name.Split(Path.GetInvalidFileNameChars())).Trim() + ".json");
                        if (File.Exists(file) && !File.ReadAllText(file).Contains("\"Icon\""))
                            Save(template);
                    }
                    return;
                }

                foreach (var template in DefaultTemplates)
                    Save(template);

                File.WriteAllText(SeededMarker, "");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        public static void RestoreDefaults()
        {
            Directory.CreateDirectory(Folder);

            foreach (var template in DefaultTemplates)
                Save(template);

            File.WriteAllText(SeededMarker, "");
        }

        public static List<FlagTemplate> GetAll()
        {
            var list = new List<FlagTemplate>();

            if (!Directory.Exists(Folder))
                return list;

            foreach (string file in Directory.GetFiles(Folder, "*.json").OrderBy(x => File.GetCreationTimeUtc(x)).ThenBy(x => x))
            {
                try
                {
                    var template = JsonSerializer.Deserialize<FlagTemplate>(File.ReadAllText(file));

                    if (template is null)
                        continue;

                    template.Flags ??= new();
                    template.FilePath = file;

                    if (String.IsNullOrWhiteSpace(template.Name))
                        template.Name = Path.GetFileNameWithoutExtension(file);

                    list.Add(template);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not read template '{file}'");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }

            return list;
        }

        public static string Save(FlagTemplate template)
        {
            Directory.CreateDirectory(Folder);

            string fileName = String.Concat(template.Name.Split(Path.GetInvalidFileNameChars())).Trim();

            if (String.IsNullOrEmpty(fileName))
                fileName = "Template";

            string path = Path.Combine(Folder, fileName + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(template, JsonOptions));
            template.FilePath = path;

            return path;
        }

        public static void Delete(FlagTemplate template)
        {
            if (File.Exists(template.FilePath))
                File.Delete(template.FilePath);
        }

        /// <summary>
        /// Adds the template's flags on top of the current flags (pending until the user presses Save).
        /// </summary>
        public static void Apply(FlagTemplate template)
        {
            foreach (var pair in template.Flags)
                App.FastFlags.SetValue(pair.Key, pair.Value);

            if (template.RobloxSettings is not null)
                foreach (var pair in template.RobloxSettings)
                    App.GlobalSettings.SetPreset(pair.Key, pair.Value);
        }

        /// <summary>
        /// Removes the template's flags from the current flags.
        /// </summary>
        public static void Remove(FlagTemplate template)
        {
            foreach (var pair in template.Flags)
                App.FastFlags.SetValue(pair.Key, null);

            // put the UI transparency back to Roblox's default
            if (template.RobloxSettings?.ContainsKey("UI.Transparency") == true)
                App.GlobalSettings.SetPreset("UI.Transparency", "0");
        }

        public static FlagTemplate FromCurrentFlags(string name)
        {
            return new FlagTemplate
            {
                Name = name,
                Description = "Saved from your current flags",
                Flags = App.FastFlags.Prop.ToDictionary(x => x.Key, x => x.Value?.ToString() ?? "")
            };
        }
    }
}
