using System.Collections.ObjectModel;
using HVO.SkyMonitorV5.RPi.Cameras.MockCamera;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Data;

public sealed class ConstellationCatalog : IConstellationCatalog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<ConstellationStar>> Constellations;

    static ConstellationCatalog()
    {
        var dictionary = new Dictionary<string, IReadOnlyList<ConstellationStar>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ursa Major (Big Dipper)"] = new[]
            {
                Create("Dubhe", 11.0621, 61.7511, 1.79, "K0III"),
                Create("Merak", 11.0307, 56.3824, 2.37, "A1V"),
                Create("Phecda", 11.8972, 53.6948, 2.43, "A0V"),
                Create("Megrez", 12.2571, 57.0326, 3.31, "A3V"),
                Create("Alioth", 12.9004, 55.9598, 1.76, "A0p"),
                Create("Mizar", 13.3987, 54.9254, 2.23, "A2V"),
                Create("Alkaid", 13.7923, 49.3133, 1.86, "B3V"),
            },
            ["Ursa Minor (Little Dipper)"] = new[]
            {
                Create("Polaris", 2.5303, 89.2641, 1.98, "F7Ib"),
                Create("Kochab", 14.8451, 74.1555, 2.07, "K4III"),
                Create("Pherkad", 15.3459, 71.8339, 3.00, "A2Iab"),
                Create("Yildun", 17.5369, 86.5865, 4.35, "A1V"),
                Create("Epsilon UMi", 16.7661, 82.0373, 4.21, "G5III"),
                Create("Zeta UMi", 15.7343, 77.7959, 4.29, "A3V"),
                Create("Eta UMi", 16.2918, 75.7553, 4.95, "A3V"),
            },
            ["Orion"] = new[]
            {
                Create("Betelgeuse", 5.9195, 7.4071, 0.45, "M2Iab"),
                Create("Bellatrix", 5.4189, 6.3497, 1.64, "B2III"),
                Create("Alnilam", 5.6036, -1.2019, 1.69, "B0Ia"),
                Create("Alnitak", 5.6793, -1.9426, 1.77, "O9.5Ib"),
                Create("Mintaka", 5.5333, 0.2991, 2.23, "O9.5II"),
                Create("Rigel", 5.2423, -8.2016, 0.13, "B8Ia"),
                Create("Saiph", 5.7959, -9.6696, 2.07, "B0.5Iab"),
                Create("Meissa", 5.9195, 9.9342, 3.54, "O8III"),
            },
            ["Cassiopeia"] = new[]
            {
                Create("Schedar", 0.6751, 56.5373, 2.24, "K0III"),
                Create("Caph", 0.1530, 59.1498, 2.27, "F2III"),
                Create("Gamma Cassiopeiae", 0.9451, 60.7167, 2.47, "B0.5IVe"),
                Create("Ruchbah", 1.4303, 60.2353, 2.68, "A5V"),
                Create("Segin", 2.0969, 63.6700, 3.35, "B3V"),
            },
            ["Cygnus (Northern Cross)"] = new[]
            {
                Create("Deneb", 20.6905, 45.2803, 1.25, "A2Ia"),
                Create("Sadr", 20.3705, 40.2567, 2.23, "F8Ib"),
                Create("Gienah", 20.7702, 33.9703, 2.48, "B8II"),
                Create("Albireo", 19.5120, 27.9597, 3.05, "K3II"),
                Create("Delta Cygni", 19.7499, 45.1308, 2.87, "B9V"),
            },
            ["Aquila"] = new[]
            {
                Create("Altair", 19.8464, 8.8683, 0.77, "A7V"),
                Create("Tarazed", 19.7711, 10.6132, 2.72, "K3II"),
                Create("Alshain", 19.9219, 6.4079, 3.72, "G8IV"),
            },
            ["Lyra"] = new[]
            {
                Create("Vega", 18.6156, 38.7837, 0.03, "A0V"),
                Create("Zeta Lyrae", 18.7469, 37.6050, 4.35, "A4V"),
                Create("Delta Lyrae", 18.9080, 36.8986, 4.22, "M4II"),
                Create("Sheliak", 18.8347, 33.3627, 3.52, "B7IV"),
                Create("Sulafat", 18.9824, 32.6896, 3.25, "B9III"),
            },
            ["Taurus"] = new[]
            {
                Create("Aldebaran", 4.5987, 16.5093, 0.85, "K5III"),
                Create("Elnath", 5.4382, 28.6075, 1.65, "B7III"),
                Create("Alcyone", 3.7914, 24.1051, 2.87, "B7III"),
                Create("Atlas", 3.6273, 24.0534, 3.62, "B8III"),
                Create("Electra", 3.7169, 24.1133, 3.70, "B6III"),
                Create("Maia", 3.7638, 24.3678, 3.86, "B8III"),
                Create("Merope", 3.7723, 23.9483, 4.17, "B6IV"),
                Create("Taygeta", 3.7913, 24.4673, 4.29, "B6IV"),
                Create("Pleione", 3.8281, 24.1367, 5.05, "B8V"),
            },
            ["Perseus"] = new[]
            {
                Create("Mirfak", 3.4054, 49.8612, 1.79, "F5Ib"),
                Create("Algol", 3.1361, 40.9556, 2.12, "B8V"),
                Create("Atik", 3.2020, 32.2883, 2.87, "B1III"),
                Create("Zeta Persei", 3.9250, 31.8836, 2.85, "B1Ib"),
            },
            ["Pegasus (Great Square) & Andromeda"] = new[]
            {
                Create("Markab", 23.0793, 15.2053, 2.49, "B9V"),
                Create("Scheat", 23.0629, 28.0825, 2.42, "M2III"),
                Create("Algenib", 0.2206, 15.1836, 2.83, "B2IV"),
                Create("Alpheratz", 0.1398, 29.0904, 2.07, "B9p/A0"),
                Create("Mirach", 1.1622, 35.6206, 2.06, "M0III"),
                Create("Almach", 2.0649, 42.3297, 2.10, "K3III"),
            },
            ["Cepheus"] = new[]
            {
                Create("Alderamin", 21.3097, 62.5856, 2.45, "A7IV"),
                Create("Alfirk", 21.4777, 70.5607, 3.23, "B2III"),
                Create("Mu Cephei", 21.7337, 58.7800, 4.00, "M2Ia"),
                Create("Zeta Cephei", 22.1809, 58.2013, 3.40, "K1III"),
            },
            ["Crux"] = new[]
            {
                Create("Acrux", 12.4433, -63.0990, 0.77, "B0.5IV"),
                Create("Mimosa", 12.7953, -59.6880, 1.25, "B0.5III"),
                Create("Gacrux", 12.5194, -57.1140, 1.63, "M3.5III"),
                Create("Delta Crucis", 12.2524, -58.7490, 2.60, "B2IV"),
                Create("Epsilon Crucis", 12.3560, -60.4010, 3.59, "K3III"),
            },
            ["Carina"] = new[]
            {
                Create("Canopus", 6.3992, -52.6950, -0.74, "A9II"),
                Create("Miaplacidus", 9.2203, -69.7180, 1.67, "A2IV"),
                Create("Avior", 8.3752, -59.5090, 1.86, "K3III"),
                Create("Aspidiske", 9.2848, -59.2750, 2.21, "A9Ib"),
                Create("Theta Carinae", 10.7159, -64.3940, 2.76, "B0V"),
            },
            ["Canis Major"] = new[]
            {
                Create("Sirius", 6.7525, -16.7160, -1.46, "A1V"),
                Create("Mirzam", 6.3783, -17.9550, 1.98, "B1III"),
                Create("Adhara", 6.9771, -28.9720, 1.50, "B2II"),
                Create("Wezen", 7.1399, -26.3930, 1.83, "F8Ia"),
                Create("Aludra", 7.4016, -29.3030, 2.45, "B5Ia"),
                Create("Muliphein", 7.0504, -24.3930, 2.93, "B1IV"),
            },
            ["Leo"] = new[]
            {
                Create("Regulus", 10.1395, 11.9670, 1.35, "B7V"),
                Create("Algieba", 10.3329, 19.8410, 2.01, "K1III"),
                Create("Zosma", 11.2351, 20.5230, 2.56, "A4V"),
                Create("Chertan", 11.2373, 15.4290, 3.34, "A2V"),
                Create("Denebola", 11.8177, 14.5710, 2.14, "A3V"),
            },
            ["Virgo"] = new[]
            {
                Create("Spica", 13.4199, -11.1610, 0.98, "B1V"),
                Create("Porrima", 12.6943, -1.4490, 2.74, "F0V"),
                Create("Vindemiatrix", 13.0363, 10.9590, 2.83, "G8III"),
                Create("Heze", 13.9114, -0.5950, 3.37, "A3V"),
            },
            ["Gemini"] = new[]
            {
                Create("Castor", 7.5766, 31.8880, 1.58, "A1V"),
                Create("Pollux", 7.7553, 28.0260, 1.14, "K0III"),
                Create("Alhena", 6.6285, 16.3990, 1.93, "A0IV"),
                Create("Tejat", 6.3827, 22.5130, 2.87, "M3III"),
                Create("Mebsuta", 6.7321, 25.1310, 3.06, "G8Ib"),
                Create("Wasat", 7.3354, 21.9820, 3.53, "F0IV"),
            },
            ["Canis Minor"] = new[]
            {
                Create("Procyon", 7.6550, 5.2250, 0.38, "F5IV"),
                Create("Gomeisa", 7.4525, 8.2890, 2.89, "B8V"),
            },
            ["Auriga"] = new[]
            {
                Create("Capella", 5.2782, 45.9970, 0.08, "G8III"),
                Create("Menkalinan", 5.9921, 44.9470, 1.90, "A2IV"),
                Create("Mahasim", 5.9922, 37.2130, 2.65, "A0p"),
                Create("Hassaleh", 5.1071, 33.3590, 2.69, "K3II"),
                Create("Almaaz", 5.0260, 43.8230, 3.00, "F0Ia"),
            },
            ["Andromeda"] = new[]
            {
                Create("Alpheratz", 0.1398, 29.0900, 2.07, "B9p"),
                Create("Mirach", 1.1622, 35.6210, 2.06, "M0III"),
                Create("Almach", 2.0649, 42.3300, 2.10, "K3III"),
                Create("Delta Andromedae", 0.6559, 30.8610, 3.28, "K3III"),
            },
            ["Pegasus (Great Square)"] = new[]
            {
                Create("Markab", 23.0793, 15.2050, 2.49, "B9V"),
                Create("Scheat", 23.0629, 28.0820, 2.42, "M2III"),
                Create("Algenib", 0.2206, 15.1830, 2.83, "B2IV"),
                Create("Enif", 21.7364, 9.8750, 2.38, "K2Ib"),
            },
            ["Scorpius"] = new[]
            {
                Create("Antares", 16.4901, -26.4320, 0.96, "M1.5Iab"),
                Create("Dschubba", 16.0056, -22.6217, 2.29, "B1.5V"),
                Create("Acrab", 16.0906, -19.8060, 2.62, "B1V"),
                Create("Shaula", 17.5601, -37.1038, 1.62, "B2IV"),
                Create("Sargas", 17.6229, -42.9978, 1.86, "F1II"),
                Create("Larawag", 16.8361, -34.2930, 2.29, "K1III"),
            },
            ["Sagittarius (Teapot)"] = new[]
            {
                Create("Kaus Australis", 18.4029, -34.3846, 1.79, "B9.5III"),
                Create("Kaus Media", 18.3499, -29.8281, 2.72, "K3III"),
                Create("Kaus Borealis", 18.4662, -25.4216, 2.81, "K2III"),
                Create("Nunki", 18.9211, -26.2967, 2.05, "B2.5V"),
                Create("Ascella", 19.0435, -29.8801, 2.60, "B3V"),
                Create("Alnasl", 18.0968, -30.4244, 2.98, "K0III"),
                Create("Phi Sagittarii", 19.1553, -26.2967, 3.17, "B8V"),
            },
        };

        Constellations = new ReadOnlyDictionary<string, IReadOnlyList<ConstellationStar>>(dictionary);
    }

    public IReadOnlyDictionary<string, IReadOnlyList<ConstellationStar>> GetAll() => Constellations;

    private static ConstellationStar Create(string name, double rightAscensionHours, double declinationDegrees, double magnitude, string? spectralType)
    {
        return new ConstellationStar(name, new Star(rightAscensionHours, declinationDegrees, magnitude, SpectralColor(spectralType)));
    }

    private static SKColor SpectralColor(string? spectral)
    {
        if (string.IsNullOrWhiteSpace(spectral))
        {
            return SKColors.White;
        }

        var first = spectral.TrimStart()[0];
        var c = char.ToUpperInvariant(first);
        return c switch
        {
            'O' => new SKColor(155, 176, 255),
            'B' => new SKColor(170, 191, 255),
            'A' => new SKColor(202, 215, 255),
            'F' => new SKColor(248, 247, 255),
            'G' => new SKColor(255, 244, 234),
            'K' => new SKColor(255, 210, 161),
            'M' => new SKColor(255, 180, 140),
            _ => SKColors.White,
        };
    }
}
