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
        private float pinchStartDistance = 0;
        private float startScale = 1.0f;

        // Режимы работы
        private bool isMarkMode = false;
        private bool isWaitingForSecondTap = false;
        private int currentMarkNumber = 1;

        // Структура для хранения метки с кружком и номером
        private class Mark
        {
            public SKPoint Position { get; set; }
            public int Number { get; set; }
        }

        // История действий для отмены
        private List<Mark> marks = new List<Mark>();
        private SKPoint pendingMarkPosition;

        public PhotoEditorPage()
        {
            InitializeComponent();
        }

        #region Обработка загрузки и сохранения фото

        private async void OnPickPhotoClicked(object sender, EventArgs e)
        {
            try
            {
                noPhotoLabel .IsVisible = false;
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
                        isWaitingForSecondTap = false;
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
            float scaleX = canvasWidth * 0.9f / photoWidth;
            float scaleY = canvasHeight * 0.9f / photoHeight;
            scale = Math.Min(scaleX, scaleY);
            scale = Math.Max(minScale, Math.Min(maxScale, scale));

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
                    canvas.Clear(SKColors.White);
                    canvas.DrawBitmap(photo, 0, 0);

                    // Отрисовка меток на изображении
                    DrawMarksToCanvas(canvas, true);

                    // Создание и сохранение изображения
                    using (var image = surface.Snapshot())
                    using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 90))
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
                    canvas.Clear(SKColors.White);
                    canvas.DrawBitmap(photo, 0, 0);

                    // Отрисовка меток на изображении
                    DrawMarksToCanvas(canvas, true);

                    // Создание и сохранение временного файла для шаринга
                    using (var image = surface.Snapshot())
                    using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 90))
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

            // Отрисовка ожидаемой метки (если ждем второе касание для номера)
            if (isWaitingForSecondTap)
            {
                // Рисуем кружок в позиции первого касания
                using (var paint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = new SKColor(255, 0, 0, 128), // Полупрозрачный красный
                    IsAntialias = true
                })
                {
                    canvas.DrawCircle(pendingMarkPosition, 30, paint);
                }

                using (var paint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.Red,
                    StrokeWidth = 4,
                    IsAntialias = true
                })
                {
                    canvas.DrawCircle(pendingMarkPosition, 30, paint);
                }
            }
        }

        private void DrawMarksToCanvas(SKCanvas canvas, bool forExport)
        {
            // Для разных целей используем разные радиусы и размеры шрифта
            float circleRadius = forExport ? 40 : 30;
            float textSize = forExport ? 40 : 24;

            // Если экспортируем изображение, не применяем матрицу трансформации
            // Т.к. координаты уже преобразованы в координаты исходной фотографии
            SKMatrix matrix = forExport ? SKMatrix.CreateIdentity() : transformMatrix;

            foreach (var mark in marks)
            {
                SKPoint position = forExport ?
                    ConvertToPhotoCoordinates(mark.Position) :
                    mark.Position;

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
                                    position.Y + (textSize / 3), // Небольшая корректировка для вертикального центрирования
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

            // Ограничиваем координаты в пределах фото
            photoPoint.X = Math.Max(0, Math.Min(photo.Width, photoPoint.X));
            photoPoint.Y = Math.Max(0, Math.Min(photo.Height, photoPoint.Y));

            return photoPoint;
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
                if (!isWaitingForSecondTap)
                {
                    // Первое касание - сохраняем позицию и ждем второе для номера
                    pendingMarkPosition = e.Location;
                    isWaitingForSecondTap = true;
                    canvasView.InvalidateSurface();
                }
                else
                {
                    // Второе касание - создаем метку с номером
                    marks.Add(new Mark
                    {
                        Position = pendingMarkPosition,
                        Number = currentMarkNumber++
                    });

                    isWaitingForSecondTap = false;
                    canvasView.InvalidateSurface();
                }
            }
        }

        private void HandleViewModeTouch(SKTouchEventArgs e)
        {
            // Для масштабирования жестом щипка нужно отслеживать состояния нескольких касаний
            switch (e.ActionType)
            {
                case SKTouchAction.Pressed:
                    isPanning = !isScaling; // Начинаем панорамирование, если уже не масштабируем
                    lastTouchPoint = e.Location;
                    lastTouchTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                    break;

                case SKTouchAction.Moved:
                    if (e.InContact)
                    {
                        if (isPanning)
                        {
                            // Обновляем позицию панорамирования
                            var deltaX = e.Location.X - lastTouchPoint.X;
                            var deltaY = e.Location.Y - lastTouchPoint.Y;

                            panPosition.X += deltaX;
                            panPosition.Y += deltaY;

                            UpdateTransformMatrix();
                            canvasView.InvalidateSurface();
                        }
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

        private void HandleDoubleTap(SKPoint location)
        {
            // Двойное касание переключает между исходным масштабом и увеличением
            if (Math.Abs(scale - 1.0f) < 0.1f)
            {
                // Увеличение в точке касания
                SKPoint beforeZoom = ConvertToPhotoCoordinates(location);
                scale = 2.0f;
                UpdateTransformMatrix();

                // Корректировка позиции для центрирования увеличенного места
                SKPoint afterZoom = ConvertToPhotoCoordinates(location);
                panPosition.X += (afterZoom.X - beforeZoom.X) * scale;
                panPosition.Y += (afterZoom.Y - beforeZoom.Y) * scale;
                UpdateTransformMatrix();
            }
            else
            {
                // Возврат к фиксации изображения на экране
                ResetViewToFit();
            }

            canvasView.InvalidateSurface();
        }

        // Для реализации пинч-зума нужно использовать события GestureRecognizer
        // здесь можно добавить реализацию через PinchGestureRecognizer в XAML
        // и соответствующий метод в code-behind

        #endregion

        #region Управление режимами и отмена действий

        private void OnToggleMarkModeClicked(object sender, EventArgs e)
        {
            isMarkMode = !isMarkMode;
            toggleMarkModeButton.Text = isMarkMode ? "Режим просмотра" : "Режим меток";

            if (!isMarkMode)
            {
                // При выходе из режима меток отменяем ожидание второго касания
                isWaitingForSecondTap = false;
                canvasView.InvalidateSurface();
            }
        }

        private void OnUndoClicked(object sender, EventArgs e)
        {
            if (isWaitingForSecondTap)
            {
                // Если ждем второе касание, отменяем ожидание
                isWaitingForSecondTap = false;
                canvasView.InvalidateSurface();
                return;
            }

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