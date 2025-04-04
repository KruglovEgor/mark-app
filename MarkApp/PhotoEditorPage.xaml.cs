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
        // ��������� ����������� � ���������
        private SKBitmap photo;
        private SKMatrix transformMatrix = SKMatrix.CreateIdentity();
        private SKMatrix previousTransformMatrix = SKMatrix.CreateIdentity();

        // ��������� ��������������� � �����������
        private float scale = 1.0f;
        private float minScale = 0.5f;
        private float maxScale = 5.0f;
        private SKPoint panPosition = new SKPoint(0, 0);
        private SKPoint lastTouchPoint;
        private long lastTouchTime;

        // ��������� ������
        private bool isPanning = false;
        private bool isScaling = false;
        private float pinchStartDistance = 0;
        private float startScale = 1.0f;

        // ������ ������
        private bool isMarkMode = false;
        private bool isWaitingForSecondTap = false;
        private int currentMarkNumber = 1;

        // ��������� ��� �������� ����� � ������� � �������
        private class Mark
        {
            public SKPoint Position { get; set; }
            public int Number { get; set; }
        }

        // ������� �������� ��� ������
        private List<Mark> marks = new List<Mark>();
        private SKPoint pendingMarkPosition;

        public PhotoEditorPage()
        {
            InitializeComponent();
        }

        #region ��������� �������� � ���������� ����

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
                    PickerTitle = "�������� ����"
                });

                if (result != null)
                {
                    using (var stream = await result.OpenReadAsync())
                    {
                        // �������� �����������
                        photo = SKBitmap.Decode(stream);

                        // ����� �������� ��� �������� ������ �����������
                        marks.Clear();
                        currentMarkNumber = 1;
                        isWaitingForSecondTap = false;
                        isMarkMode = false;
                        toggleMarkModeButton.Text = "����� �����";

                        // ����� ������� �������������
                        ResetViewToFit();

                        // �����������
                        canvasView.InvalidateSurface();
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("������", $"�� ������� ��������� ����: {ex.Message}", "OK");
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

            // ������ ����������� ������ ������� � ����
            float canvasWidth = (float)canvasView.Width;
            float canvasHeight = (float)canvasView.Height;
            float photoWidth = photo.Width;
            float photoHeight = photo.Height;

            // ���������� �������� ��� ���������� ���� �� ����� � ��������� ��������
            float scaleX = canvasWidth * 0.9f / photoWidth;
            float scaleY = canvasHeight * 0.9f / photoHeight;
            scale = Math.Min(scaleX, scaleY);
            scale = Math.Max(minScale, Math.Min(maxScale, scale));

            // ���������� ������� ��� �������������
            panPosition.X = (canvasWidth - photoWidth * scale) / 2;
            panPosition.Y = (canvasHeight - photoHeight * scale) / 2;

            // ���������� ������� �������������
            UpdateTransformMatrix();
        }

        private void UpdateTransformMatrix()
        {
            // ��������� ���������� ������� ��� ��������
            previousTransformMatrix = transformMatrix;

            // ������� ����� ������� � ������ �������� � �������
            transformMatrix = SKMatrix.CreateScale(scale, scale);
            transformMatrix = transformMatrix.PostConcat(SKMatrix.CreateTranslation(panPosition.X, panPosition.Y));
        }

        private async void OnSavePhotoClicked(object sender, EventArgs e)
        {
            if (photo == null)
            {
                await DisplayAlert("��������", "��� ������������ ����", "OK");
                return;
            }

            try
            {
                loadingIndicator.IsVisible = true;
                loadingIndicator.IsRunning = true;

                // ������� ����� ����������� � �������
                var info = new SKImageInfo(photo.Width, photo.Height);
                using (var surface = SKSurface.Create(info))
                {
                    var canvas = surface.Canvas;
                    canvas.Clear(SKColors.White);
                    canvas.DrawBitmap(photo, 0, 0);

                    // ��������� ����� �� �����������
                    DrawMarksToCanvas(canvas, true);

                    // �������� � ���������� �����������
                    using (var image = surface.Snapshot())
                    using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 90))
                    {
                        // ���� ��� ���������� � ��� ����������
                        var filePath = Path.Combine(FileSystem.CacheDirectory, $"MarkedPhoto_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                        using (var fs = File.OpenWrite(filePath))
                        {
                            data.SaveTo(fs);
                        }

                        // �������� ������������ �� �������� ����������
                        await DisplayAlert("�����", "���� ���������", "OK");

                        // � �������� ���������� ����� ����� �������� ��� ��� ���������� � �������
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("������", $"�� ������� ��������� ����: {ex.Message}", "OK");
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
                await DisplayAlert("��������", "��� ������������ ����", "OK");
                return;
            }

            try
            {
                loadingIndicator.IsVisible = true;
                loadingIndicator.IsRunning = true;

                // ������� ����� ����������� � �������
                var info = new SKImageInfo(photo.Width, photo.Height);
                using (var surface = SKSurface.Create(info))
                {
                    var canvas = surface.Canvas;
                    canvas.Clear(SKColors.White);
                    canvas.DrawBitmap(photo, 0, 0);

                    // ��������� ����� �� �����������
                    DrawMarksToCanvas(canvas, true);

                    // �������� � ���������� ���������� ����� ��� �������
                    using (var image = surface.Snapshot())
                    using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 90))
                    {
                        var filePath = Path.Combine(FileSystem.CacheDirectory, $"SharedPhoto_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                        using (var fs = File.OpenWrite(filePath))
                        {
                            data.SaveTo(fs);
                        }

                        // ������ �����
                        await Share.RequestAsync(new ShareFileRequest
                        {
                            Title = "���������� ���� � �������",
                            File = new ShareFile(filePath)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("������", $"�� ������� ���������� ����: {ex.Message}", "OK");
            }
            finally
            {
                loadingIndicator.IsVisible = false;
                loadingIndicator.IsRunning = false;
            }
        }

        #endregion

        #region ��������� ��������� � �������

        private void OnCanvasViewPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var canvasWidth = e.Info.Width;
            var canvasHeight = e.Info.Height;

            // ������� ������
            canvas.Clear(SKColors.LightGray);

            if (photo == null)
                return;

            // ���������� ������� �������������
            canvas.Save();
            canvas.SetMatrix(transformMatrix);

            // ��������� ����
            canvas.DrawBitmap(photo, 0, 0);

            // ������� � �������� ����������� ��� ��������� �����
            canvas.Restore();

            // ��������� �����
            DrawMarksToCanvas(canvas, false);

            // ��������� ��������� ����� (���� ���� ������ ������� ��� ������)
            if (isWaitingForSecondTap)
            {
                // ������ ������ � ������� ������� �������
                using (var paint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = new SKColor(255, 0, 0, 128), // �������������� �������
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
            // ��� ������ ����� ���������� ������ ������� � ������� ������
            float circleRadius = forExport ? 40 : 30;
            float textSize = forExport ? 40 : 24;

            // ���� ������������ �����������, �� ��������� ������� �������������
            // �.�. ���������� ��� ������������� � ���������� �������� ����������
            SKMatrix matrix = forExport ? SKMatrix.CreateIdentity() : transformMatrix;

            foreach (var mark in marks)
            {
                SKPoint position = forExport ?
                    ConvertToPhotoCoordinates(mark.Position) :
                    mark.Position;

                // ������ ������
                using (var paint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = new SKColor(255, 0, 0, 128), // �������������� �������
                    IsAntialias = true
                })
                {
                    canvas.DrawCircle(position, circleRadius, paint);
                }

                // ������ ������� ������
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

                // ������ ����� �����
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
                                    position.Y + (textSize / 3), // ��������� ������������� ��� ������������� �������������
                                    paint);
                }
            }
        }

        private SKPoint ConvertToPhotoCoordinates(SKPoint viewPoint)
        {
            // ����������� ���������� ������ � ���������� ��������� ����
            SKMatrix invertedMatrix = new SKMatrix();
            if (!transformMatrix.TryInvert(out invertedMatrix))
                return viewPoint;

            SKPoint photoPoint = invertedMatrix.MapPoint(viewPoint);

            // ������������ ���������� � �������� ����
            photoPoint.X = Math.Max(0, Math.Min(photo.Width, photoPoint.X));
            photoPoint.Y = Math.Max(0, Math.Min(photo.Height, photoPoint.Y));

            return photoPoint;
        }

        private void OnCanvasViewTouch(object sender, SKTouchEventArgs e)
        {
            if (photo == null)
                return;

            var touchPoint = e.Location;

            // ��������� �������� � ����������� �� ������
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
                    // ������ ������� - ��������� ������� � ���� ������ ��� ������
                    pendingMarkPosition = e.Location;
                    isWaitingForSecondTap = true;
                    canvasView.InvalidateSurface();
                }
                else
                {
                    // ������ ������� - ������� ����� � �������
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
            // ��� ��������������� ������ ����� ����� ����������� ��������� ���������� �������
            switch (e.ActionType)
            {
                case SKTouchAction.Pressed:
                    isPanning = !isScaling; // �������� ���������������, ���� ��� �� ������������
                    lastTouchPoint = e.Location;
                    lastTouchTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                    break;

                case SKTouchAction.Moved:
                    if (e.InContact)
                    {
                        if (isPanning)
                        {
                            // ��������� ������� ���������������
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

                    // �������� �� ������� ������� ��� ���������������
                    long currentTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                    if (currentTime - lastTouchTime < 300) // 300�� ��� �������� �������
                    {
                        HandleDoubleTap(e.Location);
                    }
                    lastTouchTime = currentTime;
                    break;
            }
        }

        private void HandleDoubleTap(SKPoint location)
        {
            // ������� ������� ����������� ����� �������� ��������� � �����������
            if (Math.Abs(scale - 1.0f) < 0.1f)
            {
                // ���������� � ����� �������
                SKPoint beforeZoom = ConvertToPhotoCoordinates(location);
                scale = 2.0f;
                UpdateTransformMatrix();

                // ������������� ������� ��� ������������� ������������ �����
                SKPoint afterZoom = ConvertToPhotoCoordinates(location);
                panPosition.X += (afterZoom.X - beforeZoom.X) * scale;
                panPosition.Y += (afterZoom.Y - beforeZoom.Y) * scale;
                UpdateTransformMatrix();
            }
            else
            {
                // ������� � �������� ����������� �� ������
                ResetViewToFit();
            }

            canvasView.InvalidateSurface();
        }

        // ��� ���������� ����-���� ����� ������������ ������� GestureRecognizer
        // ����� ����� �������� ���������� ����� PinchGestureRecognizer � XAML
        // � ��������������� ����� � code-behind

        #endregion

        #region ���������� �������� � ������ ��������

        private void OnToggleMarkModeClicked(object sender, EventArgs e)
        {
            isMarkMode = !isMarkMode;
            toggleMarkModeButton.Text = isMarkMode ? "����� ���������" : "����� �����";

            if (!isMarkMode)
            {
                // ��� ������ �� ������ ����� �������� �������� ������� �������
                isWaitingForSecondTap = false;
                canvasView.InvalidateSurface();
            }
        }

        private void OnUndoClicked(object sender, EventArgs e)
        {
            if (isWaitingForSecondTap)
            {
                // ���� ���� ������ �������, �������� ��������
                isWaitingForSecondTap = false;
                canvasView.InvalidateSurface();
                return;
            }

            if (marks.Count > 0)
            {
                // ������� ��������� ����� � ��������� �������
                marks.RemoveAt(marks.Count - 1);
                if (currentMarkNumber > 1)
                    currentMarkNumber--;

                canvasView.InvalidateSurface();
            }
        }

        #endregion

        #region ��������� ��������� ������� ��������

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);

            // ��� ��������� ������� ���� ������������� �������
            if (photo != null && width > 0 && height > 0)
            {
                ResetViewToFit();
                canvasView.InvalidateSurface();
            }
        }

        #endregion
    }
}