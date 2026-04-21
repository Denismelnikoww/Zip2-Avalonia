using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Zip2.Services;

namespace Zip2.Steganography;

public class BenderSteganographyService
{
    private readonly BmpReaderService _bmpReader = new();
    private readonly BmpWriterService _bmpWriter = new();

    private const bool DEBUG_MODE = true;

    private void LogMessage(string message)
    {
        string logPath = Path.Combine(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory), "stego_debug.log");
        string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
        File.AppendAllText(logPath, logEntry);
    }

    // ================= VALIDATE =================
    public (bool hasSecret, int width, int height, string error)
        ValidateStegoImage(string stegoPath)
    {
        try
        {
            var (data, width, height, _) =
                _bmpReader.ReadImageData(stegoPath);

            var regions = FindTextureRegions(data, width, height, 16);

            if (regions.Count < 64)
                return (false, 0, 0, "Недостаточно блоков");

            byte[] header = new byte[8];
            int bitIndex = 0;

            // Извлекаем 64 бита заголовка
            for (int i = 0; i < header.Length; i++)
            {
                byte value = 0;
                for (int b = 7; b >= 0; b--)
                {
                    byte bit = ExtractBitsFromBlock(data, regions[bitIndex], width);
                    value |= (byte)((bit & 1) << b);
                    bitIndex++;
                }
                header[i] = value;

                if (DEBUG_MODE)
                    LogMessage($"Validate - Заголовок[{i}] = 0x{header[i]:X2}");
            }

            var (w, h) = DecodeHeader(header);

            if (DEBUG_MODE)
                LogMessage($"Validate - Извлеченный заголовок: ширина={w}, высота={h}, регионов={regions.Count}");

            if (w <= 0 || h <= 0 || w > width || h > height)
            {
                return (false, 0, 0, "Нет корректного заголовка");
            }

            // Проверяем, достаточно ли данных для извлеченных размеров
            int totalPixels = w * h;
            if (totalPixels > (regions.Count - 64))
            {
                return (false, 0, 0, "Недостаточно данных для извлеченных размеров");
            }

            return (true, w, h, "");
        }
        catch (Exception ex)
        {
            if (DEBUG_MODE)
                LogMessage($"Ошибка ValidateStegoImage: {ex.Message}");
            return (false, 0, 0, "Ошибка: " + ex.Message);
        }
    }

    // ================= EMBED =================
    public void EmbedSecretImage(
        string containerPath,
        string secretPath,
        string outputPath,
        int blockSize = 16)
    {
        var (container, width, height, _) =
            _bmpReader.ReadImageData(containerPath);

        var (secret, sw, sh, _) =
            _bmpReader.ReadImageData(secretPath);

        if (DEBUG_MODE)
            LogMessage($"Embed - контейнер {width}x{height}, секрет {sw}x{sh}");

        byte[] bits = ImageToBits(secret, sw, sh);
        int totalRequired = bits.Length + 64; // 64 для заголовка

        var regions = FindTextureRegions(container, width, height, blockSize);

        if (DEBUG_MODE)
            LogMessage($"Embed - требуются {totalRequired} регионов, доступно {regions.Count}");

        if (regions.Count < totalRequired)
            throw new Exception($"Недостаточно текстурных блоков: доступно {regions.Count}, требуется {totalRequired}");

        byte[] result = new byte[container.Length];
        Array.Copy(container, result, container.Length);

        // КОДИРУЕМ ЗАГОЛОВОК
        byte[] header = EncodeHeader(sw, sh);

        if (DEBUG_MODE)
        {
            LogMessage($"Embed - Кодируем заголовок: w={sw}, h={sh}");
            LogMessage($"Embed - Заголовок в байтах: {BitConverter.ToString(header)}");
        }

        int bitIndex = 0;

        // Встраиваем 64 бита заголовка
        for (int i = 0; i < header.Length; i++)
        {
            for (int b = 7; b >= 0; b--)
            {
                byte bit = (byte)((header[i] >> b) & 1);
                if (DEBUG_MODE && (i == 6 || i == 7)) // Отладка проблемных байтов
                    LogMessage($"Embed - встраиваем бит [{i * 8 + (7 - b)}] (байт {i}, бит {7 - b}): {bit} в регион ({regions[bitIndex].X},{regions[bitIndex].Y})");

                EmbedBitsInBlock(result, regions[bitIndex], bit, width);
                bitIndex++;
            }
        }

        // Встраиваем данные
        for (int i = 0; i < bits.Length; i++)
        {
            if (DEBUG_MODE && i < 5)
                LogMessage($"Embed - встраиваем бит данных [{i + 64}]: {bits[i]} в регион ({regions[bitIndex + i].X},{regions[bitIndex + i].Y})");

            EmbedBitsInBlock(result, regions[bitIndex + i], bits[i], width);
        }

        if (DEBUG_MODE)
        {
            // Проверка битов заголовка перед сохранением
            LogMessage("Проверка сохраненных битов заголовка ПЕРЕД СОХРАНЕНИЕМ:");
            for (int i = 0; i < 8; i++)
            {
                byte value = 0;
                for (int b = 7; b >= 0; b--)
                {
                    int regionIdx = i * 8 + (7 - b);
                    byte extracted = ExtractBitsFromBlock(result, regions[regionIdx], width);
                    value |= (byte)((extracted & 1) << b);
                }
                LogMessage($"  Байт [{i}] в памяти: 0x{value:X2} (ожидаем 0x{header[i]:X2})");
            }
        }

        _bmpWriter.SaveBmp24Color(outputPath, result, width, height);

        if (DEBUG_MODE)
        {
            // Проверка после сохранения
            var (afterSaveData, _, _, _) = _bmpReader.ReadImageData(outputPath);
            LogMessage("Проверка сохраненных битов заголовка ПОСЛЕ СОХРАНЕНИЯ:");
            for (int i = 0; i < 8; i++)
            {
                byte value = 0;
                for (int b = 7; b >= 0; b--)
                {
                    int regionIdx = i * 8 + (7 - b);
                    byte extracted = ExtractBitsFromBlock(afterSaveData, regions[regionIdx], width);
                    value |= (byte)((extracted & 1) << b);
                }
                LogMessage($"  Байт [{i}] после сохранения: 0x{value:X2} (ожидаем 0x{header[i]:X2})");
            }
        }
    }

    // ================= EXTRACT =================
    public void ExtractSecretImage(
        string stegoPath,
        string outputPath)
    {
        var (data, width, height, _) =
            _bmpReader.ReadImageData(stegoPath);

        var regions = FindTextureRegions(data, width, height, 16);

        if (DEBUG_MODE)
            LogMessage($"Extract - найдено {regions.Count} регионов");

        // Извлекаем 64 бита заголовка
        byte[] header = new byte[8];
        int bitIndex = 0;

        for (int i = 0; i < header.Length; i++)
        {
            byte value = 0;
            for (int b = 7; b >= 0; b--)
            {
                byte bit = ExtractBitsFromBlock(data, regions[bitIndex], width);
                value |= (byte)((bit & 1) << b);
                bitIndex++;
            }
            header[i] = value;

            if (DEBUG_MODE)
                LogMessage($"Extract - Извлечен байт заголовка [{i}]: 0x{header[i]:X2}");
        }

        var (sw, sh) = DecodeHeader(header);

        if (DEBUG_MODE)
            LogMessage($"Extract - Декодированный заголовок: ширина={sw}, высота={sh}");

        if (sw <= 0 || sh <= 0)
            throw new Exception($"Ошибка заголовка: w={sw}, h={sh}");

        int totalPixels = sw * sh;
        int availableDataBlocks = regions.Count - 64;

        if (DEBUG_MODE)
            LogMessage($"Extract - Требуется {totalPixels} пикселей, доступно {availableDataBlocks} блоков данных");

        if (totalPixels > availableDataBlocks)
            throw new Exception($"Недостаточно данных: требуется {totalPixels}, доступно {availableDataBlocks}");

        List<byte> bits = new();

        for (int i = 0; i < totalPixels; i++)
        {
            byte bit = ExtractBitsFromBlock(data, regions[i + 64], width);
            bits.Add(bit);
        }

        if (DEBUG_MODE)
            LogMessage($"Extract - Извлечено {bits.Count} бит данных");

        byte[] image = BitsToImage(bits.ToArray(), sw, sh);

        _bmpWriter.SaveBmp24Color(outputPath, image, sw, sh);

        if (DEBUG_MODE)
            LogMessage($"Extract - Сохранено изображение {sw}x{sh} в {outputPath}");
    }

    // ================= CORE =================

    private byte[] ImageToBits(byte[] imageData, int width, int height)
    {
        List<byte> bits = new();
        int totalPixels = width * height;

        for (int i = 0; i < totalPixels; i++)
        {
            int blueIndex = i * 3 + 2;

            if (blueIndex < imageData.Length)
            {
                byte lsb = (byte)(imageData[blueIndex] & 1);
                bits.Add(lsb);
            }
        }

        if (DEBUG_MODE)
            LogMessage($"ImageToBits - преобразовано {bits.Count} пикселей");

        return bits.ToArray();
    }

    private byte[] BitsToImage(byte[] bits, int width, int height)
    {
        byte[] image = new byte[width * height * 3];

        for (int i = 0; i < width * height && i < bits.Length; i++)
        {
            byte val = (byte)(bits[i] * 255);

            image[i * 3 + 0] = val; // R
            image[i * 3 + 1] = val; // G
            image[i * 3 + 2] = val; // B
        }

        return image;
    }

    // Используем сигнатуру для проверки подлинности заголовка
    private byte[] EncodeHeader(int w, int h)
    {
        byte[] data = new byte[8];

        // Сигнатура для проверки подлинности
        data[0] = 0xDE;
        data[1] = 0xAD;

        // Ограничим максимальные размеры для предотвращения переполнения
        if (w > 65535 || h > 65535)
            throw new ArgumentException("Размеры изображения слишком велики");

        // Ширина (2 байта)
        data[2] = (byte)((w >> 8) & 0xFF);
        data[3] = (byte)(w & 0xFF);

        // Высота (2 байта)
        data[4] = (byte)((h >> 8) & 0xFF);
        data[5] = (byte)(h & 0xFF);

        // Зарезервированные байты (для будущего использования)
        data[6] = 0x00;
        data[7] = 0x00;

        return data;
    }

    private (int, int) DecodeHeader(byte[] d)
    {
        if (d.Length < 8)
            throw new ArgumentException("Недостаточная длина заголовка");

        // Проверяем сигнатуру
        if (d[0] != 0xDE || d[1] != 0xAD)
            throw new InvalidOperationException("Неверная сигнатура заголовка");

        // Читаем ширину и высоту
        int w = (d[2] << 8) | d[3];
        int h = (d[4] << 8) | d[5];

        // Проверяем разумность значений
        if (w <= 0 || h <= 0)
            throw new InvalidOperationException("Неверные размеры в заголовке");

        return (w, h);
    }

    // НАСТОЯЩЕЕ РЕШЕНИЕ: СТАБИЛЬНАЯ СОРТИРОВКА

    private List<TextureRegion> FindTextureRegions(
        byte[] data, int width, int height, int size)
    {
        List<TextureRegion> regions = new();

        // 1. Строго фиксированный порядок обхода (ВАЖНО!)
        for (int y = 0; y <= height - size; y += size)
        {
            for (int x = 0; x <= width - size; x += size)
            {
                double var = CalculateVariance(data, x, y, size, width);

                // 2. variance используем только как фильтр
                if (var > 100)
                {
                    regions.Add(new TextureRegion
                    {
                        X = x,
                        Y = y,
                        Size = size,
                        Variance = var
                    });
                }
            }
        }
        return regions;
    }
    private double CalculateVariance(
        byte[] data, int sx, int sy, int size, int width)
    {
        long sum = 0, sumSq = 0;
        int count = 0;

        for (int y = sy; y < sy + size; y++)
        {
            for (int x = sx; x < sx + size; x++)
            {
                int idx = (y * width + x) * 3;

                byte g = (byte)(
                    (data[idx] + data[idx + 1] + data[idx + 2]) / 3);

                sum += g;
                sumSq += g * g;
                count++;
            }
        }

        double mean = (double)sum / count;
        return (double)sumSq / count - mean * mean;
    }

    private void EmbedBitsInBlock(
        byte[] data, TextureRegion r, byte value, int width)
    {
        int bit = value & 1;

        int idx = (r.Y * width + r.X) * 3;

        data[idx] = (byte)((data[idx] & 0xFE) | bit);
    }

    private byte ExtractBitsFromBlock(
        byte[] data, TextureRegion r, int width)
    {
        int idx = (r.Y * width + r.X) * 3;
        return (byte)(data[idx] & 1);
    }
}

