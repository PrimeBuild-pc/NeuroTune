using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NeuroTune;

public sealed record SupportingAttachment(string Id, string Name, string Kind, string ContentType, string Content, string Sha256 = "");
public sealed record SupportingAttachmentInfo(string Id, string Name, string Kind, string ContentType, string Sha256, int Bytes);
public sealed record SupportingAttachmentsRequest(List<SupportingAttachment> Attachments);

public static class SupportingAttachments
{
    public const int MaxFiles = 8;
    public const int MaxImages = 4;
    public const int MaxReportCharacters = 40_000;
    public const int MaxCombinedReportCharacters = 80_000;
    public const int MaxImageBytes = 768 * 1024;
    public const int MaxImageSide = 1600;
    private static readonly HashSet<string> ReportExtensions = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".log", ".csv", ".json", ".xml", ".html", ".htm" };

    public static List<SupportingAttachment> Preview(SupportingAttachmentsRequest request)
    {
        if (MeasurementService.HasActiveRecording()) throw new InvalidOperationException("Supporting-file preparation is paused while recording.");
        return Normalize(request.Attachments, imagesConfirmed: true);
    }

    public static List<SupportingAttachment> Normalize(IReadOnlyList<SupportingAttachment>? attachments, bool imagesConfirmed = false)
    {
        attachments ??= [];
        if (attachments.Count > MaxFiles || attachments.Count(item => item?.Kind == "image") > MaxImages)
            throw new InvalidOperationException("Upload at most 8 supporting files, including at most 4 images.");
        var result = new List<SupportingAttachment>(); var characters = 0;
        foreach (var item in attachments)
        {
            if (item is null || !Guid.TryParseExact(item.Id, "D", out _) || result.Any(previous => previous.Id == item.Id) ||
                string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 120 || item.Name.Any(character => char.IsControl(character) || character is '/' or '\\' or ':') || item.Content is null)
                throw new InvalidOperationException("Invalid supporting attachment identity or filename.");
            var name = ProfileSanitizer.Redact(item.Name);
            byte[] bytes; string content;
            if (item.Kind == "report")
            {
                if (item.ContentType != "text/plain" || !ReportExtensions.Contains(Path.GetExtension(item.Name)) ||
                    item.Content.Length > MaxReportCharacters || item.Content.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
                    throw new InvalidOperationException("Reports must be plain-text exports (TXT/LOG/CSV/JSON/XML/HTML), at most 40,000 characters each. HTML is never rendered.");
                content = ProfileSanitizer.Redact(item.Content.Replace("\r\n", "\n").Replace('\r', '\n'));
                // Best-effort label-based omission, not a guarantee of anonymization; the user reviews the remaining plain text.
                content = Regex.Replace(content, @"^.*\b(serial|serialnumber|uuid|password|passwd|api[ _-]?key|token|licen[cs]e|product[ _-]?key|user[ _-]?name|host[ _-]?name|mac[ _-]?address|ip[ _-]?address)\b[^\n]*$",
                    "[Sensitive-labelled line omitted]", RegexOptions.Multiline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("The report contains no readable text.");
                characters += content.Length;
                if (characters > MaxCombinedReportCharacters) throw new InvalidOperationException("Combined reports exceed 80,000 characters. Select smaller excerpts; no silent truncation is applied.");
                bytes = new UTF8Encoding(false, true).GetBytes(content);
            }
            else if (item.Kind == "image")
            {
                if (!imagesConfirmed) throw new InvalidOperationException("Confirm image review and selected-model vision support before including screenshots. Images are never silently discarded or routed to another provider.");
                if (item.ContentType != "image/png" || item.Content.Length > ((MaxImageBytes + 2) / 3) * 4)
                    throw new InvalidOperationException("Screenshots must be normalized PNG images, at most 768 KiB each.");
                try { bytes = Convert.FromBase64String(item.Content); }
                catch (FormatException) { throw new InvalidOperationException("Invalid screenshot encoding."); }
                ValidatePng(bytes);
                content = Convert.ToBase64String(bytes);
            }
            else throw new InvalidOperationException("Unsupported attachment type. Executables, archives, PDFs and active documents are not imported.");
            result.Add(new(item.Id, name, item.Kind, item.ContentType, content, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
        }
        return result;
    }

    public static List<SupportingAttachmentInfo> Describe(IEnumerable<SupportingAttachment> attachments) => attachments.Select(item =>
        new SupportingAttachmentInfo(item.Id, item.Name, item.Kind, item.ContentType, item.Sha256,
            item.Kind == "image" ? Convert.FromBase64String(item.Content).Length : Encoding.UTF8.GetByteCount(item.Content))).ToList();

    public static IReadOnlyDictionary<string, string> Evidence(IEnumerable<SupportingAttachment> attachments)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in attachments)
        {
            var prefix = "support:" + item.Id;
            facts[prefix + ":provenance"] = JsonSerializer.Serialize(new { item.Name, item.Kind, item.Sha256, status = "User-supplied, unverified content; hash identifies the prepared payload, not authenticity/freshness or association with this PC. Screenshots are not automatically anonymized." });
            if (item.Kind == "report")
            {
                var part = 1;
                for (var offset = 0; offset < item.Content.Length;)
                {
                    var length = Math.Min(4000, item.Content.Length - offset);
                    if (offset + length < item.Content.Length && char.IsHighSurrogate(item.Content[offset + length - 1])) length--;
                    facts[$"{prefix}:text:{part++}"] = item.Content.Substring(offset, length);
                    offset += length;
                }
            }
        }
        return facts;
    }

    public static IReadOnlyDictionary<string, string> MergeEvidence(IReadOnlyDictionary<string, string> original, IEnumerable<SupportingAttachment> attachments)
    {
        var result = original.ToDictionary(fact => fact.Key, fact => fact.Value, StringComparer.Ordinal);
        foreach (var fact in Evidence(attachments)) if (!result.TryAdd(fact.Key, fact.Value)) throw new InvalidOperationException("Duplicate supporting evidence.");
        if (!LlmClient.MeasureEvidence(result).FitsSinglePass) throw new InvalidOperationException("Scan, measurement and supporting reports exceed the evidence budget. Reduce optional reports; mandatory evidence is never discarded.");
        return result;
    }

    internal static void ValidatePng(byte[] bytes)
    {
        if (bytes.Length is < 45 or > MaxImageBytes || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new InvalidOperationException("Invalid or oversized PNG screenshot.");
        var offset = 8; var header = false; var data = false; var chunks = 0;
        while (offset <= bytes.Length - 12 && ++chunks <= 4096)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length > bytes.Length - offset - 12) throw new InvalidOperationException("Malformed PNG chunk bounds.");
            var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            if (type == "IHDR")
            {
                if (header || offset != 8 || length != 13) throw new InvalidOperationException("Invalid PNG header.");
                var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8, 4));
                var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 12, 4));
                if (width is 0 or > MaxImageSide || height is 0 or > MaxImageSide || bytes[offset + 16] != 8 || bytes[offset + 17] is not (2 or 6) || bytes[offset + 18] != 0 || bytes[offset + 19] != 0 || bytes[offset + 20] != 0)
                    throw new InvalidOperationException("Screenshots must be noninterlaced RGB/RGBA PNG, at most 1600 pixels per side.");
                header = true;
            }
            else if (type == "IDAT") { if (!header) throw new InvalidOperationException("PNG data precedes its header."); data = true; }
            else if (type == "IEND")
            {
                if (!header || !data || length != 0 || offset + 12 != bytes.Length) throw new InvalidOperationException("Invalid PNG termination.");
                return;
            }
            else if (type is not ("sRGB" or "gAMA" or "cHRM" or "pHYs"))
                throw new InvalidOperationException("PNG metadata/active extensions are not accepted. Re-export through NeuroTune's screenshot picker.");
            offset += (int)length + 12;
        }
        throw new InvalidOperationException("Incomplete PNG screenshot.");
    }
}
