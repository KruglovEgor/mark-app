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
        private float pinchStartScale = 1.0f;
        private SKPoint pinchCenter;

        // ������ ������
        private bool isMarkMode = false;
        private bool isWaitingForSecondPoint = false;
        private SKPoint firstPoint; // ������ ����� ��� ������ �����

        // ������� ����� ������������ ���� (� ��������� �� ������ ����)
        private const float CircleRadiusPercent = 0.02f; // 2% �� ������ ����
        private const float TextBoxWidthPercent = 0.06f; // 6% �� ������ ����
        private const float TextBoxHeightPercent = 0.04f; // 4% �� ������ ����
        private const float TextSizePercent = 0.02f;     // 2% �� ������ ����
        private const float StrokeWidthPercent = 0.002f; // 0.2% �� ������ ����
        private const float LineWidthPercent = 0.002f;   // 0.2% �� ������ ����

        // ���������� ����������� ������� ��� ��������
        private const float MinExportCircleRadius = 20f;
        private const float MinExportTextBoxWidth = 60f;
        private const float MinExportTextBoxHeight = 40f;
        private const float MinExportTextSize = 20f;
        private const float MinExportStrokeWidth = 2f;
        private const float MinExportLineWidth = 2f;

        // ��������� ��� �������� ����� � �������, ��������������� � ������
        private class Mark
        {
            public SKPoint CirclePosition { get; set; }      // ������� ������ � ����������� ����������
            public SKPoint BoxPosition { get; set; }         // ������� �������������� � ����������� ����������
            public int Number { get; set; }                  // ����� �����
        }

        // ������� �������� ��� ������
        private List<Mark> marks = new List<Mark>();

        public PhotoEditorPage()
        {
            InitializeComponent();

            // ������������� ��������� �������� ��� ��������
            zoomSlider.Value = scale;
            zoomValueLabel.Text = $"{scale * 100:0}%";
        }

        #region ��������� �������� � ���������� ����

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
                        isWaitingForSecondPoint = false;
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
            float scaleX = canvasWidth * 0.95f / photoWidth;
            float scaleY = canvasHeight * 0.95f / photoHeight;
            scale = Math.Min(scaleX, scaleY);
            scale = Math.Max(minScale, Math.Min(maxScale, scale));

            // ��������� ������� ��� ������ �������
            zoomSlider.ValueChanged -= OnZoomSliderValueChanged;
            zoomSlider.Value = scale;
            zoomSlider.ValueChanged += OnZoomSliderValueChanged;

            // ��������� ����� � ������� ���������
            zoomValueLabel.Text = $"{scale * 100:0}%";

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

                // ��������� ������������ �����������
                var photoCopy = photo.Copy();

                // ������ ����� ��������������� �� ����� �����������
                using (var canvas = new SKCanvas(photoCopy))
                {
                    // ������ �����
                    DrawMarksToCanvas(canvas, true);
                }

                // ����������� ������� � ������ ��� ����������
                using (var image = SKImage.FromBitmap(photoCopy))
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100)) // PNG ��� ������� ��������
                {
                    var filename = $"MarkedPhoto_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                    var cacheFilePath = Path.Combine(FileSystem.CacheDirectory, filename);

                    using (var fs = File.OpenWrite(cacheFilePath))
                    {
                        data.SaveTo(fs);
                    }

                    // ��������� � �������
                    await SaveToGallery(cacheFilePath, filename);

                    await DisplayAlert("�����", "���� ��������� � �������", "OK");
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

        // ����� ��� ���������� ���� � �������
        private async Task SaveToGallery(string filePath, string filename)
        {
            if (!File.Exists(filePath))
                return;

#if ANDROID
                    try
                    {
                        // ��������� ���������� �� ������ ��� Android
                        var status = await Permissions.CheckStatusAsync<Permissions.StorageWrite>();
                        if (status != PermissionStatus.Granted)
                        {
                            status = await Permissions.RequestAsync<Permissions.StorageWrite>();
                            if (status != PermissionStatus.Granted)
                                return;
                        }

                        // ����� ����������� ������ � �����
                        var mediaStatus = await Permissions.CheckStatusAsync<Permissions.Media>();
                        if (mediaStatus != PermissionStatus.Granted)
                        {
                            mediaStatus = await Permissions.RequestAsync<Permissions.Media>();
                            if (mediaStatus != PermissionStatus.Granted)
                                return;
                        }

                        // ����� ����������� ������ ��� API 29+
                        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                        var values = new Android.Content.ContentValues();
                        values.Put(Android.Provider.MediaStore.Images.Media.InterfaceConsts.DisplayName, filename);
                        values.Put(Android.Provider.MediaStore.Images.Media.InterfaceConsts.MimeType, "image/png");
                        
                        // ���������� ������������� ���� ������ ��� Android API 29+
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
                        
                        // ��� API < 29 ���������� ������ ������
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
                    // ��� iOS ���������� Photos API
                    var status = await Permissions.CheckStatusAsync<Permissions.Photos>();
                    if (status != PermissionStatus.Granted)
                    {
                        status = await Permissions.RequestAsync<Permissions.Photos>();
                        if (status != PermissionStatus.Granted)
                            return;
                    }

                    // ����������� ����������� � Photos (����������� �����)
                    await Photos.PHPhotoLibrary.RequestAuthorizationAsync();

                    // �������� ����� ��������� ���������� ����������
                    var library = Photos.PHPhotoLibrary.SharedPhotoLibrary;
                    if (library == null)
                        return;

                    // ������� ����������� �� �����
                    var image = UIKit.UIImage.FromFile(filePath);
                    if (image == null)
                        return;

                    // ��������� ����������� � �����������
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
                await DisplayAlert("��������", "��� ������������ ����", "OK");
                return;
            }

            try
            {
                loadingIndicator.IsVisible = true;
                loadingIndicator.IsRunning = true;

                // ��������� ������������ �����������
                var photoCopy = photo.Copy();

                // ������ ����� ��������������� �� ����� �����������
                using (var canvas = new SKCanvas(photoCopy))
                {
                    // ������ �����
                    DrawMarksToCanvas(canvas, true);
                }

                // ����������� ������� � ������ ��� ����������
                using (var image = SKImage.FromBitmap(photoCopy))
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100)) // PNG ��� ������� ��������
                {
                    var filePath = Path.Combine(FileSystem.CacheDirectory, $"SharedPhoto_{DateTime.Now:yyyyMMdd_HHmmss}.png");
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

            // ��������� ����� � ������������� ��������
            DrawMarksToCanvas(canvas, false);

            // ���� ������� ������ �����, ������ ��������� ������ ������ �����
            if (isWaitingForSecondPoint)
            {
                DrawTemporaryCircle(canvas);
            }
        }

        private void DrawTemporaryCircle(SKCanvas canvas)
        {
            // ���������� ��������� ������ ��� ������ �����
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

            // ������������ ������� ����� ������������ ����
            float photoWidth = photo.Width;
            float circleRadius = photoWidth * CircleRadiusPercent;
            float boxWidth = photoWidth * TextBoxWidthPercent;
            float boxHeight = photoWidth * TextBoxHeightPercent;
            float textSize = photoWidth * TextSizePercent;
            float strokeWidth = photoWidth * StrokeWidthPercent;
            float lineWidth = photoWidth * LineWidthPercent;

            // ��� �������� ���������� ����������� ��������
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
                // �������� ������� ���������
                SKPoint circlePosition, boxPosition;
                float actualCircleRadius, actualBoxWidth, actualBoxHeight, actualTextSize;
                float actualStrokeWidth, actualLineWidth;

                if (forExport)
                {
                    // ��� �������� ���������� ���������� ���������� ��������
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
                    // ��� ����������� �� ������ ����������� ���������� ���� � ��������
                    circlePosition = transformMatrix.MapPoint(mark.CirclePosition);
                    boxPosition = transformMatrix.MapPoint(mark.BoxPosition);

                    // ��������� ������� ��� ����������� �� ������
                    actualCircleRadius = circleRadius * scale;
                    actualBoxWidth = boxWidth * scale;
                    actualBoxHeight = boxHeight * scale;
                    actualTextSize = textSize * scale;
                    actualStrokeWidth = strokeWidth * scale;
                    actualLineWidth = lineWidth * scale;
                }

                // 1. ������ ������
                using (var paint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = new SKColor(255, 0, 0, 128), // �������������� �������
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

                // 2. ������ ������������� � �������
                var rect = new SKRect(
                    boxPosition.X - actualBoxWidth / 2,
                    boxPosition.Y - actualBoxHeight / 2,
                    boxPosition.X + actualBoxWidth / 2,
                    boxPosition.Y + actualBoxHeight / 2);

                using (var paint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = new SKColor(200, 200, 200, 230), // ������-����� � ��������� �������������
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

                // ������ ����� ������ ��������������
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
                                    boxPosition.Y + actualTextSize / 3, // ������������� ��� �������������
                                    paint);
                }

                // 3. ������ �������������� ����� ����� ������� � ��������� ����� ��������������
                using (var paint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.Black,
                    StrokeWidth = actualLineWidth,
                    IsAntialias = true
                })
                {
                    // ���������� ��������� � ������ ���� ��������������
                    SKPoint nearestCorner = GetNearestBoxCorner(circlePosition, boxPosition, actualBoxWidth, actualBoxHeight);
                    canvas.DrawLine(circlePosition, nearestCorner, paint);
                }
            }
        }

        // ����� ��� ����������� ���������� ���� �������������� � ����� �����
        private SKPoint GetNearestBoxCorner(SKPoint circlePosition, SKPoint boxPosition, float boxWidth, float boxHeight)
        {
            // ����������, � ����� ��������� ������������ ������ �������������� ��������� ������
            bool isCircleRightOfBox = circlePosition.X > boxPosition.X;
            bool isCircleBelowBox = circlePosition.Y > boxPosition.Y;

            // �������� ��������������� ����
            float cornerX = boxPosition.X + (isCircleRightOfBox ? 1 : -1) * (boxWidth / 2);
            float cornerY = boxPosition.Y + (isCircleBelowBox ? 1 : -1) * (boxHeight / 2);

            return new SKPoint(cornerX, cornerY);
        }

        private SKPoint ConvertToPhotoCoordinates(SKPoint viewPoint)
        {
            // ����������� ���������� ������ � ���������� ��������� ����
            SKMatrix invertedMatrix = new SKMatrix();
            if (!transformMatrix.TryInvert(out invertedMatrix))
                return viewPoint;

            SKPoint photoPoint = invertedMatrix.MapPoint(viewPoint);
            return photoPoint;
        }

        // ���������, ��������� �� ����� � �������� ����������
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

        private async void HandleMarkModeTouch(SKTouchEventArgs e)
        {
            if (e.ActionType == SKTouchAction.Released)
            {
                // ����������� ���������� ������� � ���������� ����
                SKPoint photoPoint = ConvertToPhotoCoordinates(e.Location);

                // ���������, ��������� �� ����� � �������� ����������
                if (IsPointInsidePhoto(photoPoint))
                {
                    if (!isWaitingForSecondPoint)
                    {
                        // ������ ������� - ��������� ������ ����� � ���� ������� �������
                        firstPoint = photoPoint;
                        isWaitingForSecondPoint = true;
                        canvasView.InvalidateSurface();
                    }
                    else
                    {
                        // ������ ������� - ����������� � ������������ �����
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
                string result = await DisplayPromptAsync("���� ������", "������� ����� (�� 0 �� 999):",
                    initialValue: "0", maxLength: 3, keyboard: Keyboard.Numeric);

                if (result != null && int.TryParse(result, out int number) && number >= 0 && number <= 999)
                {
                    // ������� ����� �����
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
                    // ������������ ����, ���������� ��������������
                    await DisplayAlert("������", "������� ����� �� 0 �� 999", "OK");
                    // ��������� ������
                    await PromptForMarkNumber(secondPoint);
                }
                else
                {
                    // ������������ ������� ����, �������� ��� �����
                    canvasView.InvalidateSurface();
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("������", $"��������� ������: {ex.Message}", "OK");
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
                        // ��������� ������� ���������������
                        var deltaX = e.Location.X - lastTouchPoint.X;
                        var deltaY = e.Location.Y - lastTouchPoint.Y;

                        panPosition.X += deltaX;
                        panPosition.Y += deltaY;

                        // ������������ ���������������
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

        // ����������� ���������������, ����� ����������� �� �������� �� ������� ������
        private void LimitPanningPosition()
        {
            if (photo == null) return;

            float canvasWidth = (float)canvasView.Width;
            float canvasHeight = (float)canvasView.Height;
            float photoWidth = photo.Width * scale;
            float photoHeight = photo.Height * scale;

            // ����� ������� ����������� - ���� ������ ������ ���� �� ������
            // ����������� �������� X - �������������: (photoWidth - ����������� ������� �����)
            // ������������ �������� X - �������������: (canvasWidth - ����������� ������� �����)
            float minVisiblePart = Math.Min(photoWidth, canvasWidth) * 0.25f; // ���� �� 25% ���� ������ ���� �����

            // ����������� �� �����������
            if (panPosition.X > canvasWidth - minVisiblePart)
                panPosition.X = canvasWidth - minVisiblePart;
            else if (panPosition.X + photoWidth < minVisiblePart)
                panPosition.X = minVisiblePart - photoWidth;

            // ����������� �� ��������� � ����� �� �������
            float minVisiblePartY = Math.Min(photoHeight, canvasHeight) * 0.25f;
            if (panPosition.Y > canvasHeight - minVisiblePartY)
                panPosition.Y = canvasHeight - minVisiblePartY;
            else if (panPosition.Y + photoHeight < minVisiblePartY)
                panPosition.Y = minVisiblePartY - photoHeight;
        }

        private void OnZoomSliderValueChanged(object sender, ValueChangedEventArgs e)
        {
            if (photo == null) return;

            // ��������� ������� ����� � ������ ������
            float canvasWidth = (float)canvasView.Width;
            float canvasHeight = (float)canvasView.Height;
            SKPoint centerScreen = new SKPoint(canvasWidth / 2, canvasHeight / 2);
            SKPoint beforeZoom = ConvertToPhotoCoordinates(centerScreen);

            // ������������� ����� ������� �� ��������
            scale = (float)e.NewValue;

            // ��������� ����� � ������� ��������� � ���������
            zoomValueLabel.Text = $"{scale * 100:0}%";

            // ��������� ������� �������������
            UpdateTransformMatrix();

            // ������������ �������, ����� ����� ������ ��������� � ��� �� ����� ����
            SKPoint afterZoom = ConvertToPhotoCoordinates(centerScreen);
            panPosition.X += (afterZoom.X - beforeZoom.X) * scale;
            panPosition.Y += (afterZoom.Y - beforeZoom.Y) * scale;

            // ����������� ���������������
            LimitPanningPosition();

            // ��� ��� ��������� ������� ����� ��������� �������
            UpdateTransformMatrix();

            // �������������� ������
            canvasView.InvalidateSurface();
        }

        #endregion

        #region ���������� �������� � ������ ��������

        private void OnToggleMarkModeClicked(object sender, EventArgs e)
        {
            isMarkMode = !isMarkMode;
            if (!isMarkMode)
            {
                // ��� ������ �� ������ �����, ���������� ��������� ��������
                isWaitingForSecondPoint = false;
            }
            toggleMarkModeButton.Text = isMarkMode ? "����� ���������" : "����� �����";
            canvasView.InvalidateSurface();
        }

        private void OnUndoClicked(object sender, EventArgs e)
        {
            if (isWaitingForSecondPoint)
            {
                // ���� ������� ������ �����, �������� ������� ��������
                isWaitingForSecondPoint = false;
                canvasView.InvalidateSurface();
            }
            else if (marks.Count > 0)
            {
                // ������� ��������� �����
                marks.RemoveAt(marks.Count - 1);
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