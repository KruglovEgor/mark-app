using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Point = Microsoft.Maui.Graphics.Point;
using Color = Microsoft.Maui.Graphics.Color;

namespace MarkApp
{
    public partial class PhotoEditorPage : ContentPage
    {
        // Настройки редактора
        private bool isMarkMode = false;
        private int currentMarkNumber = 0;
        private SKBitmap? originalImage;
        private SKBitmap? displayImage;

        // Масштабирование и перемещение
        private float scale = 1.0f;
        private float minScale = 0.5f;
        private float maxScale = 5.0f;
        private SKPoint offset = new SKPoint(0, 0);
        private SKPoint lastTouchPoint = new SKPoint(0, 0);
        private bool isDragging = false;

        // Маркеры
        private class Marker
        {
            public SKPoint Position { get; set; }
            public int Number { get; set; }
        }

        private List<Marker> markers = new List<Marker>();

        public PhotoEditorPage()
        {
            InitializeComponent();
        }

        private async void OnPickPhotoClicked(object sender, EventArgs e)
        {
            try
            {
                var result = await FilePicker.PickAsync(new PickOptions
                {
                    FileTypes = FilePickerFileType.Images
                });

                if (result == null)
                    return;

                using var stream = await result.OpenReadAsync();
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                // Загружаем изображение в SkiaSharp
                originalImage = SKBitmap.Decode(memoryStream.ToArray());

                // Создаем копию для отображения с изменениями
                displayImage = originalImage.Copy();

                // Сбрасываем настройки масштабирования и маркеры
                scale = 1.0f;
                offset = new SKPoint(0, 0);
                markers.Clear();
                currentMarkNumber = 0;

                // Обновляем канвас
                canvasView.InvalidateSurface();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Ошибка", $"Не удалось загрузить изображение: {ex.Message}", "OK");
            }
        }

        private void OnMarkModeClicked(object sender, EventArgs e)
        {
            if (originalImage == null)
            {
                DisplayAlert("Предупреждение", "Сначала выберите изображение", "OK");
                return;
            }

            isMarkMode = !isMarkMode;
            markModeButton.Text = isMarkMode ? "Выключить метки" : "Режим меток";
        }

        private void OnUndoClicked(object sender, EventArgs e)
        {
            if (markers.Count > 0)
            {
                markers.RemoveAt(markers.Count - 1);

                // Если удалили последнюю метку, уменьшаем счетчик
                if (currentMarkNumber > 0)
                    currentMarkNumber--;

                // Перерисовываем
                RedrawImage();
                canvasView.InvalidateSurface();
            }
        }

        private async void OnSavePhotoClicked(object sender, EventArgs e)
        {
            if (displayImage == null)
            {
                await DisplayAlert("Предупреждение", "Нет изображения для сохранения", "OK");
                return;
            }

            try
            {
                // Получаем путь для сохранения
                string fileName = $"marked_photo_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
                string filePath = Path.Combine(FileSystem.CacheDirectory, fileName);

                // Сохраняем изображение с маркерами
                using (var outputStream = File.OpenWrite(filePath))
                {
                    // Создаем финальное изображение в оригинальном размере с метками
                    using (var finalImage = new SKBitmap(originalImage.Width, originalImage.Height))
                    {
                        using (var canvas = new SKCanvas(finalImage))
                        {
                            // Рисуем оригинал
                            canvas.DrawBitmap(originalImage, 0, 0);

                            // Рисуем все маркеры
                            DrawMarkersOnCanvas(canvas, 1.0f, new SKPoint(0, 0));
                        }

                        // Сохраняем в JPEG
                        finalImage.Encode(SKEncodedImageFormat.Jpeg, 95).SaveTo(outputStream);
                    }
                }

                // Показываем сообщение об успехе с возможностью поделиться
                bool share = await DisplayAlert("Успешно",
                    $"Изображение сохранено по пути:\n{filePath}",
                    "Поделиться", "OK");

                if (share)
                {
                    await Share.RequestAsync(new ShareFileRequest
                    {
                        Title = "Поделиться фото с метками",
                        File = new ShareFile(filePath)
                    });
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Ошибка", $"Не удалось сохранить изображение: {ex.Message}", "OK");
            }
        }

        private void OnCanvasViewPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            SKCanvas canvas = e.Surface.Canvas;

            // Очищаем канвас
            canvas.Clear(SKColors.LightGray);

            if (displayImage == null)
                return;

            // Получаем размеры канваса и изображения
            var canvasSize = e.Info.Size;

            // Вычисляем масштаб для правильного отображения
            float imageAspect = (float)displayImage.Width / displayImage.Height;
            float canvasAspect = canvasSize.Width / canvasSize.Height;

            float scaleX, scaleY;
            SKRect destRect;

            if (imageAspect > canvasAspect)
            {
                // Изображение шире канваса, масштабируем по ширине
                scaleX = scaleY = canvasSize.Width / displayImage.Width;
                float scaledHeight = displayImage.Height * scaleX;
                destRect = new SKRect(0, (canvasSize.Height - scaledHeight) / 2,
                                     canvasSize.Width, (canvasSize.Height + scaledHeight) / 2);
            }
            else
            {
                // Изображение выше канваса, масштабируем по высоте
                scaleX = scaleY = canvasSize.Height / displayImage.Height;
                float scaledWidth = displayImage.Width * scaleY;
                destRect = new SKRect((canvasSize.Width - scaledWidth) / 2, 0,
                                     (canvasSize.Width + scaledWidth) / 2, canvasSize.Height);
            }

            // Применяем масштаб и смещение от пользователя
            SKMatrix matrix = SKMatrix.CreateScale(scale, scale);
            SKPoint center = new SKPoint(canvasSize.Width / 2, canvasSize.Height / 2);
            matrix = matrix.PostConcat(SKMatrix.CreateTranslation(-center.X, -center.Y));
            matrix = matrix.PostConcat(SKMatrix.CreateTranslation(center.X + offset.X, center.Y + offset.Y));

            canvas.SetMatrix(matrix);

            // Рисуем изображение
            canvas.DrawBitmap(displayImage, destRect);

            // Рисуем маркеры
            DrawMarkersOnCanvas(canvas, scaleX, new SKPoint(destRect.Left, destRect.Top));

            // Сбрасываем матрицу трансформации
            canvas.ResetMatrix();
        }

        private void DrawMarkersOnCanvas(SKCanvas canvas, float imageScale, SKPoint imageOffset)
        {
            if (markers.Count == 0)
                return;

            // Настройки для маркеров
            float circleRadius = 15 / imageScale;

            // Кисти для рисования
            using (var circlePaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = new SKColor(255, 0, 0, 180),
                IsAntialias = true
            })
            using (var strokePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = SKColors.White,
                StrokeWidth = 2 / imageScale,
                IsAntialias = true
            })
            using (var textPaint = new SKPaint
            {
                Color = SKColors.White,
                TextSize = 12 / imageScale,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center
            })
            {
                foreach (var marker in markers)
                {
                    // Позиция маркера с учетом смещения изображения
                    SKPoint position = new SKPoint(
                        imageOffset.X + marker.Position.X,
                        imageOffset.Y + marker.Position.Y
                    );

                    // Рисуем круг
                    canvas.DrawCircle(position, circleRadius, circlePaint);
                    canvas.DrawCircle(position, circleRadius, strokePaint);

                    // Рисуем номер
                    canvas.DrawText(marker.Number.ToString(),
                                   position.X,
                                   position.Y + textPaint.TextSize / 3, // Выравнивание по вертикали
                                   textPaint);
                }
            }
        }

        private void OnCanvasViewTouch(object sender, SKTouchEventArgs e)
        {
            switch (e.ActionType)
            {
                case SKTouchAction.Pressed:
                    // Запоминаем точку нажатия
                    lastTouchPoint = e.Location;

                    if (isMarkMode && !e.InContact)
                    {
                        // Добавляем маркер только если в режиме меток и не используется стилус
                        AddMarker(e.Location);
                    }
                    else
                    {
                        // Начинаем перемещение
                        isDragging = true;
                    }
                    break;

                case SKTouchAction.Moved:
                    if (isDragging)
                    {
                        // Перемещение изображения
                        offset = new SKPoint(
                            offset.X + (e.Location.X - lastTouchPoint.X) / scale,
                            offset.Y + (e.Location.Y - lastTouchPoint.Y) / scale
                        );
                        lastTouchPoint = e.Location;
                        canvasView.InvalidateSurface();
                    }
                    break;

                case SKTouchAction.Released:
                    isDragging = false;
                    break;

                case SKTouchAction.WheelChanged:
                    // Масштабирование при прокрутке колесика мыши
                    float newScale = scale * (float)Math.Pow(2, -e.WheelDelta / 10.0f);
                    scale = Math.Clamp(newScale, minScale, maxScale);
                    canvasView.InvalidateSurface();
                    break;
            }

            // Помечаем событие как обработанное
            e.Handled = true;
        }

        private void AddMarker(SKPoint touchPoint)
        {
            if (originalImage == null || displayImage == null)
                return;

            try
            {
                // Получаем размеры изображения на экране
                var canvasSize = canvasView.CanvasSize;
                float imageAspect = (float)displayImage.Width / displayImage.Height;
                float canvasAspect = canvasSize.Width / canvasSize.Height;

                SKRect destRect;
                float scaleX, scaleY;

                if (imageAspect > canvasAspect)
                {
                    scaleX = scaleY = canvasSize.Width / displayImage.Width;
                    float scaledHeight = displayImage.Height * scaleX;
                    destRect = new SKRect(0, (canvasSize.Height - scaledHeight) / 2,
                                         canvasSize.Width, (canvasSize.Height + scaledHeight) / 2);
                }
                else
                {
                    scaleX = scaleY = canvasSize.Height / displayImage.Height;
                    float scaledWidth = displayImage.Width * scaleY;
                    destRect = new SKRect((canvasSize.Width - scaledWidth) / 2, 0,
                                         (canvasSize.Width + scaledWidth) / 2, canvasSize.Height);
                }

                // Учитываем текущий масштаб и смещение пользователя
                SKMatrix matrix = SKMatrix.CreateScale(scale, scale);
                SKPoint center = new SKPoint(canvasSize.Width / 2, canvasSize.Height / 2);
                matrix = matrix.PostConcat(SKMatrix.CreateTranslation(-center.X, -center.Y));
                matrix = matrix.PostConcat(SKMatrix.CreateTranslation(center.X + offset.X, center.Y + offset.Y));

                // Инвертируем матрицу для получения координат в исходном пространстве
                SKMatrix inverseMatrix;
                if (!matrix.TryInvert(out inverseMatrix))
                    return;

                // Преобразуем точку касания в координаты оригинального пространства
                SKPoint originalTouchPoint = inverseMatrix.MapPoint(touchPoint);

                // Проверяем, что точка находится в пределах изображения
                if (!destRect.Contains(originalTouchPoint))
                    return;

                // Преобразуем координаты в координаты изображения
                float imageX = (originalTouchPoint.X - destRect.Left) / destRect.Width * displayImage.Width;
                float imageY = (originalTouchPoint.Y - destRect.Top) / destRect.Height * displayImage.Height;

                // Увеличиваем счетчик маркеров
                currentMarkNumber++;

                // Создаем новый маркер
                Marker newMarker = new Marker
                {
                    Position = new SKPoint(imageX, imageY),
                    Number = currentMarkNumber
                };

                // Добавляем маркер в список
                markers.Add(newMarker);

                // Перерисовываем изображение с маркерами
                RedrawImage();
                canvasView.InvalidateSurface();
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Ошибка", $"Не удалось добавить метку: {ex.Message}", "OK");
                });
            }
        }

        private void RedrawImage()
        {
            if (originalImage == null)
                return;

            // Создаем новую копию оригинального изображения
            displayImage = originalImage.Copy();
        }
    }
}