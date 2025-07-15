using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace TargetLock;

#pragma warning disable CA1416
public static class ScreenCapturer
{
    private static readonly Stopwatch CwWatch = new();
    
    private static readonly List<long> TimingsCw = new(1000000);

    public static DataBox GpuImage;
    
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetProcessDpiAwarenessContext(int dpiFlag);

    internal enum DPI_AWARENESS_CONTEXT
    {
        DPI_AWARENESS_CONTEXT_UNAWARE = 16,
        DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = 17,
        DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = 18,
        DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = 34
    }

    public static void StartCapture(Int32 adapterIndex, Int32 displayIndex, int outputWidth, int outputHeight)
    {
        SetProcessDpiAwarenessContext((int)DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        var factory = new Factory1();
        var adapter = factory.GetAdapter1(0);
        var output = adapter.GetOutput(0);

        Output5 output5 = output.QueryInterface<Output5>();
        
        using Device device = new Device(adapter);
        using DeviceContext deviceContext = device.ImmediateContext;

        Int32 width = output5.Description.DesktopBounds.Right - output5.Description.DesktopBounds.Left;
        Int32 height = output5.Description.DesktopBounds.Bottom - output5.Description.DesktopBounds.Top;

        int centerWidth = width / 2 - outputWidth / 2;
        int centerHeight = height / 2 - outputHeight / 2;

        Texture2DDescription texture2DDescription = new Texture2DDescription
        {
            CpuAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None,
            // 32 bit ( 8 + 8 + 8 + 8 = 4)
            // 64 bit ( 8 + 8 + 8 + 8 + 8 + 8 + 8 + 8 = 8)
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
        // using OutputDuplication outputDuplication = output5.DuplicateOutput1(device, 1, 1, [Format.R10G10B10A2_UNorm]);
        using OutputDuplication outputDuplication = output5.DuplicateOutput1(device, 1, 1, [Format.B8G8R8A8_UNorm]);

        GpuImage = deviceContext.MapSubresource(texture2D, 0, MapMode.Read, MapFlags.None);

        ResourceRegion resourceRegion = new ResourceRegion(centerWidth, centerHeight, 0, centerWidth + outputWidth, centerHeight + outputHeight, 1);
        
        bool previousState = false;

        Texture2D texturePtr = null!;
        bool firstRun = true;
        
        while (true)
        {
            if (previousState)
            {
                outputDuplication.ReleaseFrame();
            }

            var status = outputDuplication.TryAcquireNextFrame(0, out var data, out var screenResource);
            previousState = status.Success;

            if (data.LastPresentTime == 0 || screenResource == null)
            {
                continue;
            }

            if (firstRun)
            {
                screenResource.QueryInterface(typeof(Texture2D).GUID, out var screenPtr);
                texturePtr = CppObject.FromPointer<Texture2D>(screenPtr);
                firstRun = false;
            }
            
            deviceContext.CopySubresourceRegion(texturePtr, 0, resourceRegion, texture2D, 0);

            CwWatch.Restart();
            bool compute = false;
            Program.HandleImage(ref compute);
            screenResource.Dispose();
            CwWatch.Stop();

            if (compute && !CwWatch.IsRunning)
            {
                TimingsCw.Add(CwWatch.ElapsedTicks);
            }
            
            if (TimingsCw.Count != 0 && TimingsCw.Count % 100 == 0)
            {
                Console.WriteLine($"Timings CAL Avg: ({TimingsCw.Average() / 10000.0}, {TimingsCw.Count})");
            }
        }
    }
}