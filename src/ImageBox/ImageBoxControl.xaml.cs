using System;
using System.Collections;
using System.Linq;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ImageBox;

/// <summary>
/// ImageBox控件：提供图片显示、缩放和平移功能
/// </summary>
public partial class ImageBoxControl : UserControl
{
    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.Register(
            nameof(ImageSource),
            typeof(ImageSource),
            typeof(ImageBoxControl),
            new PropertyMetadata(null, OnImageSourceChanged));

    public static readonly DependencyProperty OverlayContentProperty =
        DependencyProperty.Register(
            nameof(OverlayContent),
            typeof(object),
            typeof(ImageBoxControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty OverlayItemsProperty =
        DependencyProperty.Register(
            nameof(OverlayItems),
            typeof(IEnumerable),
            typeof(ImageBoxControl),
            new PropertyMetadata(null, OnOverlayItemsChanged));

    #region 私有字段
    
    private Point _lastMousePosition;
    private bool _isPanning = false;
    private double _currentScale = 1.0;
    private const double _minScale = 0.1;
    private const double _maxScale = 10.0;
    private const double _scaleRate = 1.2; // 每次缩放的比例
    private bool _isImageLoaded = false;
    private Point _imageCenter = new Point(0, 0); // 图像中心点
    private const double _fitMargin = 0.98; // 自动适应时的边距系数（修复高度溢出问题）
    private INotifyCollectionChanged? _overlayItemsNotifier;
    
    // 用于检测双击的字段
    private DateTime _lastClickTime = DateTime.MinValue;
    private const double _doubleClickTimeThreshold = 300; // 双击时间阈值（毫秒）
    
    #endregion

    #region 公共属性
    
    /// <summary>
    /// 获取或设置当前缩放比例
    /// </summary>
    public double Scale
    {
        get { return _currentScale; }
        set
        {
            if (value < _minScale) value = _minScale;
            if (value > _maxScale) value = _maxScale;
            
            _currentScale = value;
            UpdateImageScale();
            UpdateScaleInfo();
        }
    }
    
    /// <summary>
    /// 获取或设置图像源
    /// </summary>
    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    /// <summary>
    /// 获取或设置随图像一起缩放和平移的覆盖层内容。
    /// </summary>
    public object? OverlayContent
    {
        get => GetValue(OverlayContentProperty);
        set => SetValue(OverlayContentProperty, value);
    }

    public IEnumerable? OverlayItems
    {
        get => (IEnumerable?)GetValue(OverlayItemsProperty);
        set => SetValue(OverlayItemsProperty, value);
    }
    
    /// <summary>
    /// 获取或设置是否显示亮度值
    /// </summary>
    public bool ShowBrightnessInfo { get; set; } = false;
    
    #endregion

    public ImageBoxControl()
    {
        InitializeComponent();
        
        // 添加大小改变事件处理，确保图像在控件大小变化时调整
        SizeChanged += (s, e) => 
        {
            if (_isImageLoaded && DisplayImage.Source != null)
            {
                FitImageToView();
            }
        };
        
        // 控件加载完成后执行
        Loaded += (s, e) =>
        {
            if (_isImageLoaded && DisplayImage.Source != null)
            {
                FitImageToView();
            }
        };
        
        // 为图像添加加载完成事件
        DisplayImage.Loaded += (s, e) =>
        {
            if (_isImageLoaded && DisplayImage.Source != null)
            {
                FitImageToView();
            }
        };
        ShowBrightnessInfo = true;
        // 初始化亮度值显示
        BrightnessInfoText.Visibility = ShowBrightnessInfo ? Visibility.Visible : Visibility.Collapsed;
        SetDisplayImage(ImageSource);
    }

    private static void OnImageSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is ImageBoxControl control && control.IsInitialized)
        {
            control.SetDisplayImage(e.NewValue as ImageSource);
        }
    }

    private static void OnOverlayItemsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is ImageBoxControl control && control.IsInitialized)
        {
            control.UpdateOverlayItemsSubscription(e.OldValue, e.NewValue);
            control.RenderOverlayItems();
        }
    }

    private void UpdateOverlayItemsSubscription(object? oldValue, object? newValue)
    {
        if (_overlayItemsNotifier != null)
        {
            _overlayItemsNotifier.CollectionChanged -= OnOverlayItemsCollectionChanged;
        }

        _overlayItemsNotifier = newValue as INotifyCollectionChanged;
        if (_overlayItemsNotifier != null)
        {
            _overlayItemsNotifier.CollectionChanged += OnOverlayItemsCollectionChanged;
        }
    }

    private void OnOverlayItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RenderOverlayItems();
    }

    private void SetDisplayImage(ImageSource? value)
    {
        DisplayImage.Source = value;
        if (value != null)
        {
            _isImageLoaded = true;
            ImageLayer.Width = value.Width;
            ImageLayer.Height = value.Height;
            RenderOverlayItems();
            // 图像加载后自动调整
            FitImageToView();
            return;
        }

        _isImageLoaded = false;
        ImageLayer.Width = double.NaN;
        ImageLayer.Height = double.NaN;
        OverlayCanvas.Children.Clear();
        ResetTransforms();
        // 清空坐标显示
        UpdateCoordinateInfo(new Point(0, 0));
    }

    #region 公共方法
    
    /// <summary>
    /// 从文件加载图像
    /// </summary>
    /// <param name="filePath">图像文件路径</param>
    public void LoadImage(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(filePath);
            bitmap.EndInit();
            
            ImageSource = bitmap;
            
            // 在UI线程中延迟执行，确保图像已完全加载
            Dispatcher.BeginInvoke(new Action(() => 
            {
                FitImageToView();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载图像失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 从内存加载图像
    /// </summary>
    /// <param name="imageData">图像字节数据</param>
    public void LoadImageFromBytes(byte[] imageData)
    {
        try
        {
            if (imageData == null || imageData.Length == 0)
                return;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(imageData);
            bitmap.EndInit();
            
            ImageSource = bitmap;
            
            // 在UI线程中延迟执行，确保图像已完全加载
            Dispatcher.BeginInvoke(new Action(() => 
            {
                FitImageToView();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载图像失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 重置视图
    /// </summary>
    public void ResetView()
    {
        if (!_isImageLoaded || DisplayImage.Source == null)
            return;
            
        FitImageToView();
    }

    /// <summary>
    /// 将图像居中显示
    /// </summary>
    public void CenterImage()
    {
        if (!_isImageLoaded || DisplayImage.Source == null)
            return;

        // 重置平移变换
        ImageTranslateTransform.X = 0;
        ImageTranslateTransform.Y = 0;

        double canvasWidth = MainCanvas.ActualWidth;
        double canvasHeight = MainCanvas.ActualHeight;
        double imageWidth = DisplayImage.Source.Width;
        double imageHeight = DisplayImage.Source.Height;

        if (canvasWidth <= 0 || canvasHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
            return;

        // RenderTransform does not affect ActualWidth/ActualHeight. Center by the scaled visual size.
        var scaledWidth = imageWidth * _currentScale;
        var scaledHeight = imageHeight * _currentScale;

        _imageCenter = new Point(canvasWidth / 2, canvasHeight / 2);
        Canvas.SetLeft(ImageLayer, (canvasWidth - scaledWidth) / 2);
        Canvas.SetTop(ImageLayer, (canvasHeight - scaledHeight) / 2);
    }
    
    /// <summary>
    /// 自动缩放图像以适应视图
    /// </summary>
    public void FitImageToView()
    {
        if (!_isImageLoaded || DisplayImage.Source == null)
            return;
            
        // 等待布局完成后执行
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                // 重置所有变换
                ResetTransforms();
                
                // 重置Canvas位置
                Canvas.SetLeft(ImageLayer, 0);
                Canvas.SetTop(ImageLayer, 0);
                
                // 获取图像原始大小
                double imageWidth = DisplayImage.Source.Width;
                double imageHeight = DisplayImage.Source.Height;
                
                // 获取Canvas的可见区域大小
                double canvasWidth = MainCanvas.ActualWidth;
                double canvasHeight = MainCanvas.ActualHeight;
                
                if (canvasWidth <= 0 || canvasHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
                    return;
                
                // 计算缩放因子以适应Canvas
                double scaleX = canvasWidth / imageWidth;
                double scaleY = canvasHeight / imageHeight;
                
                // 使用较小的缩放因子以确保图像完全可见，并应用边距系数
                _currentScale = Math.Min(scaleX, scaleY) * _fitMargin;
                
                // 限制缩放范围
                _currentScale = Math.Max(_minScale, Math.Min(_maxScale, _currentScale));
                
                // 更新缩放变换
                UpdateImageScale();
                
                // 居中显示图像
                CenterImage();
                
                // 更新缩放信息显示
                UpdateScaleInfo();
            }
            catch (Exception ex)
            {
                // 防止异常导致UI崩溃
                Console.WriteLine($"自动适应图像错误: {ex.Message}");
            }
        }), System.Windows.Threading.DispatcherPriority.Render);
    }
    
    /// <summary>
    /// 重置所有变换
    /// </summary>
    public void ResetTransforms()
    {
        // 重置缩放
        ImageScaleTransform.ScaleX = 1.0;
        ImageScaleTransform.ScaleY = 1.0;
        _currentScale = 1.0;
        
        // 重置平移
        ImageTranslateTransform.X = 0;
        ImageTranslateTransform.Y = 0;
    }
    
    /// <summary>
    /// 启用或禁用亮度值显示
    /// </summary>
    /// <param name="enable">是否启用</param>
    public void EnableBrightnessInfo(bool enable)
    {
        ShowBrightnessInfo = enable;
        BrightnessInfoText.Visibility = enable ? Visibility.Visible : Visibility.Collapsed;
    }
    
    #endregion

    #region 私有方法

    /// <summary>
    /// 更新图像缩放
    /// </summary>
    private void UpdateImageScale()
    {
        if (!_isImageLoaded || DisplayImage.Source == null)
            return;

        // 使用ScaleTransform更新缩放
        ImageScaleTransform.ScaleX = _currentScale;
        ImageScaleTransform.ScaleY = _currentScale;
        UpdateFixedScreenOverlayScale();
    }

    /// <summary>
    /// 更新缩放信息显示
    /// </summary>
    private void UpdateScaleInfo()
    {
        ScaleInfoText.Text = $"缩放: {_currentScale * 100:0}%";
    }

    private void RenderOverlayItems()
    {
        OverlayCanvas.Children.Clear();
        if (OverlayItems == null || DisplayImage.Source == null)
        {
            return;
        }

        OverlayCanvas.Width = DisplayImage.Source.Width;
        OverlayCanvas.Height = DisplayImage.Source.Height;

        foreach (var item in OverlayItems.OfType<ImageOverlayItem>().Where(item => item.IsVisible))
        {
            var element = CreateOverlayElement(item);
            if (element == null)
            {
                continue;
            }

            OverlayCanvas.Children.Add(element);
        }
    }

    private FrameworkElement? CreateOverlayElement(ImageOverlayItem item)
    {
        var element = item.Kind switch
        {
            ImageOverlayKind.Rectangle => CreateRectangle(item, rotate: false),
            ImageOverlayKind.RotatedRectangle => CreateRectangle(item, rotate: true),
            ImageOverlayKind.Line => CreateLine(item),
            ImageOverlayKind.Polyline => CreatePolyline(item),
            ImageOverlayKind.Polygon => CreatePolygon(item),
            ImageOverlayKind.Circle => CreateCircle(item),
            ImageOverlayKind.Cross => CreateCross(item),
            ImageOverlayKind.Text => CreateText(item),
            _ => null
        };

        ApplyFixedScreenScale(element, item);
        return element;
    }

    private static FrameworkElement CreateRectangle(ImageOverlayItem item, bool rotate)
    {
        var rectangle = new Rectangle
        {
            Width = item.Width,
            Height = item.Height,
            Stroke = item.Stroke,
            Fill = item.Fill,
            StrokeThickness = item.StrokeThickness,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        if (rotate)
        {
            rectangle.RenderTransform = new RotateTransform(item.Angle);
        }

        Canvas.SetLeft(rectangle, item.X);
        Canvas.SetTop(rectangle, item.Y);
        return rectangle;
    }

    private static FrameworkElement? CreateLine(ImageOverlayItem item)
    {
        if (item.Points.Count < 2)
        {
            return null;
        }

        return new Line
        {
            X1 = item.Points[0].X,
            Y1 = item.Points[0].Y,
            X2 = item.Points[1].X,
            Y2 = item.Points[1].Y,
            Stroke = item.Stroke,
            StrokeThickness = item.StrokeThickness
        };
    }

    private static FrameworkElement? CreatePolyline(ImageOverlayItem item)
    {
        if (item.Points.Count == 0)
        {
            return null;
        }

        return new Polyline
        {
            Points = new PointCollection(item.Points),
            Stroke = item.Stroke,
            StrokeThickness = item.StrokeThickness,
            Fill = item.Fill
        };
    }

    private static FrameworkElement? CreatePolygon(ImageOverlayItem item)
    {
        if (item.Points.Count == 0)
        {
            return null;
        }

        return new Polygon
        {
            Points = new PointCollection(item.Points),
            Stroke = item.Stroke,
            StrokeThickness = item.StrokeThickness,
            Fill = item.Fill
        };
    }

    private static FrameworkElement CreateCircle(ImageOverlayItem item)
    {
        var diameter = Math.Max(0, item.Radius * 2);
        var ellipse = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Stroke = item.Stroke,
            Fill = item.Fill,
            StrokeThickness = item.StrokeThickness
        };
        Canvas.SetLeft(ellipse, item.X - item.Radius);
        Canvas.SetTop(ellipse, item.Y - item.Radius);
        return ellipse;
    }

    private static FrameworkElement CreateCross(ImageOverlayItem item)
    {
        var radius = item.Radius <= 0 ? 8 : item.Radius;
        var group = new Canvas
        {
            Width = radius * 2,
            Height = radius * 2
        };

        group.Children.Add(new Line
        {
            X1 = 0,
            Y1 = radius,
            X2 = radius * 2,
            Y2 = radius,
            Stroke = item.Stroke,
            StrokeThickness = item.StrokeThickness
        });
        group.Children.Add(new Line
        {
            X1 = radius,
            Y1 = 0,
            X2 = radius,
            Y2 = radius * 2,
            Stroke = item.Stroke,
            StrokeThickness = item.StrokeThickness
        });

        Canvas.SetLeft(group, item.X - radius);
        Canvas.SetTop(group, item.Y - radius);
        return group;
    }

    private static FrameworkElement CreateText(ImageOverlayItem item)
    {
        var text = new TextBlock
        {
            Text = item.Text ?? string.Empty,
            Foreground = item.Foreground,
            FontSize = item.FontSize
        };
        Canvas.SetLeft(text, item.X);
        Canvas.SetTop(text, item.Y);
        return text;
    }

    private void ApplyFixedScreenScale(FrameworkElement? element, ImageOverlayItem item)
    {
        if (element == null || !item.IsSizeFixedToScreen)
        {
            return;
        }

        element.Tag = item;
        element.RenderTransformOrigin = item.Kind == ImageOverlayKind.Text
            ? new Point(0, 0)
            : new Point(0.5, 0.5);
        element.RenderTransform = CreateFixedScreenTransform(item);
    }

    private double GetFixedScreenScale()
    {
        return _currentScale <= 0
            ? 1
            : 1 / _currentScale;
    }

    private void UpdateFixedScreenOverlayScale()
    {
        var scale = GetFixedScreenScale();
        foreach (var element in OverlayCanvas.Children.OfType<FrameworkElement>())
        {
            if (element.Tag is not ImageOverlayItem { IsSizeFixedToScreen: true } item)
            {
                continue;
            }

            element.RenderTransform = CreateFixedScreenTransform(item);
        }
    }

    private Transform CreateFixedScreenTransform(ImageOverlayItem item)
    {
        var scale = GetFixedScreenScale();
        if (item.ScreenOffsetX == 0 && item.ScreenOffsetY == 0)
        {
            return new ScaleTransform(scale, scale);
        }

        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(scale, scale));
        group.Children.Add(new TranslateTransform(item.ScreenOffsetX * scale, item.ScreenOffsetY * scale));
        return group;
    }
    
    /// <summary>
    /// 更新坐标信息显示
    /// </summary>
    private void UpdateCoordinateInfo(Point mousePoint)
    {
        // 只在图像已加载时更新坐标
        if (!_isImageLoaded || DisplayImage.Source == null)
        {
            CoordinateInfoText.Text = "坐标: (-, -)";
            return;
        }
        
        try
        {
            // 计算鼠标在图像坐标系中的位置
            Point imagePosition = TransformToImageCoordinates(mousePoint);
            
            // 检查是否在图像范围内
            bool isInImage = IsPointInImage(imagePosition);
            
            // 更新坐标显示
            if (isInImage)
            {
                int x = (int)Math.Round(imagePosition.X);
                int y = (int)Math.Round(imagePosition.Y);
                CoordinateInfoText.Text = $"坐标: ({x}, {y})";
                
                // 如果启用了亮度显示，则更新亮度值
                if (ShowBrightnessInfo)
                {
                    string brightnessValue = GetPixelBrightness(x, y);
                    
                    // 判断是RGB值还是灰度值
                    if (brightnessValue.StartsWith("R:"))
                    {
                        BrightnessInfoText.Text = $"颜色: {brightnessValue}";
                    }
                    else
                    {
                        BrightnessInfoText.Text = $"灰度: {brightnessValue}";
                    }
                }
            }
            else
            {
                CoordinateInfoText.Text = "坐标: (-, -)";
                if (ShowBrightnessInfo)
                {
                    BrightnessInfoText.Text = "亮度: -";
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"更新坐标信息错误: {ex.Message}");
            CoordinateInfoText.Text = "坐标: (-, -)";
        }
    }
    
    /// <summary>
    /// 将Canvas坐标转换为图像坐标
    /// </summary>
    /// <param name="canvasPoint">Canvas上的点</param>
    /// <returns>对应的图像坐标</returns>
    private Point TransformToImageCoordinates(Point canvasPoint)
    {
        // 获取图像在Canvas中的位置
        double imageLeft = Canvas.GetLeft(ImageLayer);
        double imageTop = Canvas.GetTop(ImageLayer);
        
        // 处理可能的NaN值
        if (double.IsNaN(imageLeft)) imageLeft = 0;
        if (double.IsNaN(imageTop)) imageTop = 0;
        
        // 考虑平移变换
        imageLeft += ImageTranslateTransform.X;
        imageTop += ImageTranslateTransform.Y;
        
        // 计算相对于图像的坐标
        double relativeX = canvasPoint.X - imageLeft;
        double relativeY = canvasPoint.Y - imageTop;
        
        // 考虑缩放因子，转换为图像原始坐标
        double originalX = relativeX / _currentScale;
        double originalY = relativeY / _currentScale;
        
        return new Point(originalX, originalY);
    }
    
    /// <summary>
    /// 判断点是否在图像范围内
    /// </summary>
    private bool IsPointInImage(Point point)
    {
        if (DisplayImage.Source == null)
            return false;
            
        double width = DisplayImage.Source.Width;
        double height = DisplayImage.Source.Height;
        
        return point.X >= 0 && point.X < width && point.Y >= 0 && point.Y < height;
    }
    
    /// <summary>
    /// 获取图像指定位置的亮度值
    /// </summary>
    /// <param name="x">X坐标</param>
    /// <param name="y">Y坐标</param>
    /// <returns>亮度值（RGB或灰度）</returns>
    private string GetPixelBrightness(int x, int y)
    {
        try
        {
            // 对于BitmapSource，使用CopyPixels方法获取像素值
            if (DisplayImage.Source is BitmapSource bitmapSource)
            {
                // 检查坐标是否在图像范围内
                if (x < 0 || x >= bitmapSource.PixelWidth || y < 0 || y >= bitmapSource.PixelHeight)
                    return "-";
                
                // 计算一行的字节数（stride）
                int bytesPerPixel = (bitmapSource.Format.BitsPerPixel + 7) / 8;
                int stride = bitmapSource.PixelWidth * bytesPerPixel;
                
                // 创建足够大的缓冲区来存储像素数据
                byte[] pixels = new byte[stride];
                
                // 定义要读取的区域（仅读取包含目标像素的一行）
                Int32Rect rect = new Int32Rect(0, y, bitmapSource.PixelWidth, 1);
                
                // 复制像素数据
                bitmapSource.CopyPixels(rect, pixels, stride, 0);
                
                // 计算目标像素在数组中的索引
                int index = x * bytesPerPixel;
                
                // 根据图像格式返回不同的亮度值
                if (bitmapSource.Format == PixelFormats.Bgr24 || 
                    bitmapSource.Format == PixelFormats.Rgb24 || 
                    bitmapSource.Format == PixelFormats.Bgra32 ||
                     bitmapSource.Format == PixelFormats.Bgr32 ||
                    bitmapSource.Format == PixelFormats.Pbgra32)
                {
                    // 彩色图像，获取RGB值
                    byte b = pixels[index];
                    byte g = pixels[index + 1];
                    byte r = pixels[index + 2];
                    
                    // 返回RGB格式的字符串
                    return $"R:{r}, G:{g}, B:{b}";
                }
                else if (bitmapSource.Format == PixelFormats.Gray8 || 
                         bitmapSource.Format == PixelFormats.Gray16)
                {
                    // 灰度图像，直接返回灰度值
                    byte gray = pixels[index];
                    return $"{gray}";
                }
                else
                {
                    // 对于其他格式，可以尝试转换为灰度值
                    // 为简单起见，这里只处理常见的格式
                    return "不支持的格式";
                }
            }
            
            // 对于其他类型的图像源，返回不支持的提示
            return "不支持的图像源";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"获取亮度值错误: {ex.Message}");
            return "错误";
        }
    }
    
    /// <summary>
    /// 检查是否为双击
    /// </summary>
    /// <returns>是否是双击</returns>
    private bool IsDoubleClick()
    {
        DateTime now = DateTime.Now;
        TimeSpan timeSinceLastClick = now - _lastClickTime;
        
        // 如果距离上次点击的时间小于阈值，则认为是双击
        bool isDoubleClick = timeSinceLastClick.TotalMilliseconds < _doubleClickTimeThreshold;
        
        // 更新上次点击时间
        _lastClickTime = now;
        
        return isDoubleClick;
    }

    #endregion

    #region 事件处理程序

    private void MainCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_isImageLoaded || DisplayImage.Source == null)
            return;

        // 获取鼠标在Canvas上的位置
        Point mousePosition = e.GetPosition(MainCanvas);
        
        // 存储旧的缩放值用于计算
        double oldScale = _currentScale;
        
        // 根据鼠标滚轮方向计算新的缩放值
        if (e.Delta > 0)
            _currentScale *= _scaleRate; // 放大
        else
            _currentScale /= _scaleRate; // 缩小
            
        // 限制缩放范围
        _currentScale = Math.Max(_minScale, Math.Min(_maxScale, _currentScale));
        
        // 更新缩放变换
        UpdateImageScale();
        
        // 更新缩放比例显示
        UpdateScaleInfo();
        
        // 计算缩放中心点相对于当前图像位置的偏移
        Point imagePosition = new Point(
            Canvas.GetLeft(ImageLayer) + ImageTranslateTransform.X,
            Canvas.GetTop(ImageLayer) + ImageTranslateTransform.Y
        );
        
        Point relativePosition = new Point(
            mousePosition.X - imagePosition.X,
            mousePosition.Y - imagePosition.Y
        );
        
        // 计算新的位置以保持鼠标指向的点不变
        double scaleFactor = _currentScale / oldScale;
        double newX = mousePosition.X - relativePosition.X * scaleFactor;
        double newY = mousePosition.Y - relativePosition.Y * scaleFactor;
        
        // 更新平移变换
        ImageTranslateTransform.X += newX - imagePosition.X;
        ImageTranslateTransform.Y += newY - imagePosition.Y;
        
        // 更新鼠标坐标信息
        UpdateCoordinateInfo(mousePosition);
        
        e.Handled = true;
    }

    private void MainCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isImageLoaded || DisplayImage.Source == null)
            return;
            
        // 检查是否是双击
        if (IsDoubleClick())
        {
            // 双击时自动适应视图大小
            FitImageToView();
            e.Handled = true;
            return;
        }

        // 单击处理
        _lastMousePosition = e.GetPosition(MainCanvas);
        _isPanning = true;
        MainCanvas.Cursor = Cursors.Hand;
        MainCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void MainCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        MainCanvas.Cursor = Cursors.Arrow;
        MainCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void MainCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        // 获取鼠标位置并更新坐标信息
        Point mousePosition = e.GetPosition(MainCanvas);
        UpdateCoordinateInfo(mousePosition);
        
        // 处理拖动
        if (_isPanning && e.LeftButton == MouseButtonState.Pressed && DisplayImage.Source != null)
        {
            // 计算移动距离
            Vector moveVector = mousePosition - _lastMousePosition;
            
            // 更新平移变换
            ImageTranslateTransform.X += moveVector.X;
            ImageTranslateTransform.Y += moveVector.Y;
            
            // 更新上一次鼠标位置
            _lastMousePosition = mousePosition;
            
            e.Handled = true;
        }
    }

    #endregion
}

