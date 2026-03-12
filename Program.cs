using System.Numerics;
using System.Text;

namespace Ranking_text_files;

class Program
{
    static void Main(string[] args)
    {
        string path = "D:\\Projects\\text-ranging\\content\\cantrbry\\alice29.txt";

        byte[] data = File.ReadAllBytes(path);
        int dataLength = data.Length;

        Console.WriteLine($"Длина данных: {dataLength} байт");

        HashSet<byte> hashAlph = new HashSet<byte>(data);
        Dictionary<byte, int> byteToIndex = hashAlph
            .OrderBy(b => b)
            .Select((b, index) => new { b, index })
            .ToDictionary(x => x.b, x => x.index);

        Dictionary<int, byte> indexToByte = byteToIndex
            .ToDictionary(x => x.Value, x => x.Key);

        int alphabetSize = byteToIndex.Count;
        Console.WriteLine($"Размер алфавита: {alphabetSize} байт");

        int blockSize = 10000;
        int blockCount = (int)Math.Ceiling((double)dataLength / blockSize);
        Console.WriteLine($"Количество блоков: {blockCount}");

        byte[][] dataBlocks = Enumerable.Range(0, blockCount)
            .Select(i =>
            {
                int startIndex = i * blockSize;
                int length = Math.Min(blockSize, dataLength - startIndex);
                byte[] block = new byte[blockSize];
                Array.Copy(data, startIndex, block, 0, length);
                for (int j = length; j < blockSize; j++)
                {
                    block[j] = length > 0 ? block[length - 1] : (byte)0;
                }
                return block;
            })
            .ToArray();


        Console.WriteLine("Длина алфавита и блоков рассчитана");
        Console.WriteLine("Длина алфавита: " + alphabetSize);

        EncodeToFile(data, byteToIndex, "encoded.dat");

        Console.WriteLine("Файл закодирован");

        DecodeFile("encoded.dat", "decoded.txt");

        Console.WriteLine("Файл декодирован");

    }



    // Сохранение закодированных данных, алфавита и размера блоков для декодирования
    public static void EncodeToFile(
        byte[] data,
        Dictionary<byte, int> byteToIndex,
        string path)
    {
        using FileStream fs = new FileStream(path, FileMode.Create);
        using BinaryWriter writer = new BinaryWriter(fs);

        int originalLength = data.Length;

        writer.Write(originalLength);

        writer.Write(byteToIndex.Count);

        foreach (var kv in byteToIndex.OrderBy(x => x.Value))
            writer.Write(kv.Key);

        int bitsPerSymbol = BitsPerSymbol(byteToIndex.Count);

        writer.Write(bitsPerSymbol);

        BitWriter bitWriter = new BitWriter(fs);

        foreach (byte b in data)
        {
            int index = byteToIndex[b];
            bitWriter.WriteBits((uint)index, bitsPerSymbol);
        }

        bitWriter.Flush();
    }

    // Загрузка закодированных данных, алфавита и размера блоков для декодирования
    public static void DecodeFile(string encodedPath, string outputPath)
    {
        using FileStream fs = new FileStream(encodedPath, FileMode.Open);
        using BinaryReader reader = new BinaryReader(fs);

        int originalLength = reader.ReadInt32();

        int alphabetSize = reader.ReadInt32();

        Dictionary<int, byte> indexToByte = new();

        for (int i = 0; i < alphabetSize; i++)
        {
            byte symbol = reader.ReadByte();
            indexToByte[i] = symbol;
        }

        int bitsPerSymbol = reader.ReadInt32();

        BitReader bitReader = new BitReader(fs);

        byte[] output = new byte[originalLength];

        for (int i = 0; i < originalLength; i++)
        {
            uint index = bitReader.ReadBits(bitsPerSymbol);
            output[i] = indexToByte[(int)index];
        }

        File.WriteAllBytes(outputPath, output);
    }

    public static int BitsPerSymbol(int alphabetSize)
    {
        return (int)Math.Ceiling(Math.Log2(alphabetSize));
    }
}


// Класс для записи битов в поток
public class BitWriter
{
    private readonly Stream stream;
    private byte currentByte;
    private int bitPosition;

    public BitWriter(Stream stream)
    {
        this.stream = stream;
    }

    public void WriteBits(uint value, int bitCount)
    {
        for (int i = bitCount - 1; i >= 0; i--)
        {
            int bit = (int)((value >> i) & 1);

            currentByte |= (byte)(bit << (7 - bitPosition));
            bitPosition++;

            if (bitPosition == 8)
            {
                stream.WriteByte(currentByte);
                currentByte = 0;
                bitPosition = 0;
            }
        }
    }

    public void Flush()
    {
        if (bitPosition > 0)
            stream.WriteByte(currentByte);
    }
}

// Класс для чтения битов из потока
public class BitReader
{
    private readonly Stream stream;
    private int currentByte;
    private int bitPosition = 8;

    public BitReader(Stream stream)
    {
        this.stream = stream;
    }

    public uint ReadBits(int bitCount)
    {
        uint result = 0;

        for (int i = 0; i < bitCount; i++)
        {
            if (bitPosition == 8)
            {
                currentByte = stream.ReadByte();
                bitPosition = 0;
            }

            int bit = (currentByte >> (7 - bitPosition)) & 1;
            bitPosition++;

            result = (result << 1) | (uint)bit;
        }

        return result;
    }
}