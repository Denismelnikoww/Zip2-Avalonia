using System;
using System.IO;
using System.Collections.Generic;
using Zip2.Services;

namespace Zip2.Steganography;

/// <summary>
/// Стеганография методом Бендера (Bender)
/// Метод основан на копировании блоков из случайно выбранной текстурной области 
/// в другую область с похожими статистическими характеристиками.
/// Это создаёт повторяющиеся блоки, обнаруживаемые по автокорреляции.
/// </summary>
public class BenderSteganographyService
{
    private readonly BmpReaderService _bmpReader;
    private readonly BmpWriterService _bmpWriter;

    public BenderSteganographyService()
    {
        _bmpReader = new BmpReaderService();
        _bmpWriter = new BmpWriterService();
    }

    /// <summary>
    /// Проверяет, содержит ли изображение скрытый секрет
    /// </summary>
    public (bool hasSecret, int width, int height, string error) ValidateStegoImage(string stegoPath)
    {
        try
        {
            var (stegoData, width, height, bpp) = _bmpReader.ReadImageData(stegoPath);

            // Проверяем что это 24-битное изображение
            if (bpp != 3)
            {
                return (false, 0, 0, "Изображение не 24-битное");
            }

            // Извлекаем LSB
            byte[] stegoLsb = ExtractLSB(stegoData, width, height, bpp);

            // Пробуем извлечь размеры
            var (secretWidth, secretHeight) = DecodeDimensions(stegoLsb);

            // Валидация размеров - если нулевые или отрицательные, значит нет секрета
            if (secretWidth <= 0 || secretHeight <= 0)
            {
                return (false, 0, 0, "Секрет не найден");
            }

            // Проверяем что размеры разумные (не больше контейнера)
            if (secretWidth > width || secretHeight > height)
            {
                return (false, 0, 0, "Секрет не найден (некорректные размеры)");
            }

            int containerPixels = width * height;
            int headerPixels = 22;
            int maxPossiblePixels = (containerPixels - headerPixels);
            
            if (secretWidth * secretHeight > maxPossiblePixels)
            {
                return (false, 0, 0, "Секрет не найден");
            }

            return (true, secretWidth, secretHeight, "");
        }
        catch
        {
            return (false, 0, 0, "Секрет не найден");
        }
    }

    /// <summary>
    /// Извлекает младший битовый слой (LSB) из изображения
    /// </summary>
    public byte[] ExtractLSB(byte[] imageData, int width, int height, int bytesPerPixel)
    {
        int totalPixels = width * height;
        byte[] lsbData = new byte[totalPixels];

        if (bytesPerPixel == 3)
        {
            // 24-битное изображение - извлекаем LSB из каждого цветового канала
            for (int i = 0; i < totalPixels; i++)
            {
                // LSB каждого канала: R, G, B
                byte r = (byte)(imageData[i * 3 + 2] & 1);
                byte g = (byte)(imageData[i * 3 + 1] & 1);
                byte b = (byte)(imageData[i * 3 + 0] & 1);
                
                // Упаковываем 3 бита в один байт (по сути это наш контейнер)
                lsbData[i] = (byte)((r << 2) | (g << 1) | b);
            }
        }
        else if (bytesPerPixel == 1)
        {
            // 8-битное изображение
            for (int i = 0; i < totalPixels; i++)
            {
                lsbData[i] = (byte)(imageData[i] & 1);
            }
        }

        return lsbData;
    }

    /// <summary>
    /// Встраивает секретное изображение в LSB контейнера методом Бендера
    /// </summary>
    public void EmbedSecretImage(
        string containerPath, 
        string secretImagePath, 
        string outputPath,
        int blockSize = 8)
    {
        // Читаем контейнер
        var (containerData, containerWidth, containerHeight, containerBpp) = 
            _bmpReader.ReadImageData(containerPath);
        
        // Читаем секретное изображение
        var (secretData, secretWidth, secretHeight, secretBpp) = 
            _bmpReader.ReadImageData(secretImagePath);

        // Рассчитываем ёмкость контейнера
        // LSB: 3 бита на пиксель (по 1 биту на R, G, B)
        // Первые 22 пикселя (66 бит) используются для заголовка
        int containerPixels = containerWidth * containerHeight;
        int headerPixels = 22;
        int availablePixels = containerPixels - headerPixels;
        int availableBits = availablePixels * 3; // 3 бита на пиксель
        
        // Секретное изображение: 1 байт на пиксель ( grayscale )
        int secretPixels = secretWidth * secretHeight;
        int secretBitsRequired = secretPixels * 8; // 8 бит на пиксель
        
        // Проверяем достаточно ли места
        if (secretBitsRequired > availableBits)
        {
            throw new Exception(
                $"Контейнер слишком мал для секретного изображения!\n" +
                $"Доступно: {availableBits / 8} байт ({availableBits} бит)\n" +
                $"Требуется: {secretBitsRequired / 8} байт ({secretBitsRequired} бит)\n" +
                $"Размер контейнера: {containerWidth}x{containerHeight} = {containerPixels} пикселей\n" +
                $"Размер секрета: {secretWidth}x{secretHeight} = {secretPixels} пикселей");
        }

        // Проверяем что контейнер 24-битный
        if (containerBpp != 3)
        {
            throw new Exception("Контейнер должен быть 24-битным BMP изображением");
        }

        // Создаём копию контейнера для модификации
        byte[] result = new byte[containerData.Length];
        Array.Copy(containerData, result, containerData.Length);

        // Извлекаем LSB контейнера
        byte[] containerLsb = ExtractLSB(containerData, containerWidth, containerHeight, containerBpp);

        // Кодируем размер секретного изображения в первых пикселях
        EncodeDimensions(containerLsb, secretWidth, secretHeight);

        // Кодируем данные секретного изображения в LSB
        EncodeSecretData(containerLsb, secretData, secretWidth, secretHeight, containerWidth);

        // Встраиваем модифицированный LSB обратно в изображение
        EmbedLSB(result, containerLsb, containerWidth, containerHeight, containerBpp);

        // Сохраняем результат
        _bmpWriter.SaveBmp24Color(outputPath, result, containerWidth, containerHeight);
    }

    /// <summary>
    /// Извлекает секретное изображение из стегоизображения
    /// </summary>
    public void ExtractSecretImage(
        string stegoImagePath, 
        string outputPath)
    {
        // Читаем стегоизображение
        var (stegoData, width, height, bpp) = 
            _bmpReader.ReadImageData(stegoImagePath);

        // Проверяем что это 24-битное изображение
        if (bpp != 3)
        {
            throw new Exception("Стегоизображение должно быть 24-битным BMP");
        }

        // Извлекаем LSB
        byte[] stegoLsb = ExtractLSB(stegoData, width, height, bpp);

        // Извлекаем размеры секретного изображения
        var (secretWidth, secretHeight) = DecodeDimensions(stegoLsb);

        // Валидация размеров — защита от переполнения
        int containerPixels = width * height;
        int headerPixels = 22;
        int maxPossiblePixels = (containerPixels - headerPixels);
        
        // Более мягкая проверка - если размеры некорректные, значит нет секрета
        if (secretWidth <= 0 || secretHeight <= 0)
        {
            throw new Exception("В изображении не найден скрытый секрет");
        }
        
        if (secretWidth > width || secretHeight > height)
        {
            throw new Exception(
                $"Размеры секрета превышают размеры контейнера!\n" +
                $"Секрет: {secretWidth}x{secretHeight}\n" +
                $"Контейнер: {width}x{height}");
        }
        
        if (secretWidth * secretHeight > maxPossiblePixels)
        {
            throw new Exception(
                $"Секретное изображение слишком большое для этого контейнера!\n" +
                $"Максимум: {maxPossiblePixels} пикселей\n" +
                $"Секрет: {secretWidth * secretHeight} пикселей");
        }

        // Извлекаем данные секретного изображения
        byte[] secretData = DecodeSecretData(stegoLsb, secretWidth, secretHeight, width);

        // Создаём RGB данные для секретного изображения (уже в RGB из DecodeSecretData)
        // Сохраняем
        _bmpWriter.SaveBmp24Color(outputPath, secretData, secretWidth, secretHeight);
    }

    /// <summary>
    /// Метод Бендера: копирование блоков между текстурными областями
    /// </summary>
    public void EmbedWithBlockCopying(
        string containerPath,
        string secretPath,
        string outputPath,
        int blockSize = 16)
    {
        // Читаем контейнер
        var (containerData, containerWidth, containerHeight, containerBpp) = 
            _bmpReader.ReadImageData(containerPath);

        // Читаем секретное изображение и преобразуем в битовый поток
        var (secretData, secretWidth, secretHeight, _) = 
            _bmpReader.ReadImageData(secretPath);

        // Создаём битовый поток из секретного изображения
        byte[] secretBits = ImageToBits(secretData, secretWidth * secretHeight);

        // Копируем контейнер
        byte[] result = new byte[containerData.Length];
        Array.Copy(containerData, result, containerData.Length);

        // Находим текстурные области
        List<TextureRegion> textureRegions = FindTextureRegions(
            containerData, containerWidth, containerHeight, blockSize);

        if (textureRegions.Count < 2)
        {
            throw new Exception("Недостаточно текстурных областей для стеганографии");
        }

        // Кодируем заголовок (размеры изображения)
        byte[] header = EncodeHeader(secretWidth, secretHeight);
        int bitIndex = 0;

        // Встраиваем заголовок в первые блоки
        for (int i = 0; i < header.Length && i < textureRegions.Count; i++)
        {
            EmbedBitsInBlock(result, textureRegions[i], header[i], containerWidth);
            bitIndex += 8;
        }

        // Встраиваем данные изображения в блоки
        int regionIndex = header.Length;
        for (int i = 0; i < secretBits.Length && regionIndex < textureRegions.Count; i++)
        {
            // Копируем блок из одной текстурной области в другую
            // Это создаёт повторяющиеся паттерны
            TextureRegion source = textureRegions[regionIndex % textureRegions.Count];
            TextureRegion dest = textureRegions[(regionIndex + 1) % textureRegions.Count];
            
            CopyBlockWithModification(result, source, dest, secretBits[i], containerWidth);
            regionIndex++;
        }

        // Сохраняем результат
        _bmpWriter.SaveBmp24Color(outputPath, result, containerWidth, containerHeight);
    }

    /// <summary>
    /// Извлечение данных методом Бендера
    /// </summary>
    public void ExtractWithBlockCopying(
        string stegoPath,
        string outputPath)
    {
        var (stegoData, width, height, _) = _bmpReader.ReadImageData(stegoPath);

        // Находим текстурные области
        List<TextureRegion> textureRegions = FindTextureRegions(stegoData, width, height, 16);

        // Извлекаем заголовок
        byte[] header = new byte[8];
        for (int i = 0; i < header.Length && i < textureRegions.Count; i++)
        {
            header[i] = ExtractBitsFromBlock(stegoData, textureRegions[i], width);
        }

        var (secretWidth, secretHeight) = DecodeHeader(header);

        // Защита от переполнения и некорректных данных
        if (secretWidth <= 0 || secretHeight <= 0)
        {
            throw new Exception("Неверные размеры в заголовке стегоизображения");
        }
        
        // Ограничиваем максимальное количество извлекаемых пикселей
        int maxPixels = width * height;
        long requiredPixels = (long)secretWidth * secretHeight;
        if (requiredPixels > maxPixels)
        {
            throw new Exception($"Размеры секрета превышают размеры контейнера: {secretWidth}x{secretHeight}");
        }
        
        // Ограничиваем также от переполнения при умножении
        int safeMaxPixels = Math.Min(textureRegions.Count - 8, maxPixels);
        if (requiredPixels > safeMaxPixels)
        {
            throw new Exception($"Слишком много пикселей для извлечения: {requiredPixels} > {safeMaxPixels}");
        }

        // Извлекаем данные
        List<byte> secretBits = new List<byte>();
        int targetPixels = secretWidth * secretHeight;
        for (int i = header.Length; i < textureRegions.Count && secretBits.Count < targetPixels; i++)
        {
            TextureRegion source = textureRegions[i % textureRegions.Count];
            TextureRegion dest = textureRegions[(i + 1) % textureRegions.Count];
            
            byte bit = ExtractModification(stegoData, source, dest, width);
            secretBits.Add(bit);
        }

        // Преобразуем биты в изображение
        byte[] secretRgb = BitsToImage(secretBits.ToArray(), secretWidth, secretHeight);
        _bmpWriter.SaveBmp24Color(outputPath, secretRgb, secretWidth, secretHeight);
    }

    #region Private Methods

    private void EncodeDimensions(byte[] lsb, int width, int height)
    {
        // Кодируем 64 бита (8 байт) в первые 22 пикселя LSB
        // Каждый пиксель LSB содержит 3 бита
        // 64 бита / 3 бита = 21.33 пикселя, округляем до 22
        
        byte[] widthBytes = new byte[] {
            (byte)((width >> 24) & 0xFF),
            (byte)((width >> 16) & 0xFF),
            (byte)((width >> 8) & 0xFF),
            (byte)(width & 0xFF)
        };
        
        byte[] heightBytes = new byte[] {
            (byte)((height >> 24) & 0xFF),
            (byte)((height >> 16) & 0xFF),
            (byte)((height >> 8) & 0xFF),
            (byte)(height & 0xFF)
        };
        
        byte[] allBytes = new byte[8];
        Array.Copy(widthBytes, 0, allBytes, 0, 4);
        Array.Copy(heightBytes, 0, allBytes, 4, 4);
        
        // Кодируем 8 байт (64 бита) в пиксели LSB
        int bitIndex = 0;
        for (int i = 0; i < 22 && i < lsb.Length; i++)
        {
            byte bits = 0;
            for (int j = 0; j < 3; j++)
            {
                if (bitIndex < 64)
                {
                    int byteIndex = bitIndex / 8;
                    int bitPos = 7 - (bitIndex % 8);
                    int bit = (allBytes[byteIndex] >> bitPos) & 1;
                    bits = (byte)((bits << 1) | bit);
                }
                bitIndex++;
            }
            lsb[i] = (byte)((lsb[i] & 0xF8) | bits);
        }
    }

    private (int width, int height) DecodeDimensions(byte[] lsb)
    {
        // Декодируем 64 бита из первых 22 пикселей LSB
        byte[] allBytes = new byte[8];
        int bitIndex = 0;
        
        for (int i = 0; i < 22 && i < lsb.Length; i++)
        {
            // Извлекаем 3 бита из пикселя
            byte bits = (byte)(lsb[i] & 7);
            
            for (int j = 0; j < 3; j++)
            {
                if (bitIndex < 64)
                {
                    int byteIndex = bitIndex / 8;
                    int bitPos = 7 - (bitIndex % 8);
                    int bit = (bits >> (2 - j)) & 1;
                    if (bit == 1)
                    {
                        allBytes[byteIndex] = (byte)(allBytes[byteIndex] | (1 << bitPos));
                    }
                }
                bitIndex++;
            }
        }
        
        int width = (allBytes[0] << 24) | (allBytes[1] << 16) | (allBytes[2] << 8) | allBytes[3];
        int height = (allBytes[4] << 24) | (allBytes[5] << 16) | (allBytes[6] << 8) | allBytes[7];
        
        return (width, height);
    }

    private void EncodeSecretData(byte[] lsb, byte[] secretData, int secretWidth, int secretHeight, int containerWidth)
    {
        int offset = 22; // Пропускаем заголовок (22 пикселя по 3 бита = 66 бит ≈ 64 бита)
        
        int secretPixels = secretWidth * secretHeight;
        
        // Защита от пустых данных
        if (secretPixels <= 0 || secretData.Length == 0)
        {
            return;
        }
        
        // Определяем количество байт на пиксель в секретном изображении
        int secretBytesPerPixel = secretData.Length / secretPixels;
        if (secretBytesPerPixel < 1) secretBytesPerPixel = 1;
        
        for (int i = 0; i < secretPixels && offset < lsb.Length; i++)
        {
            // Берём значение пикселя (grayscale)
            byte val;
            if (secretBytesPerPixel >= 3)
            {
                // 24-битное изображение - берём R канал
                val = secretData[i * 3 + 2];
            }
            else
            {
                // 8-битное изображение - берём напрямую
                val = secretData[i];
            }
            
            // Кодируем 8 бит в 3 пикселя LSB (по 3 бита на пиксель)
            lsb[offset] = (byte)((lsb[offset] & 0xF8) | ((val >> 5) & 7));
            offset++;
            if (offset >= lsb.Length) break;
            
            lsb[offset] = (byte)((lsb[offset] & 0xF8) | ((val >> 2) & 7));
            offset++;
            if (offset >= lsb.Length) break;
            
            lsb[offset] = (byte)((lsb[offset] & 0xF8) | (val & 7));
            offset++;
        }
    }

    private byte[] DecodeSecretData(byte[] lsb, int secretWidth, int secretHeight, int containerWidth)
    {
        int totalPixels = secretWidth * secretHeight;
        byte[] result = new byte[totalPixels * 3]; // RGB данные
        
        int offset = 22; // Пропускаем заголовок (22 пикселя)
        for (int i = 0; i < totalPixels && offset < lsb.Length; i++)
        {
            // Извлекаем 3 бита из каждого пикселя LSB
            byte b0 = (byte)(lsb[offset] & 7);
            offset++;
            if (offset >= lsb.Length) break;
            
            byte b1 = (byte)(lsb[offset] & 7);
            offset++;
            if (offset >= lsb.Length) break;
            
            byte b2 = (byte)(lsb[offset] & 7);
            offset++;
            
            // Собираем 8 бит обратно
            byte val = (byte)((b0 << 5) | (b1 << 2) | b2);
            
            // Записываем как RGB (три канала одинаковые = grayscale)
            result[i * 3 + 0] = val; // B
            result[i * 3 + 1] = val; // G
            result[i * 3 + 2] = val; // R
        }
        
        return result;
    }

    private void EmbedLSB(byte[] imageData, byte[] lsbData, int width, int height, int bytesPerPixel)
    {
        if (bytesPerPixel == 3)
        {
            for (int i = 0; i < lsbData.Length; i++)
            {
                byte r = (byte)((lsbData[i] >> 2) & 1);
                byte g = (byte)((lsbData[i] >> 1) & 1);
                byte b = (byte)(lsbData[i] & 1);
                
                imageData[i * 3 + 2] = (byte)((imageData[i * 3 + 2] & 0xFE) | r);
                imageData[i * 3 + 1] = (byte)((imageData[i * 3 + 1] & 0xFE) | g);
                imageData[i * 3 + 0] = (byte)((imageData[i * 3 + 0] & 0xFE) | b);
            }
        }
    }

    private byte[] ImageToBits(byte[] imageData, int pixelCount)
    {
        List<byte> bits = new List<byte>();
        for (int i = 0; i < pixelCount; i++)
        {
            bits.Add((byte)(imageData[i] & 1));
        }
        return bits.ToArray();
    }

    private byte[] BitsToImage(byte[] bits, int width, int height)
    {
        byte[] image = new byte[width * height];
        for (int i = 0; i < image.Length && i < bits.Length; i++)
        {
            image[i] = (byte)(bits[i] * 255);
        }
        return image;
    }

    private byte[] EncodeHeader(int width, int height)
    {
        byte[] header = new byte[8];
        header[0] = (byte)((width >> 24) & 0xFF);
        header[1] = (byte)((width >> 16) & 0xFF);
        header[2] = (byte)((width >> 8) & 0xFF);
        header[3] = (byte)(width & 0xFF);
        header[4] = (byte)((height >> 24) & 0xFF);
        header[5] = (byte)((height >> 16) & 0xFF);
        header[6] = (byte)((height >> 8) & 0xFF);
        header[7] = (byte)(height & 0xFF);
        return header;
    }

    private (int width, int height) DecodeHeader(byte[] header)
    {
        int width = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
        int height = (header[4] << 24) | (header[5] << 16) | (header[6] << 8) | header[7];
        return (width, height);
    }

    private List<TextureRegion> FindTextureRegions(byte[] imageData, int width, int height, int blockSize)
    {
        List<TextureRegion> regions = new List<TextureRegion>();
        
        for (int y = 0; y < height - blockSize; y += blockSize)
        {
            for (int x = 0; x < width - blockSize; x += blockSize)
            {
                double variance = CalculateVariance(imageData, x, y, blockSize, width);
                if (variance > 100) // Порог текстурности
                {
                    regions.Add(new TextureRegion { X = x, Y = y, Size = blockSize, Variance = variance });
                }
            }
        }
        
        // Сортируем по дисперсии
        regions.Sort((a, b) => b.Variance.CompareTo(a.Variance));
        
        return regions;
    }

    private double CalculateVariance(byte[] imageData, int startX, int startY, int blockSize, int width)
    {
        long sum = 0;
        long sumSq = 0;
        int count = 0;

        for (int y = startY; y < startY + blockSize && y < imageData.Length / 3; y++)
        {
            for (int x = startX; x < startX + blockSize && x < width; x++)
            {
                int idx = (y * width + x) * 3;
                byte gray = (byte)((imageData[idx] + imageData[idx + 1] + imageData[idx + 2]) / 3);
                sum += gray;
                sumSq += gray * gray;
                count++;
            }
        }

        if (count == 0) return 0;
        
        double mean = (double)sum / count;
        double variance = (double)sumSq / count - mean * mean;
        return variance;
    }

    private void EmbedBitsInBlock(byte[] imageData, TextureRegion region, byte data, int width)
    {
        // Модифицируем LSB пикселей в блоке для кодирования данных
        for (int y = region.Y; y < region.Y + region.Size && y < imageData.Length / (width * 3); y++)
        {
            for (int x = region.X; x < region.X + region.Size && x < width; x++)
            {
                int idx = (y * width + x) * 3;
                byte bit = (byte)((data >> ((x - region.X) + (y - region.Y) * region.Size) % 8) & 1);
                imageData[idx] = (byte)((imageData[idx] & 0xFE) | bit);
            }
        }
    }

    private byte ExtractBitsFromBlock(byte[] imageData, TextureRegion region, int width)
    {
        byte result = 0;
        int bitPos = 0;
        
        for (int y = region.Y; y < region.Y + region.Size && y < imageData.Length / (width * 3); y++)
        {
            for (int x = region.X; x < region.X + region.Size && x < width; x++)
            {
                int idx = (y * width + x) * 3;
                byte bit = (byte)(imageData[idx] & 1);
                result = (byte)((result & ~(1 << bitPos)) | (bit << bitPos));
                bitPos++;
                if (bitPos >= 8) return result;
            }
        }
        
        return result;
    }

    private void CopyBlockWithModification(byte[] imageData, TextureRegion source, TextureRegion dest, byte data, int width)
    {
        // Копируем блок из source в dest с модификацией
        for (int y = 0; y < source.Size && dest.Y + y < imageData.Length / (width * 3); y++)
        {
            for (int x = 0; x < source.Size && dest.X + x < width; x++)
            {
                int srcIdx = ((source.Y + y) * width + (source.X + x)) * 3;
                int dstIdx = ((dest.Y + y) * width + (dest.X + x)) * 3;
                
                // Копируем пиксели
                imageData[dstIdx] = imageData[srcIdx];
                imageData[dstIdx + 1] = imageData[srcIdx + 1];
                imageData[dstIdx + 2] = imageData[srcIdx + 2];
                
                // Модифицируем LSB для кодирования данных
                if (x == 0 && y == 0)
                {
                    imageData[dstIdx] = (byte)((imageData[dstIdx] & 0xFE) | (data & 1));
                }
            }
        }
    }

    private byte ExtractModification(byte[] imageData, TextureRegion source, TextureRegion dest, int width)
    {
        int dstIdx = (dest.Y * width + dest.X) * 3;
        return (byte)(imageData[dstIdx] & 1);
    }

    #endregion
}

public class TextureRegion
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Size { get; set; }
    public double Variance { get; set; }
}