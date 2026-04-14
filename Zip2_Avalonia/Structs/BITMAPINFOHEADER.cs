using System.Runtime.InteropServices;

namespace Zip2.Structs;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BITMAPINFOHEADER
{
    public uint biSize;         // Размер структуры = 40
    public int biWidth;         // Ширина в пикселях
    public int biHeight;        // Высота в пикселях
    public ushort biPlanes;     // Должно быть 1
    public ushort biBitCount;   // Бит на пиксель
    public uint biCompression;  // Тип сжатия
    public uint biSizeImage;    // Размер изображения в байтах
    public int biXPelsPerMeter; // Горизонтальное разрешение
    public int biYPelsPerMeter; // Вертикальное разрешение
    public uint biClrUsed;      // Количество используемых цветов
    public uint biClrImportant; // Количество важных цветов
}
