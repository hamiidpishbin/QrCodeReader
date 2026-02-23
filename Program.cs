using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using PDFtoImage;
using SkiaSharp;
using ZXing;
using ZXing.SkiaSharp;

if (args.Length == 0)
{
    Console.WriteLine("Usage: QrCodeReader <path-to-pdf>");
    Console.WriteLine("Example: QrCodeReader document.pdf");
    return 1;
}

string pdfPath = args[0];

if (!File.Exists(pdfPath))
{
    Console.Error.WriteLine($"Error: File not found — {pdfPath}");
    return 1;
}

Console.WriteLine($"Scanning: {pdfPath}");
Console.WriteLine(new string('-', 40));

var reader = new ZXing.SkiaSharp.BarcodeReader
{
    Options =
    {
        PossibleFormats = [BarcodeFormat.QR_CODE],
        TryHarder = true,
        TryInverted = true,
    }
};

var results = new List<(int Page, string Content)>();
int pageIndex = 0;

#pragma warning disable CA1416 // PDFtoImage supports Linux, macOS, Windows — all desktop targets
using var pdfStream = File.OpenRead(pdfPath);
foreach (SKBitmap bitmap in Conversion.ToImages(pdfStream))
#pragma warning restore CA1416
{
    pageIndex++;

    using (bitmap)
    {
        Result? qr = reader.Decode(bitmap);
        if (qr is not null)
        {
            results.Add((pageIndex, qr.Text));
        }
    }
}

if (results.Count == 0)
{
    Console.WriteLine("No QR codes found in the document.");
    return 0;
}

Console.WriteLine($"Found {results.Count} QR code(s):\n");

using var http = new HttpClient();

foreach (var (page, content) in results)
{
    Console.WriteLine($"  Page {page}: {content}");

    Guid? guid = ExtractGuid(content);
    if (guid is null)
    {
        Console.WriteLine("  No GUID found in the QR content.\n");
        continue;
    }

    Console.WriteLine($"  GUID: {guid}");
    Console.WriteLine("  Calling policy API...");

    try
    {
        var response = await http.PostAsJsonAsync(
            "https://sanhabinq.centinsur.ir/back/api/CarThirdParty/Policy/Guid",
            new { guid = guid.ToString() }
        );

        string json = await response.Content.ReadAsStringAsync();

        // Pretty-print the JSON
        try
        {
            var parsed = JsonSerializer.Deserialize<JsonElement>(json);
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            json = JsonSerializer.Serialize(parsed, jsonOptions);
        }
        catch { /* leave as-is if not valid JSON */ }

        Console.WriteLine($"  Status: {(int)response.StatusCode} {response.StatusCode}");
        Console.WriteLine($"  Response:\n{json}");

        string outputPath = Path.ChangeExtension(pdfPath, ".json");
        await File.WriteAllTextAsync(outputPath, json);
        Console.WriteLine($"  Saved to: {outputPath}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  API call failed: {ex.Message}");
    }

    Console.WriteLine();
}

return 0;

static Guid? ExtractGuid(string text)
{
    // Match a standard GUID pattern anywhere in the string (e.g. inside a URL)
    var match = System.Text.RegularExpressions.Regex.Match(
        text,
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"
    );

    return match.Success ? Guid.Parse(match.Value) : null;
}
