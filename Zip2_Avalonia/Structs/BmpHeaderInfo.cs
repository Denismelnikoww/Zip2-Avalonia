using System;

namespace Zip2.Structs;

public class BmpHeaderInfo
{
    public BITMAPFILEHEADER FileHeader { get; set; }
    public BITMAPINFOHEADER InfoHeader { get; set; }

    public int TotalPixels => Math.Abs(InfoHeader.biWidth) * Math.Abs(InfoHeader.biHeight);
    public string ImageType => InfoHeader.biBitCount <= 8 ? "Используется палитра" : "Палитра не используется (TrueColor)";
    public string RowOrder => InfoHeader.biHeight < 0 ? "сверху-вниз" : "снизу-вверх";
    public bool IsCompressed => InfoHeader.biCompression != 0;
}
