using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SCNexus.Models;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace SCNexus.Services;

public sealed partial class OcrProvider(GameDataService gameDataService) : IDataProvider
{
    private IReadOnlyList<VehicleCatalogItem>? _vehicles;
    public string Name => "Screen OCR";
    public DataSourceKind Source => DataSourceKind.Ocr;
    public int Priority => 5;
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(20);

    public async Task<DataProviderResult> CollectAsync(DataProviderContext context, CancellationToken token)
    {
        if (!context.OcrEnabled) return new DataProviderResult { Status = "Disabled" };
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || !IsStarCitizen(window))
            return new DataProviderResult { Status = "Waiting for Star Citizen foreground window" };
        var text = await CaptureAndRecognizeAsync(window, token);
        if (string.IsNullOrWhiteSpace(text)) return new DataProviderResult { Status = "No text recognized" };
        var now = DateTimeOffset.UtcNow;
        var values = new List<ValueObservation>();
        var balance = BalancePattern().Match(text);
        if (balance.Success)
        {
            var cleaned = balance.Groups["value"].Value.Replace(" ", "").Replace(" ", "").Replace(",", "").Replace(".", "");
            if (decimal.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
                values.Add(new ValueObservation("player.balance", amount.ToString(CultureInfo.InvariantCulture), Source, now, .78, "aUEC"));
        }
        AddTextValue(LocationPattern(), "player.location", text, now, values, .68);
        AddTextValue(ShipPattern(), "player.ship", text, now, values, .66);
        var records = new List<TypedObservation>();
        if (FleetScreenPattern().IsMatch(text))
        {
            try
            {
                _vehicles ??= await gameDataService.GetVehiclesAsync(token);
                foreach (var vehicle in _vehicles.Where(x => x.IsSpaceship == 1 && x.Name.Length >= 4 &&
                             text.Contains(x.Name, StringComparison.OrdinalIgnoreCase)).DistinctBy(x => x.Name))
                {
                    var id = "ocr-ship-" + vehicle.Id;
                    var detected = new DetectedShip
                    {
                        Id = id, Name = new ObservedValue<string>(vehicle.Name, Source, now, .82), IsCurrent = false,
                        Role = new ObservedValue<string>(VehicleCatalog.InferRole(vehicle), Source, now, .7)
                    };
                    records.Add(new TypedObservation("ship", id, detected, Source, now, .82));
                }
            }
            catch (HttpRequestException) { }
            catch (InvalidDataException) { }
        }
        return new DataProviderResult
        {
            Values = values, Records = records,
            Status = values.Count + records.Count == 0 ? "Screen read; no supported values found" : $"Recognized {values.Count + records.Count} values"
        };
    }

    private static async Task<string> CaptureAndRecognizeAsync(IntPtr window, CancellationToken token)
    {
        if (!GetClientRect(window, out var client) || client.Width <= 0 || client.Height <= 0) return "";
        var origin = new POINT();
        if (!ClientToScreen(window, ref origin)) return "";
        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(screenDc, client.Width, client.Height);
        var old = SelectObject(memoryDc, bitmap);
        try
        {
            if (!BitBlt(memoryDc, 0, 0, client.Width, client.Height, screenDc, origin.X, origin.Y, 0x00CC0020)) return "";
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            await using var encoded = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
            encoder.Save(encoded);
            encoded.Position = 0;
            token.ThrowIfCancellationRequested();
            using var random = encoded.AsRandomAccessStream();
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(random);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null) return "";
            var result = await engine.RecognizeAsync(softwareBitmap);
            return result.Text;
        }
        finally
        {
            SelectObject(memoryDc, old);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static void AddTextValue(Regex pattern, string key, string text, DateTimeOffset now,
        List<ValueObservation> values, double confidence)
    {
        var match = pattern.Match(text);
        if (!match.Success) return;
        var value = match.Groups["value"].Value.Trim(' ', ':', '-', '•', '|');
        if (value.Length is > 1 and < 100) values.Add(new ValueObservation(key, value, DataSourceKind.Ocr, now, confidence));
    }

    private static bool IsStarCitizen(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var processId);
        try { return Process.GetProcessById((int)processId).ProcessName.Equals("StarCitizen", StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }

    [GeneratedRegex(@"(?<value>[0-9][0-9\s .,]{2,})\s*aUEC", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex BalancePattern();
    [GeneratedRegex(@"(?:Current\s+Location|Location|Текущая\s+локация|Локация)\s*[:\-]?\s*(?<value>[^\r\n]{2,80})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex LocationPattern();
    [GeneratedRegex(@"(?:Current\s+Ship|Ship|Vehicle|Текущий\s+корабль|Корабль)\s*[:\-]?\s*(?<value>[^\r\n]{2,80})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex ShipPattern();
    [GeneratedRegex(@"(ASOP|Vehicle\s+Loadout|Fleet\s+Manager|Retrieve\s+Vehicle|Мой\s+флот|Корабли)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex FleetScreenPattern();

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(IntPtr window, ref POINT point);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int operation);
}
