namespace MetSaver;

/// <summary>Reads pixel dimensions from the first bytes of a JPEG or PNG without decoding it.</summary>
public static class ImageHeader
{
    public static bool TryGetSize(ReadOnlySpan<byte> d, out int width, out int height)
    {
        width = height = 0;
        if (d.Length >= 24 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47)  // PNG IHDR
        {
            width = d[16] << 24 | d[17] << 16 | d[18] << 8 | d[19];
            height = d[20] << 24 | d[21] << 16 | d[22] << 8 | d[23];
            return width > 0 && height > 0;
        }
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) return false;

        int i = 2;
        while (i + 4 <= d.Length)
        {
            if (d[i] != 0xFF) { i++; continue; }
            byte marker = d[i + 1];
            if (marker == 0xFF) { i++; continue; }                          // fill byte
            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; }
            int len = d[i + 2] << 8 | d[i + 3];
            bool sof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (sof)
            {
                if (i + 9 > d.Length) return false;                         // need more bytes
                height = d[i + 5] << 8 | d[i + 6];
                width = d[i + 7] << 8 | d[i + 8];
                return width > 0 && height > 0;
            }
            i += 2 + len;
        }
        return false;
    }
}
