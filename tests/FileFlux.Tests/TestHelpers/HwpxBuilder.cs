using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace FileFlux.Tests.TestHelpers;

/// <summary>
/// Writes a minimal HWPX (OWPML) document: one section of plain body paragraphs, no styles. Enough for the HWP reader to
/// parse, so HWPX behaviour can be tested without committing a document made in Hangul.
/// </summary>
internal static class HwpxBuilder
{
    public static void Write(string path, IEnumerable<string> paragraphs)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        // OWPML requires the mimetype entry first and stored uncompressed.
        Add(zip, "mimetype", "application/hwp+zip", CompressionLevel.NoCompression);
        Add(zip, "version.xml",
            """<?xml version="1.0" encoding="UTF-8"?><hv:HCFVersion xmlns:hv="http://www.hancom.co.kr/hwpml/2011/version" targetApplication="WORDPROCESSOR" major="5" minor="1" micro="0" buildNumber="0"/>""");
        Add(zip, "META-INF/container.xml",
            """<?xml version="1.0" encoding="UTF-8"?><ocf:container xmlns:ocf="urn:oasis:names:tc:opendocument:xmlns:container"><ocf:rootfiles><ocf:rootfile full-path="Contents/content.hpf" media-type="application/hwpml-package+xml"/></ocf:rootfiles></ocf:container>""");
        Add(zip, "Contents/content.hpf",
            """<?xml version="1.0" encoding="UTF-8"?><opf:package xmlns:opf="http://www.idpf.org/2007/opf/" version="" unique-identifier="" id=""><opf:metadata/><opf:manifest><opf:item id="header" href="Contents/header.xml" media-type="application/xml"/><opf:item id="section0" href="Contents/section0.xml" media-type="application/xml"/></opf:manifest><opf:spine><opf:itemref idref="header" linear="yes"/><opf:itemref idref="section0" linear="yes"/></opf:spine></opf:package>""");
        Add(zip, "Contents/header.xml",
            """<?xml version="1.0" encoding="UTF-8"?><hh:head xmlns:hh="http://www.hancom.co.kr/hwpml/2011/head" version="1.4" secCnt="1"/>""");

        var section = new StringBuilder(
            """<?xml version="1.0" encoding="UTF-8"?><hs:sec xmlns:hs="http://www.hancom.co.kr/hwpml/2011/section" xmlns:hp="http://www.hancom.co.kr/hwpml/2011/paragraph">""");
        var id = 0;
        foreach (var paragraph in paragraphs)
            section.Append(CultureInfo.InvariantCulture, $"""<hp:p id="{id++}" paraPrIDRef="0" styleIDRef="0"><hp:run charPrIDRef="0"><hp:t>{paragraph}</hp:t></hp:run></hp:p>""");
        section.Append("</hs:sec>");
        Add(zip, "Contents/section0.xml", section.ToString());
    }

    private static void Add(ZipArchive archive, string name, string content, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name, level).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
