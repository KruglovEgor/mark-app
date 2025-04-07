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
        private int currentMarkNumber = 1;

        // Размеры меток относительно фото (в процентах от ширины фото)
        private const float CircleRadiusPercent = 0.02f; // 2% от ширины фото
        private const float TextSizePercent = 0.02f;     // 2% от ширины фото
        private const float StrokeWidthPercent = 0.002f; // 0.2% от ширины фото

        // Абсолютные минимальные размеры для экспорта
        private const float MinExportCircleRadius = 20f;
        private const float MinExportTextSize = 20f;
        private const float MinExportStrokeWidth = 2f;

        // Структура для хранения метки с кружком и номером
        private class Mark
        {
            public SKPoint PhotoPosition { get; set; }  // Позиция в координатах фотографии
            public int Number { get; set; }
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
                        currentMarkNumber = 1;
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
                    values.Put(Android.Provider.MediaStore.Images.Media.InterfaceConsts.MimeType, "image/jpeg");
                    
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
        }

        private void DrawMarksToCanvas(SKCanvas canvas, bool forExport)
        {
            if (photo == null || marks.Count == 0)
                return;

            // Рассчитываем размеры меток относительно фото
            float photoWidth = photo.Width;
            float circleRadius = photoWidth * CircleRadiusPercent;
            float textSize = photoWidth * TextSizePercent;
            float strokeWidth = photoWidth * StrokeWidthPercent;

            // Для экспорта используем минимальные значения
            if (forExport)
            {
                circleRadius = Math.Max(circleRadius, MinExportCircleRadius);
                textSize = Math.Max(textSize, MinExportTextSize);
                strokeWidth = Math.Max(strokeWidth, MinExportStrokeWidth);
            }

            foreach (var mark in marks)
            {
                // Получаем позицию метки
                SKPoint position;

                if (forExport)
                {
                    // Для экспорта используем координаты фотографии напрямую
                    position = mark.PhotoPosition;
                }
                else
                {
                    // Для отображения на экране преобразуем координаты фото в экранные
                    position = transformMatrix.MapPoint(mark.PhotoPosition);
                }

                // Рисуем кружок
                using (var paint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = new SKColor(255, 0, 0, 128), // Полупрозрачный красный
                    IsAntialias = true
                })
                {
                    if (forExport)
                    {
                        canvas.DrawCircle(position, circleRadius, paint);
                    }
                    else
                    {
                        // Учитываем масштаб для отображения на экране
                        canvas.DrawCircle(position, circleRadius * scale, paint);
                    }
                }

                // Рисуем границу кружка
                using (var paint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.Red,
                    StrokeWidth = forExport ? strokeWidth : strokeWidth * scale,
                    IsAntialias = true
                })
                {
                    if (forExport)
                    {
                        canvas.DrawCircle(position, circleRadius, paint);
                    }
                    else
                    {
                        canvas.DrawCircle(position, circleRadius * scale, paint);
                    }
                }

                // Рисуем номер метки
                using (var paint = new SKPaint
                {
                    Color = SKColors.White,
                    TextSize = forExport ? textSize : textSize * scale,
                    IsAntialias = true,
                    TextAlign = SKTextAlign.Center
                })
                {
                    float verticalOffset = forExport ? textSize / 3 : (textSize * scale) / 3;
                    canvas.DrawText(mark.Number.ToString(),
                                    position.X,
                                    position.Y + verticalOffset,
                                    paint);
                }
            }
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

        private void HandleMarkModeTouch(SKTouchEventArgs e)
        {
            if (e.ActionType == SKTouchAction.Released)
            {
                // Преобразуем координаты касания в координаты фото
                SKPoint photoPoint = ConvertToPhotoCoordinates(e.Location);

                // Проверяем, находится ли точка в пределах фотографии
                if (IsPointInsidePhoto(photoPoint))
                {
                    // Одно касание создает метку с номером
                    marks.Add(new Mark
                    {
                        PhotoPosition = photoPoint,  // Сохраняем позицию в координатах фото
                        Number = currentMarkNumber++
                    });
                    canvasView.InvalidateSurface();
                }
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
            toggleMarkModeButton.Text = isMarkMode ? "Режим просмотра" : "Режим меток";
        }

        private void OnUndoClicked(object sender, EventArgs e)
        {
            if (marks.Count > 0)
            {
                // Удаляем последнюю метку и уменьшаем счетчик
                marks.RemoveAt(marks.Count - 1);
                if (currentMarkNumber > 1)
                    currentMarkNumber--;

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