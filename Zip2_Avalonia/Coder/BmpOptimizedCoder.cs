using System;
using Zip2.Services;
using Zip2.Structs;
using System.IO;

namespace Zip2_Avalonia.Coder;

public class BmpOptimizedCoder
{
    private void LogMessage(string message)
    {
        string logPath = Path.Combine(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory), "stego_debug.log");
        string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
        File.AppendAllText(logPath, logEntry);
    }

    private readonly BmpWriterService _bmpWriter = new BmpWriterService();
    private readonly BmpReaderService _bmpReader = new BmpReaderService();

    public void Encode(string inputPath, Stream output)
    {
        var (fileHeader, infoHeader) = _bmpReader.ReadHeaders(inputPath);
        var (imageData, width, height, bytesPerPixel) = _bmpReader.ReadImageData(inputPath);

        SaveHeaders(fileHeader, infoHeader, output);

        int pixelCount = width * height;

        byte[] redChannel = new byte[pixelCount];
        byte[] greenChannel = new byte[pixelCount];
        byte[] blueChannel = new byte[pixelCount];

        for (int i = 0; i < pixelCount; i++)
        {
            blueChannel[i] = imageData[i * 3];
            greenChannel[i] = imageData[i * 3 + 1];
            redChannel[i] = imageData[i * 3 + 2];
        }

        byte[] redDpcm = ApplyDPCM(redChannel);
        byte[] greenDpcm = ApplyDPCM(greenChannel);
        byte[] blueDpcm = ApplyDPCM(blueChannel);

        EncodeChannelWithRle(redDpcm, output);
        EncodeChannelWithRle(greenDpcm, output);
        EncodeChannelWithRle(blueDpcm, output);
    }

    public void Decode(Stream input, string outputPath)
    {
        var (fileHeader, infoHeader) = LoadHeaders(input);


        int pixelCount = infoHeader.biWidth * Math.Abs(infoHeader.biHeight);

        byte[] redDpcm = DecodeChannelWithRle(input);
        byte[] greenDpcm = DecodeChannelWithRle(input);
        byte[] blueDpcm = DecodeChannelWithRle(input);

        if (redDpcm.Length != pixelCount || greenDpcm.Length != pixelCount || blueDpcm.Length != pixelCount)
        {
            throw new Exception(
                $"Размеры каналов не совпадают! Ожидалось: {pixelCount}\n" +
                $"Red: {redDpcm.Length}, Green: {greenDpcm.Length}, Blue: {blueDpcm.Length}");
        }

        byte[] redChannel = InverseDPCM(redDpcm);
        byte[] greenChannel = InverseDPCM(greenDpcm);
        byte[] blueChannel = InverseDPCM(blueDpcm);

        if (redChannel.Length != pixelCount || greenChannel.Length != pixelCount || blueChannel.Length != pixelCount)
        {
            throw new Exception(
                $"Размеры каналов после DPCM не совпадают!\n" +
                $"Red: {redChannel.Length}, Green: {greenChannel.Length}, Blue: {blueChannel.Length}");
        }

        byte[] imageData = new byte[pixelCount * 3];
        for (int i = 0; i < pixelCount; i++)
        {
            imageData[i * 3] = blueChannel[i];
            imageData[i * 3 + 1] = greenChannel[i];
            imageData[i * 3 + 2] = redChannel[i];
        }

        _bmpWriter.SaveBmp24Color(outputPath, imageData, infoHeader.biWidth, Math.Abs(infoHeader.biHeight));
    }

    private void SaveHeaders(BITMAPFILEHEADER fileHeader, BITMAPINFOHEADER infoHeader, Stream output)
    {
        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);

        writer.Write(fileHeader.bfType);
        writer.Write(fileHeader.bfSize);
        writer.Write(fileHeader.bfReserved1);
        writer.Write(fileHeader.bfReserved2);
        writer.Write(fileHeader.bfOffBits);

        writer.Write(infoHeader.biSize);
        writer.Write(infoHeader.biWidth);
        writer.Write(infoHeader.biHeight);
        writer.Write(infoHeader.biPlanes);
        writer.Write(infoHeader.biBitCount);
        writer.Write(infoHeader.biCompression);
        writer.Write(infoHeader.biSizeImage);
        writer.Write(infoHeader.biXPelsPerMeter);
        writer.Write(infoHeader.biYPelsPerMeter);
        writer.Write(infoHeader.biClrUsed);
        writer.Write(infoHeader.biClrImportant);
    }

    private (BITMAPFILEHEADER, BITMAPINFOHEADER) LoadHeaders(Stream input)
    {

        long startPos = input.Position;

        using var reader = new BinaryReader(input, System.Text.Encoding.UTF8, leaveOpen: true);
        LogMessage($"[LoadHeaders] Начальная позиция: {startPos}");

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

        long endPos = input.Position;
        LogMessage($"[LoadHeaders] Прочитано: {endPos - startPos} байт, новая позиция: {endPos}");
        return (fileHeader, infoHeader);
    }


    private byte[] ApplyDPCM(byte[] channelData)
    {
        if (channelData.Length == 0)
            return channelData;

        byte[] result = new byte[channelData.Length];
        result[0] = channelData[0];

        for (int i = 1; i < channelData.Length; i++)
        {
            int diff = channelData[i] - channelData[i - 1];
            result[i] = (byte)(diff + 128);
        }

        return result;
    }

    private byte[] InverseDPCM(byte[] dpcmData)
    {
        if (dpcmData.Length == 0)
            return dpcmData;

        byte[] result = new byte[dpcmData.Length];
        result[0] = dpcmData[0];

        for (int i = 1; i < dpcmData.Length; i++)
        {
            int diff = dpcmData[i] - 128;
            result[i] = (byte)(result[i - 1] + diff);
        }

        return result;
    }

    private void EncodeChannelWithRle(byte[] channelData, Stream output)
    {
        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);

        writer.Write(channelData.Length);

        using var ms = new MemoryStream();
        using var src = new MemoryStream(channelData);
        RleCoder.Encode(src, ms);
        byte[] rle = ms.ToArray();

        writer.Write(rle.Length);

        output.Write(rle);
    }


    private byte[] DecodeChannelWithRle(Stream input)
    {
        using var reader = new BinaryReader(input, System.Text.Encoding.UTF8, leaveOpen: true);

        int expectedDecodedLength = reader.ReadInt32();
        int rleBlockSize = reader.ReadInt32();

        if (expectedDecodedLength <= 0 || expectedDecodedLength > 100_000_000)
            throw new Exception($"Некорректная длина канала: {expectedDecodedLength}");
        if (rleBlockSize <= 0 || rleBlockSize > 10_000_000)
            throw new Exception($"Некорректный размер RLE-блока: {rleBlockSize}");

        byte[] rleData = new byte[rleBlockSize];
        int totalRead = 0;
        while (totalRead < rleBlockSize)
        {
            int read = input.Read(rleData, totalRead, rleBlockSize - totalRead);
            if (read == 0) throw new EndOfStreamException("Не хватает RLE-данных для канала");
            totalRead += read;
        }

        using var rleStream = new MemoryStream(rleData);
        using var output = new MemoryStream();
        RleCoder.Decode(rleStream, output);
        byte[] result = output.ToArray();

        if (result.Length != expectedDecodedLength)
        {
            if (result.Length > expectedDecodedLength)
                Array.Resize(ref result, expectedDecodedLength);
            else
                Array.Resize(ref result, expectedDecodedLength);
        }

        return result;
    }
}
