using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using Resource = SharpDX.DXGI.Resource;

namespace TargetLock;

#pragma warning disable CA1416
public class ScreenCapturer
{
    private static readonly Stopwatch ScWatch = new();
    private static readonly List<long> Timings = new();

    public static void StartCapture(Int32 adapterIndex, Int32 displayIndex, int outputWidth, int outputHeight)
    {
        using Factory1 factory1 = new Factory1();
        using Adapter1 adapter1 = factory1.GetAdapter1(adapterIndex);
        using Device device = new Device(adapter1);
        using Output output = adapter1.GetOutput(displayIndex);
        using Output1 output1 = output.QueryInterface<Output1>();

        Int32 width = output1.Description.DesktopBounds.Right - output1.Description.DesktopBounds.Left;
        Int32 height = output1.Description.DesktopBounds.Bottom - output1.Description.DesktopBounds.Top;

        int centerWidth = width / 2 - outputWidth / 2;
        int centerHeight = height / 2 - outputHeight / 2;

        Texture2DDescription texture2DDescription = new Texture2DDescription
        {
            CpuAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None,
            Format = Format.B8G8R8A8_UNorm,
            Width = outputWidth,
            Height = outputHeight,
            OptionFlags = ResourceOptionFlags.None,
            MipLevels = 0,
            ArraySize = 1,
            SampleDescription =
            {
                Count = 1,
                Quality = 0
            },
            Usage = ResourceUsage.Staging
        };

        using Texture2D texture2D = new Texture2D(device, texture2DDescription);
        using OutputDuplication outputDuplication = output1.DuplicateOutput(device);

        DataBox dataBox = device.ImmediateContext.MapSubresource(texture2D, 0, MapMode.Read, MapFlags.None);
        
        ResourceRegion resourceRegion = new ResourceRegion(centerWidth, centerHeight, 0, width, height, 1);
        
        bool previousState = false;
        
        Image<Bgra, byte> dest = new Image<Bgra, byte>(outputWidth, outputHeight);

        GCHandle pinnedArray = GCHandle.Alloc(dest.Data, GCHandleType.Pinned);
        IntPtr dataPointer = pinnedArray.AddrOfPinnedObject();

        var heightPartitioner = Partitioner.Create(0, outputHeight);
        
        while (true)
        {
            ScWatch.Restart();

            if (previousState)
            {
                outputDuplication.ReleaseFrame();
            }
            
            var status = outputDuplication.TryAcquireNextFrame(0, out var data, out var screenResource);
            previousState = status.Success;
            
            if (screenResource == null || data.LastPresentTime == 0)
            {
                continue;
            }

            var screenTexture2D = screenResource.QueryInterface<Texture2D>();
            device.ImmediateContext.CopySubresourceRegion(screenTexture2D, 0, resourceRegion, texture2D, 0);

            Parallel.ForEach(heightPartitioner, range =>
            {
                for (int y = range.Item1; y < range.Item2; y++)
                {
                    IntPtr currentDataBoxPointer = IntPtr.Add(dataBox.DataPointer, y * dataBox.RowPitch);
                    IntPtr currentBitmapDataPointer = IntPtr.Add(dataPointer, y * dest.MIplImage.WidthStep);
                    Utilities.CopyMemory(currentBitmapDataPointer, currentDataBoxPointer, outputWidth * 4);
                }
            });
            
            Program.LocalImage.Data = dest.Data;
            Program.ScreenshotSync = ScWatch.ElapsedTicks;

            screenTexture2D.Dispose();
            screenResource.Dispose();
            
            Program.HandleImage();
            ScWatch.Stop();

            if (!ScWatch.IsRunning)
            {
                Timings.Add(ScWatch.ElapsedTicks);
            }

            if (Timings.Count != 0 && Timings.Count % 100 == 0)
            {
                Console.WriteLine($"Timings SC Avg: {Timings.Average() / 10000.0}");
            }
        }
    }
}