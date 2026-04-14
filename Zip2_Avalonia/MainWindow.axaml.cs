using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Zip2.Services;
using Zip2.Structs;

namespace Zip2_Avalonia;

public partial class MainWindow : Window
{
    private readonly BmpReaderService _bmpReader;
    private readonly ImageProcessingService _imageProcessor;

    private string _currentFilePath;
    private byte[] _imageData;
    private int _width;
    private int _height;
    private int _bytesPerPixel;

    public MainWindow()
    {
        InitializeComponent();
        _bmpReader = new BmpReaderService();
        _imageProcessor = new ImageProcessingService();
    }

    private async void BtnSelectImage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите BMP файл",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("BMP files")
                {
                    Patterns = new[] { "*.bmp" }
                },
                new FilePickerFileType("All files")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        if (files.Count >= 1)
        {
            _currentFilePath = files[0].Path.LocalPath;
            txtStatus.Text = $"Загрузка: {Path.GetFileName(_currentFilePath)}";

            try
            {
                LoadAndDisplayImage(_currentFilePath);
                LoadAndDisplayHeaders(_currentFilePath);
                LoadImageData(_currentFilePath);

                btnProcess.IsEnabled = true;
                txtStatus.Text = $"Загружен: {Path.GetFileName(_currentFilePath)}";
            }
            catch (Exception ex)
            {
                var dialog = new MessageBox
                {
                    Content = $"Ошибка загрузки файла: {ex.Message}",
                    Title = "Ошибка"
                };
                await dialog.ShowDialog(this);
                txtStatus.Text = "Ошибка загрузки";
                btnProcess.IsEnabled = false;
            }
        }
    }

    private void LoadAndDisplayImage(string filePath)
    {
        try
        {
            var bitmap = new Bitmap(filePath);
            imgPreview.Source = bitmap;
        }
        catch (Exception ex)
        {
            throw new Exception($"Не удалось загрузить изображение: {ex.Message}");
        }
    }

    private void LoadAndDisplayHeaders(string filePath)
    {
        var (fileHeader, infoHeader) = _bmpReader.ReadHeaders(filePath);

        string headerText = GetHeaderDisplayText(fileHeader, infoHeader);
        txtHeaderInfo.Text = headerText;

        txtImageInfo.Text = $"{Math.Abs(infoHeader.biWidth)} × {Math.Abs(infoHeader.biHeight)} | " +
                           $"{infoHeader.biBitCount} бит | " +
                           $"{(infoHeader.biCompression == 0 ? "без сжатия" : "сжато")}";
    }

    private string GetHeaderDisplayText(BITMAPFILEHEADER fileHeader, BITMAPINFOHEADER infoHeader)
    {
        string fileHeaderText = $"bfType:        0x{fileHeader.bfType:X4} ({(char)(fileHeader.bfType & 0xFF)}{(char)(fileHeader.bfType >> 8)})\n" +
                                $"bfSize:        {fileHeader.bfSize} байт\n" +
                                $"bfReserved1:   {fileHeader.bfReserved1}\n" +
                                $"bfReserved2:   {fileHeader.bfReserved2}\n" +
                                $"bfOffBits:     {fileHeader.bfOffBits} байт\n\n";

        int totalPixels = Math.Abs(infoHeader.biWidth) * Math.Abs(infoHeader.biHeight);
        string imageType = infoHeader.biBitCount <= 8 ? "Используется палитра" : "Палитра не используется (TrueColor)";
        string rowOrder = infoHeader.biHeight < 0 ? "сверху-вниз" : "снизу-вверх";

        string infoHeaderText = $"biSize:         {infoHeader.biSize} байт\n" +
                                $"biWidth:        {infoHeader.biWidth} пикселей\n" +
                                $"biHeight:       {Math.Abs(infoHeader.biHeight)} пикселей\n" +
                                $"biPlanes:       {infoHeader.biPlanes}\n" +
                                $"biBitCount:     {infoHeader.biBitCount} бит/пиксель\n" +
                                $"biCompression:  {infoHeader.biCompression} ({(infoHeader.biCompression == 0 ? "BI_RGB" : "Сжато")})\n" +
                                $"biSizeImage:    {infoHeader.biSizeImage} байт\n" +
                                $"biXPelsPerMeter:{infoHeader.biXPelsPerMeter}\n" +
                                $"biYPelsPerMeter:{infoHeader.biYPelsPerMeter}\n" +
                                $"biClrUsed:      {infoHeader.biClrUsed}\n" +
                                $"biClrImportant: {infoHeader.biClrImportant}\n" +
                                $"\n--- Дополнительно ---\n" +
                                $"Всего пикселей: {totalPixels}\n" +
                                $"Глубина цвета:  {infoHeader.biBitCount} бит\n" +
                                $"{imageType}\n" +
                                $"Порядок строк: {rowOrder}";

        return fileHeaderText + infoHeaderText;
    }

    private void LoadImageData(string filePath)
    {
        (_imageData, _width, _height, _bytesPerPixel) = _bmpReader.ReadImageData(filePath);
    }

    private async void BtnProcess_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_imageData == null)
        {
            var dialog = new MessageBox
            {
                Content = "Сначала выберите BMP файл",
                Title = "Предупреждение"
            };
            await dialog.ShowDialog(this);
            return;
        }

        try
        {
            ColorChannelMode mode;
            if (rbGrayscale.IsChecked == true)
                mode = ColorChannelMode.Grayscale;
            else if (rbColored.IsChecked == true)
                mode = ColorChannelMode.Colored;
            else
                mode = ColorChannelMode.Both;

            string outputDirectory = _imageProcessor.CreateOutputDirectory(_currentFilePath);
            txtStatus.Text = $"Обработка... Результаты будут в: {outputDirectory}";

            if (_bytesPerPixel == 3)
            {
                _imageProcessor.SplitIntoColorChannels(_imageData, _width, _height,
                    outputDirectory, Path.GetFileNameWithoutExtension(_currentFilePath), mode);
            }
            else
            {
                var dialog = new MessageBox
                {
                    Content = "Изображение не 24-битное, разложение на цветовые составляющие пропущено.",
                    Title = "Предупреждение"
                };
                await dialog.ShowDialog(this);
            }

            // Выполняем разложение на битовые срезы
            _imageProcessor.SplitIntoBitSlices(_imageData, _width, _height, _bytesPerPixel,
                outputDirectory, Path.GetFileNameWithoutExtension(_currentFilePath));

            txtStatus.Text = $"✅ Готово! Результаты в: {outputDirectory}";

            // Открываем папку с результатами
            OpenFolder(outputDirectory);

            var successDialog = new MessageBox
            {
                Content = $"Все операции успешно завершены!\nРезультаты находятся в папке:\n{outputDirectory}",
                Title = "Успех"
            };
            await successDialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            var errorDialog = new MessageBox
            {
                Content = $"Ошибка при обработке: {ex.Message}",
                Title = "Ошибка"
            };
            await errorDialog.ShowDialog(this);
            txtStatus.Text = "Ошибка при обработке";
        }
    }

    private void OpenFolder(string folderPath)
    {
        try
        {
            if (Directory.Exists(folderPath))
            {
                Process.Start("explorer.exe", folderPath);
            }
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"Результаты сохранены, но не удалось открыть папку: {ex.Message}";
        }
    }
}