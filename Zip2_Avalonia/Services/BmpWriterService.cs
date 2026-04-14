namespace Zip2.Services;

using System.IO;

public class BmpWriterService
{
    /// <summary>
    /// Сохранение полутонового BMP
    /// </summary>
    public void SaveGrayscaleBmp(byte[] grayscaleData, int width, int height, string outputPath)
    {
        int rowSize = (width * 24 + 31) / 32 * 4;
        int imageSize = rowSize * height;
        int fileSize = 14 + 40 + imageSize;

        using (FileStream fs = new FileStream(outputPath, FileMode.Create))
        using (BinaryWriter writer = new BinaryWriter(fs))
        {
            // BITMAPFILEHEADER
            writer.Write((ushort)0x4D42);
            writer.Write((uint)fileSize);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((uint)54);

            // BITMAPINFOHEADER
            writer.Write((uint)40);
            writer.Write((int)width);
            writer.Write((int)height);
            writer.Write((ushort)1);
            writer.Write((ushort)24);
            writer.Write((uint)0);
            writer.Write((uint)imageSize);
            writer.Write((int)3780);
            writer.Write((int)3780);
            writer.Write((uint)0);
            writer.Write((uint)0);

            byte[] row = new byte[rowSize];
            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    byte val = grayscaleData[y * width + x];
                    row[x * 3] = val;     // B
                    row[x * 3 + 1] = val; // G
                    row[x * 3 + 2] = val; // R
                }
                writer.Write(row);
            }
        }
    }

    /// <summary>
    /// Сохранение цветного BMP
    /// </summary>
    public void SaveBmp24Color(string outputPath, byte[] rgbData, int width, int height)
    {
        int rowSize = (width * 24 + 31) / 32 * 4;
        int imageSize = rowSize * height;
        int fileSize = 14 + 40 + imageSize;

        using (FileStream fs = new FileStream(outputPath, FileMode.Create))
        using (BinaryWriter writer = new BinaryWriter(fs))
        {
            // BITMAPFILEHEADER
            writer.Write((ushort)0x4D42);
            writer.Write((uint)fileSize);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((uint)54);

            // BITMAPINFOHEADER
            writer.Write((uint)40);
            writer.Write((int)width);
            writer.Write((int)height);
            writer.Write((ushort)1);
            writer.Write((ushort)24);
            writer.Write((uint)0);
            writer.Write((uint)imageSize);
            writer.Write((int)3780);
            writer.Write((int)3780);
            writer.Write((uint)0);
            writer.Write((uint)0);


            byte[] row = new byte[rowSize];
            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = (y * width + x) * 3;
                    row[x * 3] = rgbData[idx];      // B
                    row[x * 3 + 1] = rgbData[idx + 1]; // G
                    row[x * 3 + 2] = rgbData[idx + 2]; // R
                }
                writer.Write(row);
            }
        }
    }
}
