using SkiaSharp;
using Workspace.Core;

namespace Workspace.Tools;

public static class PolicyPdf
{
    public static byte[] Render(PolicyContent content)
    {
        using var stream = new MemoryStream();
        using var document = SKDocument.CreatePdf(stream);
        using var typeface = SKTypeface.FromFamilyName("Arial");
        using var normal = new SKFont(typeface, 11);
        using var heading = new SKFont(typeface, 16);
        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        var canvas = document.BeginPage(595, 842);
        var y = 55f;
        void Line(string text, SKFont font)
        {
            if (y > 785)
            {
                document.EndPage();
                canvas = document.BeginPage(595, 842);
                y = 55;
            }
            canvas.DrawText(text, 45, y, SKTextAlign.Left, font, ink);
            y += font.Size + 7;
        }
        void Paragraph(string text, SKFont font)
        {
            var line = "";
            foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var next = line.Length == 0 ? word : line + " " + word;
                if (font.MeasureText(next) > 505 && line.Length > 0) { Line(line, font); line = word; }
                else line = next;
            }
            if (line.Length > 0) Line(line, font);
            y += 8;
        }
        Paragraph(content.Title, heading);
        foreach (var section in content.Sections)
        {
            Paragraph(section.Heading, heading);
            Paragraph(section.Text, normal);
        }
        document.EndPage();
        document.Close();
        return stream.ToArray();
    }
}
