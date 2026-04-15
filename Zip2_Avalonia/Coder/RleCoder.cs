using System;
using System.IO;

namespace Zip2_Avalonia.Coder;

public class RleCoder
{
    /// <summary>
    /// Кодирование
    /// </summary>
    public static void Encode(Stream input, Stream output)
    {
        int prev = -1;
        int count = 0;

        int current;
        while ((current = input.ReadByte()) != -1)
        {
            if (prev == -1)
            {
                prev = current;
                count = 1;
                continue;
            }

            if (current == prev)
            {
                count++;
            }
            else
            {
                WriteEncoded(output, (byte)prev, count);
                prev = current;
                count = 1;
            }
        }

        if (prev != -1)
            WriteEncoded(output, (byte)prev, count);
    }

    private static void WriteEncoded(Stream output, byte value, int count)
    {
        if (count > 2)
        {
            while (count > 255)
            {
                output.WriteByte(0xFF);
                output.WriteByte(255);
                output.WriteByte(value);
                count -= 255;
            }

            output.WriteByte(0xFF);
            output.WriteByte((byte)count);
            output.WriteByte(value);
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                if (value == 0xFF)
                {
                    // экранирование
                    output.WriteByte(0xFF);
                    output.WriteByte(0x00);
                }
                else
                {
                    output.WriteByte(value);
                }
            }
        }
    }
    
    /// <summary>
    /// Декодирование
    /// </summary>
    public static void Decode(Stream input, Stream output)
    {
        int current;

        while ((current = input.ReadByte()) != -1)
        {
            if (current == 0xFF)
            {
                int next = input.ReadByte();

                if (next == -1)
                    throw new Exception("Invalid stream");

                if (next == 0x00)
                {
                    output.WriteByte(0xFF);
                }
                else
                {
                    int count = next;

                    int value = input.ReadByte();
                    if (value == -1)
                        throw new Exception("Invalid stream");

                    for (int i = 0; i < count; i++)
                        output.WriteByte((byte)value);
                }
            }
            else
            {
                output.WriteByte((byte)current);
            }
        }
    }
}