﻿// #define DEBUG
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
using Emgu.CV.Util;
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

    private static readonly VectorOfVectorOfPoint Contours = new();
    private static readonly Mat Output = new();

    private static readonly int StridePixels = Resolution.width * 4;

    private static readonly Socket Socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private static readonly IPAddress Broadcast = IPAddress.Parse("192.168.68.56");
    private static readonly IPEndPoint EndPoint = new(Broadcast, 7483);

    private static readonly Stopwatch ImageComputation = new();
    public static double ScreenshotSync;

    #if DEBUG
    private static readonly double WidthRatio = (double) WindowResolution.width / Resolution.width;
    private static readonly double HeightRatio = (double) WindowResolution.height / Resolution.height;
    private static readonly Image<Bgra, byte> LocalImage = new(Resolution.width, Resolution.height);
    private static IntPtr _localImageDataPtr;
    private static Image<Bgra, byte> _originalView = new(Resolution.width, Resolution.height);
    #endif

    private static readonly Image<Gray, byte> GrayImage = new(Resolution.width, Resolution.height);
    private static IntPtr _grayImageDataPtr;

    private static readonly Stopwatch Waiter = new();

    private static readonly OrderablePartitioner<Tuple<int, int>> RangePartitioner = Partitioner.Create(0, Resolution.height);

    private static readonly PidController PidControllerX = new(0.95, 0.04, 0.5);
    private static readonly PidController PidControllerY = new(0.95, 0.04, 0.5);

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
        #endif

        _grayImageDataPtr = GCHandle.Alloc(GrayImage.Data, GCHandleType.Pinned).AddrOfPinnedObject();

        ScreenCapturer.StartCapture(0, 0, Resolution.width, Resolution.height);
    }

    public static void HandleImage()
    {
        ImageComputation.Restart();

        bool compute = false;
        unsafe
        {
            Parallel.ForEach(RangePartitioner, range =>
            {
                for (int y = range.Item1; y < range.Item2; y++)
                {
                    byte* currentLine = (byte*) (ScreenCapturer.GpuImage.DataPointer + y * ScreenCapturer.GpuImage.RowPitch);
                    byte* grayLine = (byte*) (_grayImageDataPtr + y * GrayImage.MIplImage.WidthStep);

                    #if DEBUG
                    var imagePointerOffset = _localImageDataPtr + (y * LocalImage.MIplImage.WidthStep);
                    Utilities.CopyMemory(imagePointerOffset, (IntPtr) currentLine, StridePixels);
                    #endif

                    for (int x = 0; x < StridePixels; x += 4)
                    {
                        byte red = currentLine[x + 2];
                        byte green = currentLine[x + 1];
                        byte blue = currentLine[x];

                        bool isBlue = green <= GreenTolerance && red <= RedTolerance && blue >= BlueMinimum || blue - green > BlueThreshold && blue - red > BlueThreshold;

                        if (isBlue)
                        {
                            if (!compute)
                            {
                                compute = true;
                            }

                            grayLine[x >> 2] = 255;
                        }
                        else
                        {
                            grayLine[x >> 2] = 0;
                        }
                    }
                }
            });
        }

        #if DEBUG
        Image<Bgra, byte> originalImage = LocalImage.Resize(WindowResolution.width, WindowResolution.height, WindowResolution.method);
        #endif

        if (compute)
        {
            CvInvoke.FindContours(GrayImage, Contours, Output, RetrType.External, ChainApproxMethod.ChainApproxSimple);
        }

        if (Contours.Length != 0 && compute)
        {
            Span<Rectangle> boundingBoxes = stackalloc Rectangle[Contours.Size];
            for (int i = 0; i < Contours.Size; i++)
            {
                boundingBoxes[i] = CvInvoke.BoundingRectangle(Contours[i]);
            }

            #if DEBUG
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
            #endif

            (int id, double distance) near = (0, 99999);
            for (int i = 0; i < boundingBoxes.Length; i++)
            {
                var rect = boundingBoxes[i];

                var rectCenterX = rect.X + rect.Width / 2.0;
                var rectLowestY = rect.Y + rect.Height;

                var distanceToCenter = Math.Sqrt(Math.Pow(CenterMouseX - rectCenterX, 2) + Math.Pow(CenterMouseY - rectLowestY, 2));

                if (distanceToCenter < near.distance)
                {
                    near = (i, distanceToCenter);
                }
            }

            var nearest = boundingBoxes[near.id];

            double centerX = nearest.X + nearest.Width / 2.0;
            int lowestY = nearest.Y + nearest.Height;

            double deltaX = centerX - CenterMouseX;
            double deltaY = lowestY - CenterMouseY;

            deltaX /= SlowDivisorX;
            deltaY /= SlowDivisorY;

            #if DEBUG
            CvInvoke.Line(originalImage, new Point(WindowResolution.width / 2, WindowResolution.height / 2),
                new Point((int) (centerX * WidthRatio), (int) (lowestY * HeightRatio)), new MCvScalar(255, 255, 0));
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

                {
                    {
                    }
                }
            }

            if (Math.Abs(deltaX) > 50 || Math.Abs(deltaY) > 50)
            {
                if (UsePrediction)
                {
                    Predictor.Reset();
                }

                PidControllerX.Reset();
                PidControllerY.Reset();
            }

            if (UsePrediction)
            {
                var predictions = Predictor.HandlePredictions(deltaX, deltaY);

                #if DEBUG
                CvInvoke.Line(originalImage, new Point(WindowResolution.width / 2, WindowResolution.height / 2),
                    new Point((int) ((predictions.deltaX + CenterMouseX) * WidthRatio), (int) ((predictions.deltaY + CenterMouseY) * HeightRatio)), new MCvScalar(107, 255, 50));
                CvInvoke.Circle(originalImage, new Point((int)((predictions.deltaX + CenterMouseX) * WidthRatio), (int)((predictions.deltaY + CenterMouseY) * HeightRatio)), 2, new MCvScalar(255, 255, 255), 4, LineType.Filled);
                #endif

                deltaX = predictions.deltaX;
                deltaY = predictions.deltaY;
            }

            var stepX = (short) PidControllerX.Calculate(deltaX);
            var stepY = (short) PidControllerY.Calculate(deltaY);

            Socket.Send(PreparePacket(stepX, stepY, false, leftFire));
        }
        else
        {
            PidControllerX.Reset();
            PidControllerY.Reset();

            Predictor.Reset();

            {
                var data = PreparePacket(0, 0, false, false);
                Socket.Send(data);
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