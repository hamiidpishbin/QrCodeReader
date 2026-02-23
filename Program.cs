using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using PDFtoImage;
using ZXing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapPost("/scan", async (IFormFile pdf, IHttpClientFactory httpClientFactory) =>
{
    var qrReader = new ZXing.SkiaSharp.BarcodeReader
    {
        Options =
        {
            PossibleFormats = [BarcodeFormat.QR_CODE],
            TryHarder = true,
            TryInverted = true,
        }
    };

    string? qrContent = null;

    await using var stream = pdf.OpenReadStream();
    foreach (var bitmap in Conversion.ToImages(stream))
    {
        using (bitmap)
        {
            var result = qrReader.Decode(bitmap);
            
            if (result is null) continue;
            
            qrContent = result.Text;
            break;
        }
    }

    if (qrContent is null)
    {
        return Results.BadRequest("No QR code found in the PDF.");
    }

    var match = Regex.Match(
        qrContent,
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"
    );

    if (!match.Success)
    {
        return Results.BadRequest($"No GUID found in QR content: {qrContent}");
    }

    var guid = Guid.Parse(match.Value);

    var http = httpClientFactory.CreateClient();
    var apiResponse = await http.PostAsJsonAsync(
        "https://sanhabinq.centinsur.ir/back/api/CarThirdParty/Policy/Guid",
        new { guid = guid.ToString() }
    );

    var rawJson = await apiResponse.Content.ReadAsStringAsync();

    try
    {
        var parsed = JsonSerializer.Deserialize<JsonElement>(rawJson);
        var prettyJson = JsonSerializer.Serialize(parsed, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        return Results.Content(prettyJson, "application/json");
    }
    catch
    {
        return Results.Content(rawJson, "application/json");
    }
})
.WithName("ScanPdf")
.WithSummary("Scan a PDF for a QR code and retrieve the associated policy data")
.DisableAntiforgery();

app.Run("http://localhost:5001");
