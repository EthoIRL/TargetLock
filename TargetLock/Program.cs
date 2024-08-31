// #define DEBUG

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using SharpDX;

namespace TargetLock;

#pragma warning disable CA1416
class Program
{
    // (500, 300)
    private static readonly (int width, int height) Resolution = (300, 300);

    #if DEBUG
    private static readonly (int width, int height, Inter method) WindowResolution = new(896, 504, Inter.Nearest);
    #endif

    /// <summary>
    /// Attempts to Sync to fps at the cost of latency for smoothness
    /// </summary>
    private static readonly bool WaitForNewFrame = false;

    private static readonly int Fps = 300;
    private static readonly double FpsInTicks = (1000.0 / Fps) * 10000.0;

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

    private static readonly bool UsePrediction = false;
    private static readonly Prediction Predictor = new(1.4);

    private static readonly int StridePixels = Resolution.width * 4;

    private static readonly Socket Socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private static readonly IPAddress Broadcast = IPAddress.Parse("192.168.68.59");
    private static readonly IPEndPoint EndPoint = new(Broadcast, 7483);

    private static readonly Stopwatch ImageComputation = new();
    public static double ScreenshotSync;

    #if DEBUG
    private static readonly double WidthRatio = (double) WindowResolution.width / Resolution.width;
    private static readonly double HeightRatio = (double) WindowResolution.height / Resolution.height;
    private static readonly Image<Bgra, byte> LocalImage = new(Resolution.width, Resolution.height);
    private static IntPtr _localImageDataPtr;
    private static Image<Bgra, byte> _originalView = new(Resolution.width, Resolution.height);
    private static readonly Image<Gray, byte> GrayImage = new(Resolution.width, Resolution.height);
    private static IntPtr _grayImageDataPtr;
    #endif

    private static readonly Stopwatch Waiter = new();

    private static readonly ParallelOptions ParallelizationOptions = new()
    {
        MaxDegreeOfParallelism = Environment.ProcessorCount
    };

    [SupportedOSPlatform("windows")]
    static void Main(string[] args)
    {
        Socket.Connect(EndPoint);
        #if DEBUG
        new Thread(() =>
        {
            while (true)
            {
                if (_originalView is {Data: { }})
                {
                    CvInvoke.Imshow("Original View", _originalView);
                }

                CvInvoke.Imshow("Filtered View", GrayImage);
                CvInvoke.WaitKey(1);
            }
        }).Start();

        _localImageDataPtr = GCHandle.Alloc(LocalImage.Data, GCHandleType.Pinned).AddrOfPinnedObject();
        _grayImageDataPtr = GCHandle.Alloc(GrayImage.Data, GCHandleType.Pinned).AddrOfPinnedObject();
        #endif

        ScreenCapturer.StartCapture(0, 0, Resolution.width, Resolution.height);
    }

    public static void HandleImage()
    {
        var compute = false;

        ImageComputation.Restart();
        (int x, int y, double distance) closest = (0, -Int32.MaxValue, Double.MaxValue);

        unsafe
        {
            Parallel.For(0, Resolution.height, ParallelizationOptions, y =>
            {
                byte* currentLine = (byte*) (ScreenCapturer.GpuImage.DataPointer + y * ScreenCapturer.GpuImage.RowPitch);
                // Span<byte> currentLine = new Span<byte>((byte*)ScreenCapturer.GpuImage.DataPointer + y * ScreenCapturer.GpuImage.RowPitch, ScreenCapturer.GpuImage.RowPitch);

                #if DEBUG
                        byte* grayLine = (byte*) (_grayImageDataPtr + y * GrayImage.MIplImage.WidthStep);
                        var imagePointerOffset = _localImageDataPtr + (y * LocalImage.MIplImage.WidthStep);
                        Utilities.CopyMemory(imagePointerOffset, (IntPtr) currentLine, StridePixels);
                #endif

                for (int x = 0; x < StridePixels; x += 4)
                {
                    byte red = currentLine[x + 2];
                    byte green = currentLine[x + 1];
                    byte blue = currentLine[x];

                    var isBlue = IsBlue(red, green, blue);

                    if (isBlue)
                    {
                        if (!compute)
                        {
                            compute = true;
                        }

                        var distance = Math.Sqrt(Math.Pow(CenterMouseX - (x >> 2), 2) + Math.Pow(CenterMouseY - y, 2));

                        if (distance - closest.distance <= 10 && y > closest.y)
                        {
                            closest = (x >> 2, y, distance);
                        }
                    }

                    #if DEBUG
                            if (isBlue)
                            {
                                grayLine[x >> 2] = 255;
                            }
                            else
                            {
                                grayLine[x >> 2] = 0;
                            }
                    #endif
                }
            });
        }

        #if DEBUG
            Image<Bgra, byte> originalImage = LocalImage.Resize(WindowResolution.width, WindowResolution.height, WindowResolution.method);
        #endif

        if (compute && closest.y != -Int32.MaxValue)
        {
            double deltaX = closest.x - CenterMouseX;
            double deltaY = closest.y - CenterMouseY;

            deltaX /= SlowDivisorX;
            deltaY /= SlowDivisorY;

            #if DEBUG
                CvInvoke.Line(originalImage, new Point(WindowResolution.width / 2, WindowResolution.height / 2),
                    new Point((int) ((closest.x) * WidthRatio), (int) (closest.y * HeightRatio)), new MCvScalar(255, 255, 0));
            #endif

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

            if (UsePrediction)
            {
                if (Math.Abs(deltaX) > 50 || Math.Abs(deltaY) > 50)
                {
                    Predictor.Reset();
                }

                var predictions = Predictor.HandlePredictions(deltaX, deltaY);

                // Task.Run(() =>
                // {
                //     Console.WriteLine($"DeltaX: {deltaX} | DeltaY: {deltaY}");
                //     Console.WriteLine($"PDeltaX: {predictions.deltaX} | PDeltaY: {predictions.deltaY}");
                // });

                #if DEBUG
                    CvInvoke.Line(originalImage, new Point(WindowResolution.width / 2, WindowResolution.height / 2),
                        new Point((int) ((predictions.deltaX + CenterMouseX) * WidthRatio), (int) ((predictions.deltaY + CenterMouseY) * HeightRatio)),
                        new MCvScalar(107, 255, 50));
                    CvInvoke.Circle(originalImage, new Point((int) ((predictions.deltaX + CenterMouseX) * WidthRatio), (int) ((predictions.deltaY + CenterMouseY) * HeightRatio)),
                        2,
                        new MCvScalar(255, 255, 255), 4, LineType.Filled);
                #endif

                deltaX = predictions.deltaX;
                deltaY = predictions.deltaY;
            }

            Task.Run(() => Socket.Send(PreparePacket((short) deltaX, (short) deltaY)));
        }
        else
        {
            if (UsePrediction)
            {
                Predictor.Reset();
            }
        }

        #if DEBUG
            if (originalImage != null)
            {
                _originalView = originalImage;
            }
        #endif

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

    private static bool IsBlue(byte red, byte green, byte blue)
    {
        return green <= GreenTolerance && red <= RedTolerance && blue >= BlueMinimum || blue - green > BlueThreshold && blue - red > BlueThreshold;
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