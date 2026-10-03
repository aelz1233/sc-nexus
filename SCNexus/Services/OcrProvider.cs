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
    private ValueObservation? _previousBalance;
    public string Name => "Screen OCR";
    public DataSourceKind Source => DataSourceKind.Ocr;
    public int Priority => 5;
    public int IntervalSeconds { get; set; } = 5;
    public bool AutoFleetEnabled { get; set; } = true;
    public bool ScrollFleetOnRequest { get; set; } = true;
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(Math.Clamp(IntervalSeconds, 5, 30));

    public async Task<DataProviderResult> CollectAsync(DataProviderContext context, CancellationToken token)
    {
        if (!context.OcrEnabled) return new DataProviderResult { Status = "Disabled" };
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || !IsStarCitizen(window))
            return new DataProviderResult { Status = "Waiting for Star Citizen foreground window" };
        var lines = await CaptureAndRecognizeAsync(window, token);
        if (lines.Count == 0) return new DataProviderResult { Status = "No text recognized" };
        var text = string.Join("\n", lines.Select(x => x.Text));
        try
        {
            if (AutoFleetEnabled && FleetTerminalPattern().IsMatch(text))
                _vehicles ??= await gameDataService.GetVehiclesAsync(token);
        }
        catch (HttpRequestException) { }
        catch (InvalidDataException) { }
        if (GetForegroundWindow() != window) return new DataProviderResult { Status = "Waiting for Star Citizen foreground window" };
        return ConfirmBalance(ParseLines(lines, AutoFleetEnabled ? _vehicles ?? [] : [], DateTimeOffset.UtcNow));
    }

    internal static DataProviderResult ParseText(string text, IReadOnlyList<VehicleCatalogItem> vehicles, DateTimeOffset now)
        => ParseLines(text.Split('\n').Select((line, i) => new ScreenLine(line, new Rect(0, i * 20, 100, 20))).ToArray(), vehicles, now);

    private static DataProviderResult ParseLines(IReadOnlyList<ScreenLine> lines, IReadOnlyList<VehicleCatalogItem> vehicles, DateTimeOffset now)
    {
        var text = string.Join("\n", lines.Select(x => x.Text));
        var values = new List<ValueObservation>();
        if (ReadContractReward(lines) is { } reward)
            values.Add(new("mission.offeredReward", reward.ToString(CultureInfo.InvariantCulture), DataSourceKind.Ocr, now, .8, "aUEC"));
        var balance = BalancePattern().Match(text);
        if (balance.Success)
        {
            if (BalanceText.TryParse(balance.Groups["value"].Value, out var amount))
                values.Add(new ValueObservation("player.balance", amount.ToString(CultureInfo.InvariantCulture), DataSourceKind.Ocr, now, .78, "aUEC"));
        }
        else if (ReadMobiGlasBalance(lines) is { } mobiBalance)
            values.Add(new("player.balance", mobiBalance.ToString(CultureInfo.InvariantCulture), DataSourceKind.Ocr, now, .78, "aUEC"));
        AddTextValue(LocationPattern(), "player.location", text, now, values, .68);
        var records = new List<TypedObservation>();
        if (FleetTerminalPattern().IsMatch(text))
        {
            foreach (var vehicle in ReadFleetScreen(lines, vehicles).Rows.Where(x => !x.Locked && x.StatusRecognized).Select(x => x.Vehicle).DistinctBy(x => x.Id))
            {
                var id = "ocr-ship-" + vehicle.Id;
                var detected = new DetectedShip
                {
                    Id = id, Name = new ObservedValue<string>(vehicle.Name, DataSourceKind.Ocr, now, .82),
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

    internal static decimal? ReadContractReward(IReadOnlyList<ScreenLine> lines)
    {
        var label = lines.FirstOrDefault(x => Regex.IsMatch(x.Text.Trim(), @"^(НАГРАДА|REWARD)$", RegexOptions.IgnoreCase));
        if (label is null) return null;
        var candidates = lines.Where(x => x.Bounds.Left > label.Bounds.Right &&
            Math.Abs(x.Bounds.Top - label.Bounds.Top) <= Math.Max(8, label.Bounds.Height))
            .Select(x => Regex.Match(x.Text.Trim(), @"^[^\d\s]{0,2}\s*(?<amount>\d[\d,.\s]*)$"))
            .Where(x => x.Success).Select(x => BalanceText.TryParse(x.Groups["amount"].Value, out var amount) ? (decimal?)amount : null)
            .Where(x => x > 0).Distinct().ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static bool IsHomeLabel(string text) => Regex.IsMatch(text.Trim(), @"^(HOME|ГЛАВН[А-Я]{1,2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static decimal? ReadMobiGlasBalance(IReadOnlyList<ScreenLine> lines)
    {
        var home = lines.FirstOrDefault(x => IsHomeLabel(x.Text));
        if (home is null || !lines.Any(x => Regex.IsMatch(x.Text, @"\b(HEALTH|CRIMESTAT|UEE)\b|ЗДОР|КРИМСТАТ", RegexOptions.IgnoreCase))) return null;
        var tolerance = Math.Max(40, home.Bounds.Height * 4);
        var candidates = lines.Where(x => x.Bounds.Right < home.Bounds.Left && x.Bounds.Left > home.Bounds.Left - tolerance * 8 &&
            x.Bounds.Top >= home.Bounds.Top - tolerance && x.Bounds.Top <= home.Bounds.Bottom &&
            Regex.IsMatch(x.Text.Trim(), @"^\d+(?:[,\.\s]\d{3})*(?:[,.]\d{2})?$"))
            .Select(x => BalanceText.TryParse(x.Text, out var value) ? (decimal?)value : null).Where(x => x is not null).Distinct().ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    internal DataProviderResult ConfirmBalance(DataProviderResult result)
    {
        var balance = result.Values.FirstOrDefault(x => x.Key == "player.balance");
        var previous = _previousBalance;
        _previousBalance = balance;
        if (balance is null || previous is null || balance.Value != previous.Value ||
            balance.Timestamp - previous.Timestamp < TimeSpan.FromMilliseconds(400) ||
            balance.Timestamp - previous.Timestamp > TimeSpan.FromSeconds(45)) return result;
        return new() { Values = result.Values.Select(x => x == balance ? x with { Confidence = .94 } : x).ToArray(),
            Records = result.Records, Status = result.Status, FleetScan = result.FleetScan };
    }

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
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        stop.CancelAfter(TimeSpan.FromSeconds(100));
        var watching = WatchForStopAsync(window, stop);
        try
        {
            var firstLines = await CaptureAndRecognizeAsync(window, stop.Token);
            stop.Token.ThrowIfCancellationRequested();
            if (!FleetTerminalPattern().IsMatch(string.Join("\n", firstLines.Select(x => x.Text))))
                return new() { Status = "Open ASOP terminal" };
            try { _vehicles ??= await gameDataService.GetVehiclesAsync(stop.Token); }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or InvalidOperationException) { }
            if (_vehicles is not { Count: > 0 }) return new() { Status = "Ship catalog unavailable" };
            var first = ReadFleetScreen(firstLines, _vehicles);
            if (!first.IsTerminal) return new() { Status = "Open ASOP terminal" };
            if (!ScrollFleetOnRequest || first.Rows.Count == 0)
                return ParseLines(firstLines, _vehicles, DateTimeOffset.UtcNow);
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
            return await RecognizeScreenAsync(source, token);
        }
        finally
        {
            SelectObject(memoryDc, old);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    internal static async Task<IReadOnlyList<ScreenLine>> RecognizeScreenAsync(BitmapSource source, CancellationToken token)
    {
        var lines = (await RecognizeBitmapAsync(source, token)).ToList();
        var rewardLabel = lines.FirstOrDefault(x => Regex.IsMatch(x.Text.Trim(), @"^(НАГРАДА|REWARD)$", RegexOptions.IgnoreCase));
        if (rewardLabel is not null)
        {
            var rewardLeft = Math.Max(0, (int)rewardLabel.Bounds.Left);
            var rewardTop = Math.Max(0, (int)rewardLabel.Bounds.Top - 20);
            var rewardHeight = Math.Min(source.PixelHeight - rewardTop, (int)rewardLabel.Bounds.Height + 40);
            var rewardCrop = new CroppedBitmap(source, new Int32Rect(rewardLeft, rewardTop, source.PixelWidth - rewardLeft, rewardHeight));
            var gray = new FormatConvertedBitmap(rewardCrop, System.Windows.Media.PixelFormats.Gray8, null, 0);
            var pixels = new byte[gray.PixelWidth * gray.PixelHeight];
            gray.CopyPixels(pixels, gray.PixelWidth, 0);
            for (var i = 0; i < pixels.Length; i++) pixels[i] = pixels[i] >= 215 ? (byte)0 : (byte)255;
            var clean = BitmapSource.Create(gray.PixelWidth, gray.PixelHeight, 96, 96,
                System.Windows.Media.PixelFormats.Gray8, null, pixels, gray.PixelWidth);
            clean.Freeze();
            var rewardZoom = new TransformedBitmap(clean, new System.Windows.Media.ScaleTransform(3, 3));
            rewardZoom.Freeze();
            var rewardLines = await RecognizeBitmapAsync(rewardZoom, token);
            lines.RemoveAll(x => x.Bounds.Left > rewardLabel.Bounds.Right && x.Bounds.Top >= rewardTop && x.Bounds.Bottom <= rewardTop + rewardHeight);
            lines.AddRange(rewardLines.Select(x => new ScreenLine(x.Text, new Rect(rewardLeft + x.Bounds.X / 3,
                rewardTop + x.Bounds.Y / 3, x.Bounds.Width / 3, x.Bounds.Height / 3))));
        }
        var home = lines.FirstOrDefault(x => IsHomeLabel(x.Text));
        // The tiny toolbar label may be unreadable at full-screen resolution. Enlarge the
        // toolbar first, but still require its Home anchor and wallet geometry when parsing.
        if (home is null && source.PixelHeight >= 600 && lines.Any(x =>
            Regex.IsMatch(x.Text, @"\b(HEALTH|CRIMESTAT)\b|ЗДОРОВЬЕ|КРИМСТАТ", RegexOptions.IgnoreCase)))
        {
            var toolbarTop = (int)(source.PixelHeight * .85);
            var toolbar = new CroppedBitmap(source, new Int32Rect(0, toolbarTop,
                (int)(source.PixelWidth * .5), source.PixelHeight - toolbarTop));
            var zoomed = new TransformedBitmap(toolbar, new System.Windows.Media.ScaleTransform(3, 3));
            zoomed.Freeze();
            var toolbarLines = await RecognizeBitmapAsync(zoomed, token);
            lines.RemoveAll(x => x.Bounds.Top >= toolbarTop && x.Bounds.Right <= source.PixelWidth * .5);
            lines.AddRange(toolbarLines.Select(x => new ScreenLine(x.Text,
                new Rect(x.Bounds.X / 3, toolbarTop + x.Bounds.Y / 3, x.Bounds.Width / 3, x.Bounds.Height / 3))));
            home = lines.FirstOrDefault(x => IsHomeLabel(x.Text));
            if (ReadMobiGlasBalance(lines) is not null) return lines;
        }
        if (home is null) return lines;
        var tolerance = Math.Max(40, home.Bounds.Height * 4);
        var left = Math.Max(0, (int)(home.Bounds.Left - tolerance * 8));
        var top = Math.Max(0, (int)(home.Bounds.Top - tolerance));
        var right = Math.Min(source.PixelWidth, (int)(home.Bounds.Right + tolerance * 3));
        var bottom = Math.Min(source.PixelHeight, (int)home.Bounds.Bottom + 20);
        if (right <= left || bottom <= top) return lines;
        var crop = new CroppedBitmap(source, new Int32Rect(left, top, right - left, bottom - top));
        var enlarged = new TransformedBitmap(crop, new System.Windows.Media.ScaleTransform(3, 3));
        enlarged.Freeze();
        var detail = await RecognizeBitmapAsync(enlarged, token);
        // Replace coarse OCR in the wallet region; mixing both passes would retain misread amounts.
        lines.RemoveAll(x => x.Bounds.Left >= left && x.Bounds.Right < home.Bounds.Left && x.Bounds.Top >= top && x.Bounds.Bottom <= bottom);
        lines.AddRange(detail.Select(x => new ScreenLine(x.Text, new Rect(left + x.Bounds.X / 3,
            top + x.Bounds.Y / 3, x.Bounds.Width / 3, x.Bounds.Height / 3))));
        return lines.OrderBy(x => x.Bounds.Top).ToArray();
    }

    private static async Task<IReadOnlyList<ScreenLine>> RecognizeBitmapAsync(BitmapSource source, CancellationToken token)
    {
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
