using System.Runtime.InteropServices;

namespace Zip2.Structs;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BITMAPFILEHEADER
{
    public ushort bfType;      // 0x4D42
    public uint bfSize;        // Размер файла в байтах
    public ushort bfReserved1; // 0
    public ushort bfReserved2; // 0
    public uint bfOffBits;     // Смещение до данных изображения
}
