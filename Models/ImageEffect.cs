namespace SatelliteEyesWin.Models;

public class ImageEffect
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    public static readonly ImageEffect[] BuiltIn = new[]
    {
        new ImageEffect { Id = "none", Name = "None" },
        new ImageEffect { Id = "darken", Name = "Darken" },
        new ImageEffect { Id = "desaturate", Name = "Desaturate" },
        new ImageEffect { Id = "darken-desaturate", Name = "Darken + Desaturate" },
        new ImageEffect { Id = "pixellate", Name = "Pixellate" },
        new ImageEffect { Id = "blur", Name = "Blur" },
    };
}
