using System;
using System.IO;
using Zip2.Structs;

namespace Zip2.Services;

public class BmpReaderService
{
    public (BITMAPFILEHEADER fileHeader, BITMAPINFOHEADER infoHeader) ReadHeaders(string filePath)
    {
        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        using (BinaryReader reader = new BinaryReader(fs))
        {
            BITMAPFILEHEADER fileHeader = new BITMAPFILEHEADER
            {
                bfType = reader.ReadUInt16(),
                bfSize = reader.ReadUInt32(),
                bfReserved1 = reader.ReadUInt16(),
                bfReserved2 = reader.ReadUInt16(),
                bfOffBits = reader.ReadUInt32()
            };

            BITMAPINFOHEADER infoHeader = new BITMAPINFOHEADER
            {
                biSize = reader.ReadUInt32(),
                biWidth = reader.ReadInt32(),
                biHeight = reader.ReadInt32(),
                biPlanes = reader.ReadUInt16(),
                biBitCount = reader.ReadUInt16(),
                biCompression = reader.ReadUInt32(),
                biSizeImage = reader.ReadUInt32(),
                biXPelsPerMeter = reader.ReadInt32(),
                biYPelsPerMeter = reader.ReadInt32(),
                biClrUsed = reader.ReadUInt32(),
                biClrImportant = reader.ReadUInt32()
            };

            return (fileHeader, infoHeader);
        }
    }

    public (byte[] imageData, int width, int height, int bytesPerPixel) ReadImageData(string filePath)
    {
        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        using (BinaryReader reader = new BinaryReader(fs))
        {
            reader.ReadUInt16(); // bfType
            reader.ReadUInt32(); // bfSize
            reader.ReadUInt16(); // bfReserved1
            reader.ReadUInt16(); // bfReserved2
            uint bfOffBits = reader.ReadUInt32(); // bfOffBits

            // Читаем BITMAPINFOHEADER
            uint biSize = reader.ReadUInt32();
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            reader.ReadUInt16(); // biPlanes
            ushort biBitCount = reader.ReadUInt16();
            uint biCompression = reader.ReadUInt32();
            uint biSizeImage = reader.ReadUInt32();
            reader.ReadInt32(); // biXPelsPerMeter
            reader.ReadInt32(); // biYPelsPerMeter
            reader.ReadUInt32(); // biClrUsed
            reader.ReadUInt32(); // biClrImportant

            if (biBitCount <= 8)
            {
                int paletteSize = (biBitCount == 1 ? 2 : (biBitCount == 4 ? 16 : 256)) * 4;
                reader.ReadBytes(paletteSize);
            }
            else if (biBitCount == 16)
            {
                reader.ReadBytes(12); 
            }

            int absHeight = Math.Abs(height);
            int rowSize = ((width * biBitCount + 31) / 32) * 4;
            int imageSize = biSizeImage > 0 ? (int)biSizeImage : rowSize * absHeight;

            reader.BaseStream.Seek(bfOffBits, SeekOrigin.Begin);
            byte[] rawData = reader.ReadBytes(imageSize);

            int bytesPerPixel = biBitCount / 8;
            byte[] imageData;

            if (biBitCount == 24)
            {
                imageData = new byte[width * absHeight * 3];
                int destIndex = 0;

                for (int y = 0; y < absHeight; y++)
                {
                    int rowSrc = (height > 0) ? (absHeight - 1 - y) * rowSize : y * rowSize;
                    for (int x = 0; x < width; x++)
                    {
                        imageData[destIndex + 2] = rawData[rowSrc + x * 3 + 2]; 
                        imageData[destIndex + 1] = rawData[rowSrc + x * 3 + 1]; 
                        imageData[destIndex + 0] = rawData[rowSrc + x * 3 + 0]; 
                        destIndex += 3;
                    }
                }
            }
            else
            {
                imageData = rawData;
            }

            return (imageData, width, absHeight, bytesPerPixel);
        }
    }
}
