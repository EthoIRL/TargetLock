using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CircularBuffer;
using Emgu.CV;
using Emgu.CV.Cuda;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;

namespace TargetLock;

#pragma warning disable CA1416
class Program
{
    // (500, 300)
    private static readonly (int width, int height) Resolution = (500, 300);

    private static readonly (int width, int height, Inter method) WindowResolution = new(896, 504, Inter.Nearest);

    private static readonly bool AutoFire = false;
    private static readonly bool ShowDebug = false;

    /// <summary>
    /// Attempts to Sync to fps at the cost of latency for smoothness
    /// </summary>
    private static readonly bool WaitForNewFrame = false;

    private static readonly int Fps = 200;
    private static readonly double FpsInTicks = (1000.0 / (Fps / 2.0)) * 10000.0;

    private static readonly bool Slowdown = false;
    private static readonly int SlowRadius = 50;
    private static readonly double SlowSpeed = 0.2;

    private static readonly double SlowDivisorX = 1.5;
    private static readonly double SlowDivisorY = 2;

    private static readonly int CenterMouseX = Resolution.width / 2;
    private static readonly int CenterMouseY = Resolution.height / 2;

    private const int RedTolerance = 130;
    private const int GreenTolerance = 105;
    private const int BlueMinimum = 230;

    private const int BlueThreshold = 175;

    private static readonly bool UsePrediction = true;
    private static readonly Prediction Predictor = new(2);

    private static readonly Mat? Kernel = CvInvoke.GetStructuringElement(ElementShape.Rectangle, new Size(2, 2), new Point(-1, -1));

    private static readonly VectorOfVectorOfPoint Contours = new();
    private static readonly Mat Output = new();

    private static readonly double WidthRatio = (double) WindowResolution.width / Resolution.width;

    private static readonly double HeightRatio = (double) WindowResolution.height / Resolution.height;
    private static readonly int StridePixels = Resolution.width * 4;

    private static bool _lastSentLeft;

    private static readonly Socket Socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private static readonly IPAddress Broadcast = IPAddress.Parse("192.168.0.190");
    private static readonly IPEndPoint EndPoint = new(Broadcast, 7483);

    private static readonly Stopwatch ImageComputation = new();
    public static double ScreenshotSync;

    public static readonly Image<Bgra, byte> LocalImage = new(Resolution.width, Resolution.height);
    public static IntPtr LocalImageDataPtr;
    private static readonly Image<Gray, byte> GrayImage = new(Resolution.width, Resolution.height);
    
    private static Image<Bgra, byte> _originalView = new(Resolution.width, Resolution.height);

    private static readonly Stopwatch Waiter = new();

    private static readonly OrderablePartitioner<Tuple<int, int>> RangePartitioner = Partitioner.Create(0, Resolution.height);

    [SupportedOSPlatform("windows")]
    static void Main(string[] args)
    {
        Socket.Connect(EndPoint);
        if (ShowDebug)
        {
            new Thread(() =>
            {
                while (true)
                {
                    if (_originalView is {Data: { }})
                    {
                        CvInvoke.Imshow("Original View", _originalView);
                        CvInvoke.WaitKey(1);
                    }
                }
            }).Start();
        }

        ScreenCapturer.StartCapture(0, 0, Resolution.width, Resolution.height);
    }

    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    public static void HandleImage()
    {
        ImageComputation.Restart();

        Image<Bgra, byte> originalImage = null!;
        if (ShowDebug)
        {
            originalImage = LocalImage.Resize(WindowResolution.width, WindowResolution.height, WindowResolution.method);
        }

        unsafe
        {
            Parallel.ForEach(RangePartitioner, range =>
            {
                for (int y = range.Item1; y < range.Item2; y++)
                {
                    byte* currentLine = (byte*) (LocalImageDataPtr + y * LocalImage.MIplImage.WidthStep);

                    for (int x = 0; x < StridePixels; x += 4)
                    {
                        byte red = currentLine[x + 2];
                        byte green = currentLine[x + 1];
                        byte blue = currentLine[x];

                        bool isBlue = green <= GreenTolerance && red <= RedTolerance && blue >= BlueMinimum || blue - green > BlueThreshold && blue - red > BlueThreshold;

                        if (isBlue)
                        {
                            currentLine[x + 2] = 255;
                            currentLine[x + 1] = 255;
                            currentLine[x + 0] = 255;
                        }
                        else
                        {
                            currentLine[x + 2] = 0;
                            currentLine[x + 1] = 0;
                            currentLine[x + 0] = 0;
                        }
                    }
                }
            });
        }

        CvInvoke.CvtColor(LocalImage, GrayImage, ColorConversion.Bgr2Gray);

        CvInvoke.MorphologyEx(GrayImage, GrayImage, MorphOp.Dilate, Kernel, new Point(-1, -1), 2, BorderType.Replicate, new MCvScalar(255, 255, 255));

        CvInvoke.FindContours(GrayImage, Contours, Output, RetrType.External, ChainApproxMethod.ChainApproxNone);
        var contourArray = Contours.ToArrayOfArray();
        List<Rectangle> boundingBoxes = new List<Rectangle>(contourArray.Length);

        foreach (var contour in contourArray)
        {
            Rectangle rect = CvInvoke.BoundingRectangle(contour);

            boundingBoxes.Add(rect);
        }

        if (boundingBoxes.Count > 0)
        {
            if (ShowDebug)
            {
                foreach (var rect in boundingBoxes)
                {
                    var centerXLocal = rect.X + rect.Width / 2;
                    var centerYLocal = rect.Y + rect.Height / 2;

                    CvInvoke.Line(originalImage, new Point(WindowResolution.width / 2, WindowResolution.height / 2),
                        new Point((int) (centerXLocal * WidthRatio), (int) (centerYLocal * HeightRatio)), new MCvScalar(255, 255, 255));

                    var newRectangle = new Rectangle(new Point((int) (rect.X * WidthRatio), (int) (rect.Y * HeightRatio)),
                        new Size((int) (rect.Width * WidthRatio), (int) (rect.Height * HeightRatio)));
                    CvInvoke.Rectangle(originalImage, newRectangle, new MCvScalar(0, 255, 255));
                }
            }

            var validBoxes = boundingBoxes.OrderBy(rect =>
            {
                // Shortest distance to center
                var centerX = rect.X + rect.Width / 2.0;
                var lowestY = rect.Y + rect.Height;

                return Math.Sqrt(Math.Pow(Resolution.width / 2.0 - centerX, 2) + Math.Pow(Resolution.height / 2.0 - lowestY, 2));
            }).ThenBy(rect =>
            {
                // Visibility ordering
                var sizeRatio = rect.Height / (double) rect.Width;

                return sizeRatio - 1;
            }).ToList();

            var nearest = validBoxes.First();

            var centerX = (int) Math.Ceiling(nearest.X + nearest.Width / 2.0);
            var lowestY = nearest.Y + nearest.Height;

            var deltaX = centerX - CenterMouseX;
            var deltaY = lowestY - CenterMouseY;

            deltaX = (int) Math.Ceiling(deltaX / SlowDivisorX);
            deltaY = (int) Math.Ceiling(deltaY / SlowDivisorY);

            if (ShowDebug)
            {
                CvInvoke.Line(originalImage, new Point(WindowResolution.width / 2, WindowResolution.height / 2),
                    new Point((int) (centerX * WidthRatio), (int) (lowestY * HeightRatio)), new MCvScalar(255, 0, 255));
            }

            if (Slowdown)
            {
                if (Math.Abs(deltaX) > SlowRadius)
                {
                    deltaX = (int) Math.Floor(deltaX * SlowSpeed);
                }

                if (Math.Abs(deltaY) > SlowRadius)
                {
                    deltaY = (int) Math.Floor(deltaY * SlowSpeed);
                }
            }

            var leftFire = false;
            if (AutoFire)
            {
                if (nearest.Height / (double) nearest.Width > 0.65 && nearest.Width >= 8)
                {
                    var trueDeltaX = centerX - CenterMouseX;
                    var trueDeltaY = lowestY - CenterMouseY;

                    if (Math.Abs(trueDeltaY) < 5 && Math.Abs(trueDeltaX) < 2)
                    {
                        leftFire = true;
                        _lastSentLeft = true;
                    }
                    else
                    {
                        leftFire = false;
                        _lastSentLeft = false;
                    }
                }
            }

            if (UsePrediction)
            {
                var predictions = Predictor.HandlePredictions(deltaX, deltaY);

                Predictor.MouseStates.PushFront((deltaX, deltaY));

                var xDifference = predictions.deltaX - deltaX;
                var yDifference = predictions.deltaY - deltaY;
                if (xDifference != 0)
                {
                    Console.WriteLine($"X: {deltaX}, Prediction: {predictions.deltaX}, Diff: {Math.Abs(xDifference)}");
                }

                if (yDifference != 0)
                {
                    Console.WriteLine($"Y: {deltaY}, Prediction: {predictions.deltaY}, Diff: {Math.Abs(yDifference)}");
                }

                deltaX = predictions.deltaX;
                deltaY = predictions.deltaY;
            }

            var data = PreparePacket((short) deltaX, (short) deltaY, false, leftFire);

            Socket.Send(data);
        }
        else
        {
            if (_lastSentLeft)
            {
                var data = PreparePacket(0, 0);
                Socket.Send(data);
                _lastSentLeft = false;
            }
        }

        if (originalImage != null)
        {
            _originalView = originalImage;
        }

        ImageComputation.Stop();

        if (WaitForNewFrame)
        {
            var delayedSleep = FpsInTicks - ScreenshotSync - ImageComputation.ElapsedTicks - 50;

            if (delayedSleep > 0)
            {
                Waiter.Restart();
                while (true)
                {
                    if (Waiter.ElapsedTicks >= (int) delayedSleep)
                    {
                        break;
                    }
                }
            }
        }
    }

    private static byte[] PreparePacket(short deltaX, short deltaY, bool ignoreAim = false, bool left = false, bool right = false, bool middle = false)
    {
        FromShort(deltaX, out var byte1, out var byte2);
        FromShort(deltaY, out var byte3, out var byte4);

        return new[]
        {
            byte1, byte2, byte3, byte4,
            (byte) (ignoreAim ? 1 : 0),
            (byte) (left ? 1 : 0),
            (byte) (right ? 1 : 0),
            (byte) (middle ? 1 : 0)
        };
    }

    private static void FromShort(short number, out byte byte1, out byte byte2)
    {
        byte2 = (byte) (number >> 8);
        byte1 = (byte) (number >> 0);
    }
}
#pragma warning restore CA1416