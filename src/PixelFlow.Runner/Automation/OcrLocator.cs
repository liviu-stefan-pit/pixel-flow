using System.Drawing;
using System.Runtime.InteropServices.WindowsRuntime;
using FuzzySharp;
using PixelFlow.Core.Projects;
using PixelFlow.Core.Runner;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace PixelFlow.Runner.Automation;

/// <summary>
/// P13: Windows.Media.Ocr + bounded fuzzy text match against on-screen words.
/// </summary>
internal static class OcrLocator
{
    public static async Task<ResolveResult> FindAsync(LocatorLayer layer, ProcessWindowScope? scope)
    {
        if (!layer.Enabled)
        {
            return ResolveResult.NotFound("Ocr layer is disabled.");
        }

        if (string.IsNullOrWhiteSpace(layer.Text))
        {
            return ResolveResult.NotFound("Ocr layer requires text.");
        }

        var processName = scope?.ProcessName;
        if (string.IsNullOrWhiteSpace(processName))
        {
            return ResolveResult.NotFound("Process scope is required (locator.scope.processName).");
        }

        if (!ProcessWindowBounds.TryGet(processName, scope?.WindowTitle, out var originX, out var originY, out var width, out var height, out var pid, out var boundFailure))
        {
            return ResolveResult.NotFound(boundFailure);
        }

        using var bitmap = ScreenCapture.CaptureRegion(originX, originY, width, height);
        var softwareBitmap = await ToSoftwareBitmapAsync(bitmap).ConfigureAwait(false);

        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
        if (engine is null)
        {
            return ResolveResult.NotFound("Windows.Media.Ocr engine is unavailable on this machine.");
        }

        var result = await engine.RecognizeAsync(softwareBitmap).AsTask().ConfigureAwait(false);
        var needle = layer.Text.Trim();
        var threshold = layer.ConfidenceThreshold <= 0 ? 0.85 : layer.ConfidenceThreshold;

        OcrWord? bestWord = null;
        double bestScore = 0;
        OcrLine? bestLine = null;

        foreach (var line in result.Lines)
        {
            // Prefer whole-line match when the needle spans multiple words.
            var lineScore = Ratio(needle, line.Text);
            if (lineScore >= bestScore)
            {
                bestScore = lineScore;
                bestLine = line;
                bestWord = null;
            }

            foreach (var word in line.Words)
            {
                var score = Ratio(needle, word.Text);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestWord = word;
                    bestLine = null;
                }
            }
        }

        if (bestScore < threshold)
        {
            return ResolveResult.NotFound(
                $"OCR text '{needle}' not found above threshold {threshold:0.##} (best={bestScore:0.##}).");
        }

        Windows.Foundation.Rect box;
        string matchedText;
        if (bestWord is not null)
        {
            box = bestWord.BoundingRect;
            matchedText = bestWord.Text;
        }
        else if (bestLine is not null)
        {
            box = UnionLineBounds(bestLine);
            matchedText = bestLine.Text;
        }
        else
        {
            return ResolveResult.NotFound($"OCR text '{needle}' matched score but had no geometry.");
        }

        var bounds = new ScreenRect(
            originX + box.X,
            originY + box.Y,
            box.Width,
            box.Height);

        return new ResolveResult(
            Found: true,
            CandidateId: $"ocr:{pid}:{matchedText}",
            BoundingRect: bounds,
            Name: matchedText,
            ControlType: "Ocr.Text",
            ProcessId: pid,
            MatchedLayer: LocatorKinds.Ocr,
            Confidence: bestScore);
    }

    private static double Ratio(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(b))
        {
            return 0;
        }

        // FuzzySharp returns 0..100
        return Fuzz.Ratio(a, b) / 100.0;
    }

    private static Windows.Foundation.Rect UnionLineBounds(OcrLine line)
    {
        if (line.Words.Count == 0)
        {
            return default;
        }

        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (var word in line.Words)
        {
            var r = word.BoundingRect;
            left = Math.Min(left, r.X);
            top = Math.Min(top, r.Y);
            right = Math.Max(right, r.X + r.Width);
            bottom = Math.Max(bottom, r.Y + r.Height);
        }

        return new Windows.Foundation.Rect(left, top, right - left, bottom - top);
    }

    private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
        ms.Position = 0;

        using var raStream = new InMemoryRandomAccessStream();
        using (var output = raStream.GetOutputStreamAt(0))
        {
            var bytes = ms.ToArray();
            await output.WriteAsync(bytes.AsBuffer()).AsTask().ConfigureAwait(false);
            await output.FlushAsync().AsTask().ConfigureAwait(false);
        }

        var decoder = await BitmapDecoder.CreateAsync(raStream).AsTask().ConfigureAwait(false);
        return await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied).AsTask().ConfigureAwait(false);
    }
}
