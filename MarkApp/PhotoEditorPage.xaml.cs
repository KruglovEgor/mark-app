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

        // Структура для хранения метки с кружком и номером
        // Изменено: позиция хранится в координатах фотографии, а не экрана
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

                // Создаем новое изображение с метками
                var info = new SKImageInfo(photo.Width, photo.Height);
                using (var surface = SKSurface.Create(info))
                {
                    var canvas = surface.Canvas;
                    canvas.Clear(); // Не используем белый цвет, чтобы не влиять на яркость
                    canvas.DrawBitmap(photo, 0, 0);

                    // Отрисовка меток на изображении
                    DrawMarksToCanvas(canvas, true);

                    // Создание и сохранение изображения с максимальным качеством
                    using (var image = surface.Snapshot())
                    using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 100))
                    {
                        // Путь для сохранения в кэш директорию
                        var filePath = Path.Combine(FileSystem.CacheDirectory, $"MarkedPhoto_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                        using (var fs = File.OpenWrite(filePath))
                        {
                            data.SaveTo(fs);
                        }

                        // Сообщаем пользователю об успешном сохранении
                        await DisplayAlert("Успех", "Фото сохранено", "OK");

                        // В реальном приложении можно здесь добавить код для сохранения в галерею
                    }
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

                // Создаем новое изображение с метками
                var info = new SKImageInfo(photo.Width, photo.Height);
                using (var surface = SKSurface.Create(info))
                {
                    var canvas = surface.Canvas;
                    canvas.Clear(); // Без белого цвета
                    canvas.DrawBitmap(photo, 0, 0);

                    // Отрисовка меток на изображении
                    DrawMarksToCanvas(canvas, true);

                    // Создание и сохранение временного файла для шаринга с максимальным качеством
                    using (var image = surface.Snapshot())
                    using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 100))
                    {
                        var filePath = Path.Combine(FileSystem.CacheDirectory, $"SharedPhoto_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
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

            // Отрисовка меток
            DrawMarksToCanvas(canvas, false);
        }

        private void DrawMarksToCanvas(SKCanvas canvas, bool forExport)
        {
            // Для разных целей используем разные радиусы и размеры шрифта
            float circleRadius = forExport ? 40 : 30;
            float textSize = forExport ? 40 : 24;

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
                    canvas.DrawCircle(position, circleRadius, paint);
                }

                // Рисуем границу кружка
                using (var paint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.Red,
                    StrokeWidth = 3,
                    IsAntialias = true
                })
                {
                    canvas.DrawCircle(position, circleRadius, paint);
                }

                // Рисуем номер метки
                using (var paint = new SKPaint
                {
                    Color = SKColors.White,
                    TextSize = textSize,
                    IsAntialias = true,
                    TextAlign = SKTextAlign.Center
                })
                {
                    canvas.DrawText(mark.Number.ToString(),
                                    position.X,
                                    position.Y + (textSize / 3), // Корректировка для вертикального центрирования
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
                    // Начинаем панорамирование только если не масштабируем пинчем
                    if (!isScaling)
                    {
                        isPanning = true;
                        lastTouchPoint = e.Location;
                        lastTouchTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                    }
                    break;

                case SKTouchAction.Moved:
                    if (e.InContact && isPanning)
                    {
                        // Обновляем позицию панорамирования
                        var deltaX = e.Location.X - lastTouchPoint.X;
                        var deltaY = e.Location.Y - lastTouchPoint.Y;

                        panPosition.X += deltaX;
                        panPosition.Y += deltaY;

                        // Ограничиваем панорамирование, чтобы изображение не выходило за пределы экрана слишком далеко
                        LimitPanningPosition();

                        UpdateTransformMatrix();
                        canvasView.InvalidateSurface();

                        lastTouchPoint = e.Location;
                    }
                    break;

                case SKTouchAction.Released:
                    isPanning = false;

                    // Проверка на двойное касание для масштабирования
                    long currentTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                    if (currentTime - lastTouchTime < 300) // 300мс для двойного касания
                    {
                        HandleDoubleTap(e.Location);
                    }
                    lastTouchTime = currentTime;
                    break;
            }
        }

        // Ограничение панорамирования, чтобы изображение не выходило слишком далеко за пределы экрана
        private void LimitPanningPosition()
        {
            if (photo == null) return;

            float canvasWidth = (float)canvasView.Width;
            float canvasHeight = (float)canvasView.Height;
            float photoWidth = photo.Width * scale;
            float photoHeight = photo.Height * scale;

            // Расчет максимальных ограничений (разрешаем небольшой выход за границы)
            float maxOffsetX = photoWidth * 0.5f;
            float maxOffsetY = photoHeight * 0.5f;

            // Ограничение по горизонтали
            if (panPosition.X > canvasWidth + maxOffsetX)
                panPosition.X = canvasWidth + maxOffsetX;
            else if (panPosition.X + photoWidth < -maxOffsetX)
                panPosition.X = -photoWidth - maxOffsetX;

            // Ограничение по вертикали
            if (panPosition.Y > canvasHeight + maxOffsetY)
                panPosition.Y = canvasHeight + maxOffsetY;
            else if (panPosition.Y + photoHeight < -maxOffsetY)
                panPosition.Y = -photoHeight - maxOffsetY;
        }

        private void HandleDoubleTap(SKPoint location)
        {
            // Двойное касание переключает между исходным масштабом и увеличением
            if (Math.Abs(scale - minScale) < 0.1f)
            {
                // Увеличение в точке касания
                SKPoint beforeZoom = ConvertToPhotoCoordinates(location);
                scale = Math.Min(maxScale, scale * 2.5f);

                // Обновляем слайдер без вызова события
                zoomSlider.ValueChanged -= OnZoomSliderValueChanged;
                zoomSlider.Value = scale;
                zoomSlider.ValueChanged += OnZoomSliderValueChanged;

                // Обновляем текст с текущим масштабом
                zoomValueLabel.Text = $"{scale * 100:0}%";

                UpdateTransformMatrix();

                // Корректировка позиции для центрирования увеличенного места
                SKPoint afterZoom = ConvertToPhotoCoordinates(location);
                panPosition.X += (afterZoom.X - beforeZoom.X) * scale;
                panPosition.Y += (afterZoom.Y - beforeZoom.Y) * scale;

                // Ограничение панорамирования
                LimitPanningPosition();

                UpdateTransformMatrix();
            }
            else
            {
                // Возврат к фиксации изображения на экране
                ResetViewToFit();
            }

            canvasView.InvalidateSurface();
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

        private void OnPinchUpdated(object sender, PinchGestureUpdatedEventArgs e)
        {
            if (photo == null) return;

            switch (e.Status)
            {
                case GestureStatus.Started:
                    isScaling = true;
                    isPanning = false;
                    pinchStartScale = scale;
                    pinchCenter = new SKPoint((float)e.ScaleOrigin.X * (float)canvasView.Width,
                                             (float)e.ScaleOrigin.Y * (float)canvasView.Height);
                    break;

                case GestureStatus.Running:
                    // Рассчитываем новый масштаб
                    float newScale = pinchStartScale * (float)e.Scale;
                    newScale = Math.Max(minScale, Math.Min(maxScale, newScale));

                    // Вычисляем точку, которая должна оставаться под курсором при масштабировании
                    SKPoint beforeZoom = ConvertToPhotoCoordinates(pinchCenter);

                    // Устанавливаем новый масштаб
                    scale = newScale;

                    // Обновляем слайдер без вызова события
                    zoomSlider.ValueChanged -= OnZoomSliderValueChanged;
                    zoomSlider.Value = scale;
                    zoomSlider.ValueChanged += OnZoomSliderValueChanged;

                    // Обновляем текст с текущим масштабом
                    zoomValueLabel.Text = $"{scale * 100:0}%";

                    UpdateTransformMatrix();

                    // Корректируем позицию, чтобы точка оставалась под курсором
                    SKPoint afterZoom = ConvertToPhotoCoordinates(pinchCenter);
                    panPosition.X += (afterZoom.X - beforeZoom.X) * scale;
                    panPosition.Y += (afterZoom.Y - beforeZoom.Y) * scale;

                    // Ограничение панорамирования
                    LimitPanningPosition();

                    UpdateTransformMatrix();
                    canvasView.InvalidateSurface();
                    break;

                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    isScaling = false;
                    break;
            }
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