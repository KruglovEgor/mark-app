using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MarkApp
{
    public partial class PhotoEditorPage : ContentPage
    {
        // Состояние изображения и редактора
        private SKBitmap photo;
        private SKMatrix transformMatrix = SKMatrix.CreateIdentity();
        private SKMatrix previousTransformMatrix = SKMatrix.CreateIdentity();

        // Состояние масштабирования и перемещения
        private float scale = 1.0f;
        private float minScale = 0.5f;
        private float maxScale = 5.0f;
        private SKPoint panPosition = new SKPoint(0, 0);
        private SKPoint lastTouchPoint;
        private long lastTouchTime;

        // Состояние жестов
        private bool isPanning = false;
        private bool isScaling = false;
        private float pinchStartScale = 1.0f;
        private SKPoint pinchCenter;

        // Режимы работы
        private bool isMarkMode = false;
        private bool isWaitingForSecondPoint = false;
        private SKPoint firstPoint; // Первая точка для режима меток

        // Размеры меток относительно фото (в процентах от ширины фото)
        private const float CircleRadiusPercent = 0.02f; // 2% от ширины фото
        private const float TextBoxWidthPercent = 0.06f; // 6% от ширины фото
        private const float TextBoxHeightPercent = 0.04f; // 4% от ширины фото
        private const float TextSizePercent = 0.02f;     // 2% от ширины фото
        private const float StrokeWidthPercent = 0.002f; // 0.2% от ширины фото
        private const float LineWidthPercent = 0.002f;   // 0.2% от ширины фото

        // Абсолютные минимальные размеры для экспорта
        private const float MinExportCircleRadius = 20f;
        private const float MinExportTextBoxWidth = 60f;
        private const float MinExportTextBoxHeight = 40f;
        private const float MinExportTextSize = 20f;
        private const float MinExportStrokeWidth = 2f;
        private const float MinExportLineWidth = 2f;

        // Структура для хранения метки с кружком, прямоугольником и линией
        private class Mark
        {
            public SKPoint CirclePosition { get; set; }      // Позиция кружка в координатах фотографии
            public SKPoint BoxPosition { get; set; }         // Позиция прямоугольника в координатах фотографии
            public int Number { get; set; }                  // Номер метки
        }

        // История действий для отмены
        private List<Mark> marks = new List<Mark>();

        public PhotoEditorPage()
        {
            InitializeComponent();

            // Устанавливаем начальные значения для слайдера
            zoomSlider.Value = scale;
            zoomValueLabel.Text = $"{scale * 100:0}%";
        }

        #region Обработка загрузки и сохранения фото

        private async void OnPickPhotoClicked(object sender, EventArgs e)
        {
            try
            {
                noPhotoLabel.IsVisible = false;
                loadingIndicator.IsVisible = true;
                loadingIndicator.IsRunning = true;

                var result = await FilePicker.PickAsync(new PickOptions
                {
                    FileTypes = FilePickerFileType.Images,
                    PickerTitle = "Выберите фото"
                });

                if (result != null)
                {
                    using (var stream = await result.OpenReadAsync())
                    {
                        // Загрузка изображения
                        photo = SKBitmap.Decode(stream);

                        // Сброс настроек при загрузке нового изображения
                        marks.Clear();
                        isWaitingForSecondPoint = false;
                        isMarkMode = false;
                        toggleMarkModeButton.Text = "Режим меток";

                        // Сброс матрицы трансформации
                        ResetViewToFit();

                        // Перерисовка
                        canvasView.InvalidateSurface();
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Ошибка", $"Не удалось загрузить фото: {ex.Message}", "OK");
            }
            finally
            {
                loadingIndicator.IsVisible = false;
                loadingIndicator.IsRunning = false;
                noPhotoLabel.IsVisible = photo == null;
            }
        }

        private void ResetViewToFit()
        {
            if (photo == null) return;

            // Расчет соотношения сторон канваса и фото
            float canvasWidth = (float)canvasView.Width;
            float canvasHeight = (float)canvasView.Height;
            float photoWidth = photo.Width;
            float photoHeight = photo.Height;

            // Вычисление масштаба для вписывания фото на экран с некоторым отступом
            float scaleX = canvasWidth * 0.95f / photoWidth;
            float scaleY = canvasHeight * 0.95f / photoHeight;
            scale = Math.Min(scaleX, scaleY);
            scale = Math.Max(minScale, Math.Min(maxScale, scale));

            // Обновляем слайдер без вызова события
            zoomSlider.ValueChanged -= OnZoomSliderValueChanged;
            zoomSlider.Value = scale;
            zoomSlider.ValueChanged += OnZoomSliderValueChanged;

            // Обновляем текст с текущим масштабом
            zoomValueLabel.Text = $"{scale * 100:0}%";

            // Вычисление позиции для центрирования
            panPosition.X = (canvasWidth - photoWidth * scale) / 2;
            panPosition.Y = (canvasHeight - photoHeight * scale) / 2;

            // Обновление матрицы трансформации
            UpdateTransformMatrix();
        }

        private void UpdateTransformMatrix()
        {
            // Сохраняем предыдущую матрицу для расчетов
            previousTransformMatrix = transformMatrix;

            // Создаем новую матрицу с учетом масштаба и позиции
            transformMatrix = SKMatrix.CreateScale(scale, scale);
            transformMatrix = transformMatrix.PostConcat(SKMatrix.CreateTranslation(panPosition.X, panPosition.Y));
        }

        private async void OnSavePhotoClicked(object sender, EventArgs e)
        {
            if (photo == null)
            {
                await DisplayAlert("Внимание", "Нет загруженного фото", "OK");
                return;
            }

            try
            {
                loadingIndicator.IsVisible = true;
                loadingIndicator.IsRunning = true;

                // Клонируем оригинальное изображение
                var photoCopy = photo.Copy();

                // Рисуем метки непосредственно на копии изображения
                using (var canvas = new SKCanvas(photoCopy))
                {
                    // Рисуем метки
                    DrawMarksToCanvas(canvas, true);
                }

                // Преобразуем битмапу в данные для сохранения
                using (var image = SKImage.FromBitmap(photoCopy))
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100)) // PNG для лучшего качества
                {
                    var filename = $"MarkedPhoto_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                    var cacheFilePath = Path.Combine(FileSystem.CacheDirectory, filename);

                    using (var fs = File.OpenWrite(cacheFilePath))
                    {
                        data.SaveTo(fs);
                    }

                    // Сохраняем в галерею
                    await SaveToGallery(cacheFilePath, filename);

                    await DisplayAlert("Успех", "Фото сохранено в галерею", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Ошибка", $"Не удалось сохранить фото: {ex.Message}", "OK");
            }
            finally
            {
                loadingIndicator.IsVisible = false;
                loadingIndicator.IsRunning = false;
            }
        }

        // Метод для сохранения фото в галерею
        private async Task SaveToGallery(string filePath, string filename)
        {
            if (!File.Exists(filePath))
                return;

#if ANDROID
                    try
                    {
                        // Проверяем разрешения на запись для Android
                        var status = await Permissions.CheckStatusAsync<Permissions.StorageWrite>();
                        if (status != PermissionStatus.Granted)
                        {
                            status = await Permissions.RequestAsync<Permissions.StorageWrite>();
                            if (status != PermissionStatus.Granted)
                                return;
                        }

                        // Также запрашиваем доступ к медиа
                        var mediaStatus = await Permissions.CheckStatusAsync<Permissions.Media>();
                        if (mediaStatus != PermissionStatus.Granted)
                        {
                            mediaStatus = await Permissions.RequestAsync<Permissions.Media>();
                            if (mediaStatus != PermissionStatus.Granted)
                                return;
                        }

                        // Более современный способ для API 29+
                        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                        var values = new Android.Content.ContentValues();
                        values.Put(Android.Provider.MediaStore.Images.Media.InterfaceConsts.DisplayName, filename);
                        values.Put(Android.Provider.MediaStore.Images.Media.InterfaceConsts.MimeType, "image/png");
                        
                        // Используем относительный путь только для Android API 29+
                        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Q)
                        {
                            values.Put(Android.Provider.MediaStore.Images.Media.InterfaceConsts.RelativePath, "Pictures/MarkApp");
                        }

                        var resolver = Android.App.Application.Context.ContentResolver;
                        var contentUri = Android.Provider.MediaStore.Images.Media.ExternalContentUri;
                        
                        if (contentUri != null)
                        {
                            var uri = resolver?.Insert(contentUri, values);

                            if (uri != null)
                            {
                                using var outputStream = resolver?.OpenOutputStream(uri);
                                if (outputStream != null)
                                {
                                    await stream.CopyToAsync(outputStream);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error saving to gallery: {ex.Message}");
                        
                        // Для API < 29 используем старый способ
                        if (Android.OS.Build.VERSION.SdkInt < Android.OS.BuildVersionCodes.Q)
                        {
                            try
                            {
                                var mediaScanIntent = new Android.Content.Intent(Android.Content.Intent.ActionMediaScannerScanFile);
                                var file = new Java.IO.File(filePath);
                                var uri = Android.Net.Uri.FromFile(file);
                                mediaScanIntent.SetData(uri);
                                Android.App.Application.Context.SendBroadcast(mediaScanIntent);
                            }
                            catch (Exception scanEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error with media scanner: {scanEx.Message}");
                            }
                        }
                    }
#endif

#if IOS
                try
                {
                    // Для iOS используем Photos API
                    var status = await Permissions.CheckStatusAsync<Permissions.Photos>();
                    if (status != PermissionStatus.Granted)
                    {
                        status = await Permissions.RequestAsync<Permissions.Photos>();
                        if (status != PermissionStatus.Granted)
                            return;
                    }

                    // Запрашиваем авторизацию в Photos (статический метод)
                    await Photos.PHPhotoLibrary.RequestAuthorizationAsync();

                    // Получаем общий экземпляр библиотеки фотографий
                    var library = Photos.PHPhotoLibrary.SharedPhotoLibrary;
                    if (library == null)
                        return;

                    // Создаем изображение из файла
                    var image = UIKit.UIImage.FromFile(filePath);
                    if (image == null)
                        return;

                    // Сохраняем изображение в фотогалерею
                    var tcs = new TaskCompletionSource<bool>();

                    library.PerformChanges(() =>
                    {
                        Photos.PHAssetCreationRequest.CreationRequestForAsset().AddResource(Photos.PHAssetResourceType.Photo,
                            Foundation.NSUrl.FromFilename(filePath), null);
                    },
                    (success, error) =>
                    {
                        if (error != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"iOS photo save error: {error.LocalizedDescription}");
                            tcs.SetResult(false);
                        }
                        else
                        {
                            tcs.SetResult(success);
                        }
                    });

                    await tcs.Task;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error saving to iOS gallery: {ex.Message}");
                }
#endif
        }

        private async void OnSharePhotoClicked(object sender, EventArgs e)
        {
            if (photo == null)
            {
                await DisplayAlert("Внимание", "Нет загруженного фото", "OK");
                return;
            }

            try
            {
                loadingIndicator.IsVisible = true;
                loadingIndicator.IsRunning = true;

                // Клонируем оригинальное изображение
                var photoCopy = photo.Copy();

                // Рисуем метки непосредственно на копии изображения
                using (var canvas = new SKCanvas(photoCopy))
                {
                    // Рисуем метки
                    DrawMarksToCanvas(canvas, true);
                }

                // Преобразуем битмапу в данные для сохранения
                using (var image = SKImage.FromBitmap(photoCopy))
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100)) // PNG для лучшего качества
                {
                    var filePath = Path.Combine(FileSystem.CacheDirectory, $"SharedPhoto_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                    using (var fs = File.OpenWrite(filePath))
                    {
                        data.SaveTo(fs);
                    }

                    // Шаринг файла
                    await Share.RequestAsync(new ShareFileRequest
                    {
                        Title = "Поделиться фото с метками",
                        File = new ShareFile(filePath)
                    });
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Ошибка", $"Не удалось поделиться фото: {ex.Message}", "OK");
            }
            finally
            {
                loadingIndicator.IsVisible = false;
                loadingIndicator.IsRunning = false;
            }
        }

        #endregion

        #region Обработка отрисовки и касаний

        private void OnCanvasViewPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var canvasWidth = e.Info.Width;
            var canvasHeight = e.Info.Height;

            // Очистка холста
            canvas.Clear(SKColors.LightGray);

            if (photo == null)
                return;

            // Применение матрицы трансформации
            canvas.Save();
            canvas.SetMatrix(transformMatrix);

            // Отрисовка фото
            canvas.DrawBitmap(photo, 0, 0);

            // Возврат к исходным координатам для отрисовки меток
            canvas.Restore();

            // Отрисовка меток с фиксированным размером
            DrawMarksToCanvas(canvas, false);

            // Если ожидаем второй точки, рисуем временный кружок первой точки
            if (isWaitingForSecondPoint)
            {
                DrawTemporaryCircle(canvas);
            }
        }

        private void DrawTemporaryCircle(SKCanvas canvas)
        {
            // Отображаем временный кружок для первой точки
            SKPoint screenPosition = transformMatrix.MapPoint(firstPoint);
            float circleRadius = photo.Width * CircleRadiusPercent * scale;

            using (var paint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = new SKColor(255, 0, 0, 128),
                IsAntialias = true
            })
            {
                canvas.DrawCircle(screenPosition, circleRadius, paint);
            }

            using (var paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = SKColors.Red,
                StrokeWidth = photo.Width * StrokeWidthPercent * scale,
                IsAntialias = true
            })
            {
                canvas.DrawCircle(screenPosition, circleRadius, paint);
            }
        }

        private void DrawMarksToCanvas(SKCanvas canvas, bool forExport)
        {
            if (photo == null || marks.Count == 0)
                return;

            // Рассчитываем размеры меток относительно фото
            float photoWidth = photo.Width;
            float circleRadius = photoWidth * CircleRadiusPercent;
            float boxWidth = photoWidth * TextBoxWidthPercent;
            float boxHeight = photoWidth * TextBoxHeightPercent;
            float textSize = photoWidth * TextSizePercent;
            float strokeWidth = photoWidth * StrokeWidthPercent;
            float lineWidth = photoWidth * LineWidthPercent;

            // Для экспорта используем минимальные значения
            if (forExport)
            {
                circleRadius = Math.Max(circleRadius, MinExportCircleRadius);
                boxWidth = Math.Max(boxWidth, MinExportTextBoxWidth);
                boxHeight = Math.Max(boxHeight, MinExportTextBoxHeight);
                textSize = Math.Max(textSize, MinExportTextSize);
                strokeWidth = Math.Max(strokeWidth, MinExportStrokeWidth);
                lineWidth = Math.Max(lineWidth, MinExportLineWidth);
            }

            foreach (var mark in marks)
            {
                // Получаем позиции элементов
                SKPoint circlePosition, boxPosition;
                float actualCircleRadius, actualBoxWidth, actualBoxHeight, actualTextSize;
                float actualStrokeWidth, actualLineWidth;

                if (forExport)
                {
                    // Для экспорта используем координаты фотографии напрямую
                    circlePosition = mark.CirclePosition;
                    boxPosition = mark.BoxPosition;
                    actualCircleRadius = circleRadius;
                    actualBoxWidth = boxWidth;
                    actualBoxHeight = boxHeight;
                    actualTextSize = textSize;
                    actualStrokeWidth = strokeWidth;
                    actualLineWidth = lineWidth;
                }
                else
                {
                    // Для отображения на экране преобразуем координаты фото в экранные
                    circlePosition = transformMatrix.MapPoint(mark.CirclePosition);
                    boxPosition = transformMatrix.MapPoint(mark.BoxPosition);

                    // Учитываем масштаб для отображения на экране
                    actualCircleRadius = circleRadius * scale;
                    actualBoxWidth = boxWidth * scale;
                    actualBoxHeight = boxHeight * scale;
                    actualTextSize = textSize * scale;
                    actualStrokeWidth = strokeWidth * scale;
                    actualLineWidth = lineWidth * scale;
                }

                // 1. Рисуем кружок
                using (var paint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = new SKColor(255, 0, 0, 128), // Полупрозрачный красный
                    IsAntialias = true
                })
                {
                    canvas.DrawCircle(circlePosition, actualCircleRadius, paint);
                }

                using (var paint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.Red,
                    StrokeWidth = actualStrokeWidth,
                    IsAntialias = true
                })
                {
                    canvas.DrawCircle(circlePosition, actualCircleRadius, paint);
                }

                // 2. Рисуем прямоугольник с текстом
                var rect = new SKRect(
                    boxPosition.X - actualBoxWidth / 2,
                    boxPosition.Y - actualBoxHeight / 2,
                    boxPosition.X + actualBoxWidth / 2,
                    boxPosition.Y + actualBoxHeight / 2);

                using (var paint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = new SKColor(200, 200, 200, 230), // Светло-серый с небольшой прозрачностью
                    IsAntialias = true
                })
                {
                    canvas.DrawRect(rect, paint);
                }

                using (var paint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.Black,
                    StrokeWidth = actualStrokeWidth,
                    IsAntialias = true
                })
                {
                    canvas.DrawRect(rect, paint);
                }

                // Рисуем номер внутри прямоугольника
                using (var paint = new SKPaint
                {
                    Color = SKColors.Black,
                    TextSize = actualTextSize,
                    IsAntialias = true,
                    TextAlign = SKTextAlign.Center
                })
                {
                    canvas.DrawText(mark.Number.ToString(),
                                    boxPosition.X,
                                    boxPosition.Y + actualTextSize / 3, // Корректировка для центрирования
                                    paint);
                }

                // 3. Рисуем соединительную линию между кружком и ближайшим углом прямоугольника
                using (var paint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.Black,
                    StrokeWidth = actualLineWidth,
                    IsAntialias = true
                })
                {
                    // Определяем ближайший к кружку угол прямоугольника
                    SKPoint nearestCorner = GetNearestBoxCorner(circlePosition, boxPosition, actualBoxWidth, actualBoxHeight);
                    canvas.DrawLine(circlePosition, nearestCorner, paint);
                }
            }
        }

        // Метод для определения ближайшего угла прямоугольника к точке круга
        private SKPoint GetNearestBoxCorner(SKPoint circlePosition, SKPoint boxPosition, float boxWidth, float boxHeight)
        {
            // Определяем, в каком квадранте относительно центра прямоугольника находится кружок
            bool isCircleRightOfBox = circlePosition.X > boxPosition.X;
            bool isCircleBelowBox = circlePosition.Y > boxPosition.Y;

            // Выбираем соответствующий угол
            float cornerX = boxPosition.X + (isCircleRightOfBox ? 1 : -1) * (boxWidth / 2);
            float cornerY = boxPosition.Y + (isCircleBelowBox ? 1 : -1) * (boxHeight / 2);

            return new SKPoint(cornerX, cornerY);
        }

        private SKPoint ConvertToPhotoCoordinates(SKPoint viewPoint)
        {
            // Преобразуем координаты экрана в координаты исходного фото
            SKMatrix invertedMatrix = new SKMatrix();
            if (!transformMatrix.TryInvert(out invertedMatrix))
                return viewPoint;

            SKPoint photoPoint = invertedMatrix.MapPoint(viewPoint);
            return photoPoint;
        }

        // Проверяем, находится ли точка в пределах фотографии
        private bool IsPointInsidePhoto(SKPoint photoPoint)
        {
            if (photo == null) return false;

            return photoPoint.X >= 0 && photoPoint.X < photo.Width &&
                   photoPoint.Y >= 0 && photoPoint.Y < photo.Height;
        }

        private void OnCanvasViewTouch(object sender, SKTouchEventArgs e)
        {
            if (photo == null)
                return;

            var touchPoint = e.Location;

            // Обработка действия в зависимости от режима
            if (isMarkMode)
            {
                HandleMarkModeTouch(e);
            }
            else
            {
                HandleViewModeTouch(e);
            }

            e.Handled = true;
        }

        private async void HandleMarkModeTouch(SKTouchEventArgs e)
        {
            if (e.ActionType == SKTouchAction.Released)
            {
                // Преобразуем координаты касания в координаты фото
                SKPoint photoPoint = ConvertToPhotoCoordinates(e.Location);

                // Проверяем, находится ли точка в пределах фотографии
                if (IsPointInsidePhoto(photoPoint))
                {
                    if (!isWaitingForSecondPoint)
                    {
                        // Первое касание - сохраняем первую точку и ждем второго касания
                        firstPoint = photoPoint;
                        isWaitingForSecondPoint = true;
                        canvasView.InvalidateSurface();
                    }
                    else
                    {
                        // Второе касание - запрашиваем у пользователя номер
                        isWaitingForSecondPoint = false;
                        await PromptForMarkNumber(photoPoint);
                    }
                }
            }
        }

        private async Task PromptForMarkNumber(SKPoint secondPoint)
        {
            try
            {
                string result = await DisplayPromptAsync("Ввод номера", "Введите номер (от 0 до 999):",
                    initialValue: "0", maxLength: 3, keyboard: Keyboard.Numeric);

                if (result != null && int.TryParse(result, out int number) && number >= 0 && number <= 999)
                {
                    // Создаем новую метку
                    marks.Add(new Mark
                    {
                        CirclePosition = firstPoint,
                        BoxPosition = secondPoint,
                        Number = number
                    });

                    canvasView.InvalidateSurface();
                }
                else if (result != null)
                {
                    // Некорректный ввод, показываем предупреждение
                    await DisplayAlert("Ошибка", "Введите число от 0 до 999", "OK");
                    // Повторный запрос
                    await PromptForMarkNumber(secondPoint);
                }
                else
                {
                    // Пользователь отменил ввод, отменяем всю метку
                    canvasView.InvalidateSurface();
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Ошибка", $"Произошла ошибка: {ex.Message}", "OK");
                canvasView.InvalidateSurface();
            }
        }

        private void HandleViewModeTouch(SKTouchEventArgs e)
        {
            switch (e.ActionType)
            {
                case SKTouchAction.Pressed:
                    isPanning = true;
                    lastTouchPoint = e.Location;
                    break;

                case SKTouchAction.Moved:
                    if (e.InContact && isPanning)
                    {
                        // Обновляем позицию панорамирования
                        var deltaX = e.Location.X - lastTouchPoint.X;
                        var deltaY = e.Location.Y - lastTouchPoint.Y;

                        panPosition.X += deltaX;
                        panPosition.Y += deltaY;

                        // Ограничиваем панорамирование
                        LimitPanningPosition();

                        UpdateTransformMatrix();
                        canvasView.InvalidateSurface();

                        lastTouchPoint = e.Location;
                    }
                    break;

                case SKTouchAction.Released:
                    isPanning = false;
                    break;
            }
        }

        // Ограничение панорамирования, чтобы изображение не выходило за пределы экрана
        private void LimitPanningPosition()
        {
            if (photo == null) return;

            float canvasWidth = (float)canvasView.Width;
            float canvasHeight = (float)canvasView.Height;
            float photoWidth = photo.Width * scale;
            float photoHeight = photo.Height * scale;

            // Более строгие ограничения - фото всегда должно быть на экране
            // Минимальное значение X - отрицательное: (photoWidth - минимальная видимая часть)
            // Максимальное значение X - положительное: (canvasWidth - минимальная видимая часть)
            float minVisiblePart = Math.Min(photoWidth, canvasWidth) * 0.25f; // Хотя бы 25% фото должно быть видно

            // Ограничение по горизонтали
            if (panPosition.X > canvasWidth - minVisiblePart)
                panPosition.X = canvasWidth - minVisiblePart;
            else if (panPosition.X + photoWidth < minVisiblePart)
                panPosition.X = minVisiblePart - photoWidth;

            // Ограничение по вертикали с такой же логикой
            float minVisiblePartY = Math.Min(photoHeight, canvasHeight) * 0.25f;
            if (panPosition.Y > canvasHeight - minVisiblePartY)
                panPosition.Y = canvasHeight - minVisiblePartY;
            else if (panPosition.Y + photoHeight < minVisiblePartY)
                panPosition.Y = minVisiblePartY - photoHeight;
        }

        private void OnZoomSliderValueChanged(object sender, ValueChangedEventArgs e)
        {
            if (photo == null) return;

            // Сохраняем текущую точку в центре экрана
            float canvasWidth = (float)canvasView.Width;
            float canvasHeight = (float)canvasView.Height;
            SKPoint centerScreen = new SKPoint(canvasWidth / 2, canvasHeight / 2);
            SKPoint beforeZoom = ConvertToPhotoCoordinates(centerScreen);

            // Устанавливаем новый масштаб из слайдера
            scale = (float)e.NewValue;

            // Обновляем текст с текущим масштабом в процентах
            zoomValueLabel.Text = $"{scale * 100:0}%";

            // Обновляем матрицу трансформации
            UpdateTransformMatrix();

            // Корректируем позицию, чтобы центр экрана оставался в той же точке фото
            SKPoint afterZoom = ConvertToPhotoCoordinates(centerScreen);
            panPosition.X += (afterZoom.X - beforeZoom.X) * scale;
            panPosition.Y += (afterZoom.Y - beforeZoom.Y) * scale;

            // Ограничение панорамирования
            LimitPanningPosition();

            // Еще раз обновляем матрицу после коррекции позиции
            UpdateTransformMatrix();

            // Перерисовываем канвас
            canvasView.InvalidateSurface();
        }

        #endregion

        #region Управление режимами и отмена действий

        private void OnToggleMarkModeClicked(object sender, EventArgs e)
        {
            isMarkMode = !isMarkMode;
            if (!isMarkMode)
            {
                // При выходе из режима меток, сбрасываем состояние ожидания
                isWaitingForSecondPoint = false;
            }
            toggleMarkModeButton.Text = isMarkMode ? "Режим просмотра" : "Режим меток";
            canvasView.InvalidateSurface();
        }

        private void OnUndoClicked(object sender, EventArgs e)
        {
            if (isWaitingForSecondPoint)
            {
                // Если ожидаем вторую точку, отменяем текущую операцию
                isWaitingForSecondPoint = false;
                canvasView.InvalidateSurface();
            }
            else if (marks.Count > 0)
            {
                // Удаляем последнюю метку
                marks.RemoveAt(marks.Count - 1);
                canvasView.InvalidateSurface();
            }
        }

        #endregion

        #region Обработка изменения размера страницы

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);

            // При изменении размера окна пересчитываем матрицу
            if (photo != null && width > 0 && height > 0)
            {
                ResetViewToFit();
                canvasView.InvalidateSurface();
            }
        }

        #endregion
    }
}