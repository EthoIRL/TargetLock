using System.Runtime.InteropServices;

namespace TargetLock;

public class DataConverter
{
    public static byte[] StructListToBytes<T>(List<T> list) where T : struct
    {
        int structSize = Marshal.SizeOf<T>();
        byte[] bytes = new byte[structSize * list.Count];

        IntPtr ptr = Marshal.AllocHGlobal(structSize);

        try
        {
            for (int i = 0; i < list.Count; i++)
            {
                Marshal.StructureToPtr(list[i], ptr, false);
                Marshal.Copy(ptr, bytes, i * structSize, structSize);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        return bytes;
    }
    
    public static List<T> BytesToStructList<T>(byte[] bytes) where T : struct
    {
        int structSize = Marshal.SizeOf<T>();
        int count = bytes.Length / structSize;
        List<T> list = new List<T>(count);

        IntPtr ptr = Marshal.AllocHGlobal(structSize);

        try
        {
            for (int i = 0; i < count; i++)
            {
                Marshal.Copy(bytes, i * structSize, ptr, structSize);
                T item = Marshal.PtrToStructure<T>(ptr);
                list.Add(item);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        return list;
    }
}