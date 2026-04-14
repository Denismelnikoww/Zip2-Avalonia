using System.IO;
using Zip2.Structs;

namespace Zip2.Services;

public class ImageProcessingService
{
    private readonly BmpWriterService _bmpWriter;

    public ImageProcessingService()
    {
        _bmpWriter = new BmpWriterService();
    }

    public string CreateOutputDirectory(string sourcePath)
    {
        string sourceDir = Path.GetDirectoryName(sourcePath);
        string fileName = Path.GetFileNameWithoutExtension(sourcePath);
        string outputDir = Path.Combine(sourceDir, $"{fileName}_BMP_results");

        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        return outputDir;
    }

    public void SplitIntoColorChannels(byte[] rgbData, int width, int height, string outputDir, string fileName, ColorChannelMode mode)
    {
        int totalPixels = width * height;

        byte[] red = new byte[totalPixels];
        byte[] green = new byte[totalPixels];
        byte[] blue = new byte[totalPixels];

        for (int i = 0; i < totalPixels; i++)
        {
            red[i] = rgbData[i * 3 + 2];
            green[i] = rgbData[i * 3 + 1];
            blue[i] = rgbData[i * 3 + 0];
        }

        string subDir = "";

        if (mode == ColorChannelMode.Grayscale || mode == ColorChannelMode.Both)
        {
            if (mode == ColorChannelMode.Both)
            {
                subDir = Path.Combine(outputDir, "Grayscale_Channels");
                Directory.CreateDirectory(subDir);
            }
            else
            {
                subDir = outputDir;
            }

            _bmpWriter.SaveGrayscaleBmp(red, width, height, Path.Combine(subDir, $"{fileName}_RED_grayscale.bmp"));
            _bmpWriter.SaveGrayscaleBmp(green, width, height, Path.Combine(subDir, $"{fileName}_GREEN_grayscale.bmp"));
            _bmpWriter.SaveGrayscaleBmp(blue, width, height, Path.Combine(subDir, $"{fileName}_BLUE_grayscale.bmp"));
        }

        // Вариант 2: Цветные каналы
        if (mode == ColorChannelMode.Colored || mode == ColorChannelMode.Both)
        {
            if (mode == ColorChannelMode.Both)
            {
                subDir = Path.Combine(outputDir, "Colored_Channels");
                Directory.CreateDirectory(subDir);
            }
            else
            {
                subDir = outputDir;
            }

            // Создаём цветные каналы
            byte[] redChannelRGB = new byte[totalPixels * 3];
            byte[] greenChannelRGB = new byte[totalPixels * 3];
            byte[] blueChannelRGB = new byte[totalPixels * 3];

            for (int i = 0; i < totalPixels; i++)
            {
                redChannelRGB[i * 3 + 2] = red[i];
                redChannelRGB[i * 3 + 1] = 0;
                redChannelRGB[i * 3 + 0] = 0;

                greenChannelRGB[i * 3 + 2] = 0;
                greenChannelRGB[i * 3 + 1] = green[i];
                greenChannelRGB[i * 3 + 0] = 0;

                blueChannelRGB[i * 3 + 2] = 0;
                blueChannelRGB[i * 3 + 1] = 0;
                blueChannelRGB[i * 3 + 0] = blue[i];
            }

            _bmpWriter.SaveBmp24Color(Path.Combine(subDir, $"{fileName}_RED_colored.bmp"), redChannelRGB, width, height);
            _bmpWriter.SaveBmp24Color(Path.Combine(subDir, $"{fileName}_GREEN_colored.bmp"), greenChannelRGB, width, height);
            _bmpWriter.SaveBmp24Color(Path.Combine(subDir, $"{fileName}_BLUE_colored.bmp"), blueChannelRGB, width, height);
        }
    }

    public void SplitIntoBitSlices(byte[] imageData, int width, int height, int bytesPerPixel, string outputDir, string fileName)
    {
        int totalPixels = width * height;

        // Создаём подпапку для битовых срезов
        string bitSlicesDir = Path.Combine(outputDir, "Bit_Slices");
        Directory.CreateDirectory(bitSlicesDir);

        if (bytesPerPixel == 3)
        {
            // Извлекаем каждый цветовой канал
            byte[] red = new byte[totalPixels];
            byte[] green = new byte[totalPixels];
            byte[] blue = new byte[totalPixels];

            for (int i = 0; i < totalPixels; i++)
            {
                red[i] = imageData[i * 3 + 2];
                green[i] = imageData[i * 3 + 1];
                blue[i] = imageData[i * 3 + 0];
            }

            ProcessBitSlicesForChannel(red, width, height, "RED", bitSlicesDir, fileName);
            ProcessBitSlicesForChannel(green, width, height, "GREEN", bitSlicesDir, fileName);
            ProcessBitSlicesForChannel(blue, width, height, "BLUE", bitSlicesDir, fileName);
        }
        else if (bytesPerPixel == 1)
        {
            ProcessBitSlicesForChannel(imageData, width, height, "GRAY", bitSlicesDir, fileName);
        }
        else
        {
            ProcessBitSlicesForChannel(imageData, width, height, "CHANNEL", bitSlicesDir, fileName);
        }
    }

    private void ProcessBitSlicesForChannel(byte[] channelData, int width, int height, string channelName, string outputDir, string fileName)
    {
        int totalPixels = width * height;

        // Создаём подпапку для канала
        string channelDir = Path.Combine(outputDir, channelName);
        Directory.CreateDirectory(channelDir);

        for (int bit = 0; bit < 8; bit++)
        {
            byte[] bitSlice = new byte[totalPixels];
            for (int i = 0; i < totalPixels; i++)
            {
                // Извлекаем значение бита (255 если бит установлен, иначе 0)
                bitSlice[i] = (byte)(((channelData[i] >> bit) & 1) * 255);
            }

            string outputPath = Path.Combine(channelDir, $"{fileName}_{channelName}_bit{bit}.bmp");
            _bmpWriter.SaveGrayscaleBmp(bitSlice, width, height, outputPath);
        }
    }
}
