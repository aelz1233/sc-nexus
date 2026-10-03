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
        var lines = await CaptureAndRecognizeAsync(window, token);
        if (lines.Count == 0) return new DataProviderResult { Status = "No text recognized" };
        try { _vehicles ??= await gameDataService.GetVehiclesAsync(token); }
        catch (HttpRequestException) { }
        catch (InvalidDataException) { }
        if (GetForegroundWindow() != window) return new DataProviderResult { Status = "Waiting for Star Citizen foreground window" };
        return ParseLines(lines, _vehicles ?? [], DateTimeOffset.UtcNow);
    }

    internal static DataProviderResult ParseText(string text, IReadOnlyList<VehicleCatalogItem> vehicles, DateTimeOffset now)
        => ParseLines(text.Split('\n').Select((line, i) => new ScreenLine(line, new Rect(0, i * 20, 100, 20))).ToArray(), vehicles, now);

    private static DataProviderResult ParseLines(IReadOnlyList<ScreenLine> lines, IReadOnlyList<VehicleCatalogItem> vehicles, DateTimeOffset now)
    {
        var text = string.Join("\n", lines.Select(x => x.Text));
        var values = new List<ValueObservation>();
        var balance = BalancePattern().Match(text);
        if (balance.Success)
        {
            var cleaned = balance.Groups["value"].Value.Replace(" ", "").Replace(" ", "").Replace(",", "").Replace(".", "");
            if (decimal.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
                values.Add(new ValueObservation("player.balance", amount.ToString(CultureInfo.InvariantCulture), DataSourceKind.Ocr, now, .78, "aUEC"));
        }
        AddTextValue(LocationPattern(), "player.location", text, now, values, .68);
        var records = new List<TypedObservation>();
        var current = ShipPattern().Match(text);
        var currentModel = current.Success ? vehicles.FirstOrDefault(v => v.IsSpaceship == 1 &&
            (v.Name.Equals(current.Groups["value"].Value.Trim(), StringComparison.OrdinalIgnoreCase) ||
             v.NameFull.Equals(current.Groups["value"].Value.Trim(), StringComparison.OrdinalIgnoreCase))) : null;
        if (currentModel is not null)
            values.Add(new ValueObservation("player.ship", currentModel.Name, DataSourceKind.Ocr, now, .82));
        if (FleetScreenPattern().IsMatch(text) || currentModel is not null)
        {
            foreach (var vehicle in ReadFleetScreen(lines, vehicles).Rows.Where(x => !x.Locked && x.StatusRecognized).Select(x => x.Vehicle).DistinctBy(x => x.Id))
            {
                var id = "ocr-ship-" + vehicle.Id;
                var detected = new DetectedShip
                {
                    Id = id, Name = new ObservedValue<string>(vehicle.Name, DataSourceKind.Ocr, now, .82), IsCurrent = vehicle == currentModel,
                    Role = new ObservedValue<string>(VehicleCatalog.InferRole(vehicle), DataSourceKind.Uex, now, .95)
                };
                records.Add(new TypedObservation("ship", id, detected, DataSourceKind.Ocr, now, .82));
            }
        }
        return new DataProviderResult
        {
            Values = values, Records = records,
            Status = values.Count + records.Count == 0 ? "Screen read; no supported values found" : $"Recognized {values.Count + records.Count} values"
        };
    }

    internal sealed record ScreenLine(string Text, Rect Bounds);

    internal static FleetScreen ReadFleetScreen(IReadOnlyList<ScreenLine> lines, IReadOnlyList<VehicleCatalogItem> vehicles)
    {
        var text = string.Join("\n", lines.Select(x => x.Text));
        var candidates = new List<(VehicleCatalogItem Vehicle, Rect Bounds)>();
        var patterns = vehicles.Where(x => x.IsSpaceship == 1 && x.Name.Length >= 4).DistinctBy(x => x.Id)
            .OrderByDescending(x => x.Name.Length).Select(x => (Vehicle: x, Pattern: new Regex(
                @"(?<![\p{L}\p{N}])(?:" + string.Join("|", new[] { x.NameFull, x.Name }.Where(n => !string.IsNullOrWhiteSpace(n))
                    .OrderByDescending(n => n.Length).Select(n => Regex.Escape(n).Replace(@"\ ", @"\s+"))) + @")(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))).ToArray();
        foreach (var line in lines.OrderBy(x => x.Bounds.Top))
        {
            var remaining = line.Text;
            foreach (var entry in patterns)
            {
                if (!entry.Pattern.IsMatch(remaining)) continue;
                candidates.Add((entry.Vehicle, line.Bounds));
                remaining = entry.Pattern.Replace(remaining, match => new string(' ', match.Length));
            }
        }
        var anchors = new List<(VehicleCatalogItem Vehicle, Rect Bounds)>();
        foreach (var candidate in candidates.OrderBy(x => x.Bounds.Top).ThenByDescending(x => x.Vehicle.Name.Length))
        {
            if (anchors.Count > 0 && Math.Abs(anchors[^1].Bounds.Top - candidate.Bounds.Top) < Math.Max(8, candidate.Bounds.Height * .7))
            {
                if (candidate.Vehicle.Name.Length > anchors[^1].Vehicle.Name.Length) anchors[^1] = candidate;
                continue;
            }
            anchors.Add(candidate);
        }
        var rows = anchors.Select((entry, i) =>
        {
            var top = entry.Bounds.Top - entry.Bounds.Height * .35;
            var bottom = i + 1 < anchors.Count ? anchors[i + 1].Bounds.Top - anchors[i + 1].Bounds.Height * .35 : double.PositiveInfinity;
            bottom = Math.Min(bottom, entry.Bounds.Bottom + entry.Bounds.Height * 3);
            var rowText = string.Join(" ", lines.Where(x => x.Bounds.Top >= top && x.Bounds.Top < bottom).Select(x => x.Text));
            // Missing/unreadable terminal status is not evidence that a ship is available.
            var statusRecognized = !FleetTerminalPattern().IsMatch(text) || AvailableStatusPattern().IsMatch(rowText);
            return new FleetScreenRow(entry.Vehicle, LockedPattern().IsMatch(rowText), entry.Bounds, statusRecognized);
        }).ToArray();
        return new(text, rows, FleetTerminalPattern().IsMatch(text));
    }

    public async Task<DataProviderResult> DetectShipsAsync(DataProviderContext context, IProgress<FleetScanSummary>? progress, CancellationToken token)
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || !IsStarCitizen(window)) return new() { Status = "Waiting for Star Citizen foreground window" };
        try { _vehicles ??= await gameDataService.GetVehiclesAsync(token); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or InvalidOperationException) { }
        if (_vehicles is not { Count: > 0 }) return new() { Status = "Ship catalog unavailable" };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        stop.CancelAfter(TimeSpan.FromSeconds(100));
        var watching = WatchForStopAsync(window, stop);
        try
        {
            var firstLines = await CaptureAndRecognizeAsync(window, stop.Token);
            stop.Token.ThrowIfCancellationRequested();
            var first = ReadFleetScreen(firstLines, _vehicles);
            if (!first.IsTerminal || first.Rows.Count == 0) return ParseLines(firstLines, _vehicles, DateTimeOffset.UtcNow);
            var scanned = await FleetTerminalScan.RunAsync(first,
                async cancellation => ReadFleetScreen(await CaptureAndRecognizeAsync(window, cancellation), _vehicles),
                async (page, direction, cancellation) =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (GetForegroundWindow() != window || GetAsyncKeyState(0x1B) < 0) { stop.Cancel(); cancellation.ThrowIfCancellationRequested(); }
                    var row = page.Rows[page.Rows.Count / 2];
                    var origin = new POINT();
                    if (!ClientToScreen(window, ref origin)) return false;
                    // Point at recognized list text, never at retrieve/claim buttons. Send wheel input only.
                    if (!SetCursorPos(origin.X + (int)(row.Bounds.Left + Math.Min(60, row.Bounds.Width / 2)), origin.Y + (int)(row.Bounds.Top + row.Bounds.Height / 2))) return false;
                    await Task.Delay(80, cancellation);
                    if (GetForegroundWindow() != window || GetAsyncKeyState(0x1B) < 0) { stop.Cancel(); cancellation.ThrowIfCancellationRequested(); }
                    var input = new INPUT { Type = 0, Mouse = new MOUSEINPUT { MouseData = unchecked((uint)(direction * 120)), Flags = 0x0800 } };
                    if (SendInput(1, [input], Marshal.SizeOf<INPUT>()) != 1) return false;
                    await Task.Delay(450, cancellation);
                    return true;
                }, progress, stop.Token);
            var now = DateTimeOffset.UtcNow;
            return new DataProviderResult
            {
                Records = scanned.Vehicles.Select(vehicle => new TypedObservation("ship", "ocr-ship-" + vehicle.Id, new DetectedShip
                {
                    Id = "ocr-ship-" + vehicle.Id,
                    Name = new(vehicle.Name, DataSourceKind.Ocr, now, .82),
                    Role = new(VehicleCatalog.InferRole(vehicle), DataSourceKind.Uex, now, .95)
                }, DataSourceKind.Ocr, now, .82)).ToArray(),
                Status = $"Fleet scan: {scanned.Vehicles.Count} models; {scanned.Summary.StopReason}", FleetScan = scanned.Summary
            };
        }
        finally { stop.Cancel(); await watching; }
    }

    private static async Task WatchForStopAsync(IntPtr window, CancellationTokenSource stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (GetForegroundWindow() != window || GetAsyncKeyState(0x1B) < 0) { stop.Cancel(); return; }
                await Task.Delay(25, stop.Token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static async Task<IReadOnlyList<ScreenLine>> CaptureAndRecognizeAsync(IntPtr window, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (GetForegroundWindow() != window || !GetClientRect(window, out var client) || client.Width <= 0 || client.Height <= 0) return [];
        var origin = new POINT();
        if (!ClientToScreen(window, ref origin)) return [];
        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(screenDc, client.Width, client.Height);
        var old = SelectObject(memoryDc, bitmap);
        try
        {
            if (!BitBlt(memoryDc, 0, 0, client.Width, client.Height, screenDc, origin.X, origin.Y, 0x00CC0020)) return [];
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
            var ratio = Math.Min(1d, (double)OcrEngine.MaxImageDimension / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
            var transform = new BitmapTransform { ScaledWidth = (uint)Math.Max(1, decoder.PixelWidth * ratio), ScaledHeight = (uint)Math.Max(1, decoder.PixelHeight * ratio) };
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            var engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US")) ?? OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null) throw new InvalidOperationException("Windows OCR language is not installed");
            var results = new List<OcrResult> { await engine.RecognizeAsync(softwareBitmap) };
            // ASOP may use Russian labels while ship models remain Latin. Both passes share one image.
            if (!engine.RecognizerLanguage.LanguageTag.StartsWith("ru", StringComparison.OrdinalIgnoreCase) && OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ru-RU")) is { } russian)
                results.Add(await russian.RecognizeAsync(softwareBitmap));
            token.ThrowIfCancellationRequested();
            return results.SelectMany(x => x.Lines).Select(line =>
            {
                var bounds = Rect.Empty;
                foreach (var word in line.Words) bounds.Union(new Rect(word.BoundingRect.X / ratio, word.BoundingRect.Y / ratio,
                    word.BoundingRect.Width / ratio, word.BoundingRect.Height / ratio));
                return new ScreenLine(line.Text, bounds);
            }).Where(x => !x.Bounds.IsEmpty).OrderBy(x => x.Bounds.Top).ToArray();
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
        try { using var process = Process.GetProcessById((int)processId); return process.ProcessName.Equals("StarCitizen", StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    [GeneratedRegex(@"(?:Balance|Wallet|Баланс)[ \t]*[:\-]?[ \t]*(?<value>[0-9][0-9 \t .,]*)[ \t]*aUEC", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex BalancePattern();
    [GeneratedRegex(@"(?:Current\s+Location|Location|Текущая\s+локация|Локация)\s*[:\-]?\s*(?<value>[^\r\n]{2,80})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex LocationPattern();
    [GeneratedRegex(@"^(?:Current[ \t]+Ship|Текущий[ \t]+корабль)[ \t]*[:\-][ \t]*(?<value>[^\r\n]{2,80})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline)] private static partial Regex ShipPattern();
    [GeneratedRegex(@"(\bASOP\b|Vehicle\s+Loadout|Fleet\s+Manager|Retrieve\s+Vehicle|Мой\s+флот|Менеджер\s+парка\s+техники)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex FleetScreenPattern();
    [GeneratedRegex(@"(\bASOP\b|Fleet\s+Manager|Vehicle\s+Retrieval|Менеджер\s+парка\s+техники)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex FleetTerminalPattern();
    [GeneratedRegex(@"(\bLOCKED\b|ЗАБЛОКИРОВА[НH][ОOНHАAЫЬ]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex LockedPattern();
    [GeneratedRegex(@"\b(Stored|Storage|Claim|Claiming|Delivery|Delivering|Destroyed|Retrieve|Retrieving|Ready|Unlocked)\b|ХРАНИТСЯ|ХРАНЕНИ[ЕИЯ]|ДОСТАВ|ВОССТАНОВ|ВОЗМЕСТИТЬ|УНИЧТОЖ|ВЫЗВАТЬ|ПОЛУЧИТЬ|ИЗВЛЕЧЬ|ГОТОВ|ДОСТУП(?:ЕН|НО)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex AvailableStatusPattern();

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public MOUSEINPUT Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public UIntPtr ExtraInfo;
    }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
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
