using System;
using System.IO;
using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;


/// <summary>
/// Утилита для кодирования/декодирования текста в бинарный формат на основе рангов символов.
/// </summary>
/// <remarks>
/// Программа поддерживает операцию кодирования и декодирования файлов через командную строку.
/// Формат выходного файла содержит заголовок, длину исходного текста, таблицу символ->ранг,
/// список размеров блоков и массивы закодированных BigInteger-блоков.
/// </remarks>
class Program
{
    /// <summary>
    /// Служит точкой входа для приложения, обрабатывающего аргументы командной строки для выполнения операций кодирования и/или декодирования текста.
    /// </summary>
    /// <remarks>
    /// Метод ожидает как минимум два аргумента командной строки:
    /// - Первый аргумент — режим работы: "encode" или "decode".
    /// - Второй аргумент — путь к входному файлу.
    /// При недостаточном количестве аргументов печатается справка и выполнение завершается.
    /// </remarks>
    /// <param name="args">Массив аргументов командной строки. Первый аргумент определяет тип исполняемой операции. Второй аргумент определяет путь к входному файлу.</param>

    static void Main(string[] args)
    {
        // Проверяем, что передано достаточно аргументов командной строки
        if (args.Length < 2)
        {
            PrintHelp();
            return;
        }
        if (args[0] != "encode" && args[0] != "decode")
        {
            PrintHelp();
            return;
        }

        string mode = args[0];
        string filePath = args[1];

        if (mode == "encode")
        {
            // Читаем весь текст из входного файла и преобразуем его в массив символов для дальнейшей обработки.
            char[] data = File.ReadAllText(args[1])
            .ToCharArray();

            // Группируем символы по их значению и подсчитываем количество вхождений каждого символа, формируя словарь символ->количество.
            var charCounts = data
                .GroupBy(c => c)
                .ToDictionary(g => g.Key, g => g.Count());

            // Сортируем символы по убыванию их количества и формируем список символов в порядке убывания частоты.
            // Чтобы обеспечить стабильную сортировку при одинаковой частоте, добавляем вторичный критерий сортировки по символу (по возрастанию).
            var sortedChars = charCounts
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key)
                .Select(x => x.Key)
                .ToList();

            // Создаем словарь, сопоставляющий каждому символу его ранг (позицию в отсортированном списке), начиная с 1.
            var charToRank = sortedChars
                .Select((c, i) => new { Char = c, Rank = i + 1 })
                .ToDictionary(x => x.Char, x => x.Rank);

            int blockSize = 10000;
            int numBlocks = (int)Math.Ceiling((double)data.Length / blockSize);  // Вычисляем количество блоков, округляя вверх, чтобы учесть остаток символов в последнем блоке 

            var blocks = new char[numBlocks][];
            var blockSizes = new int[numBlocks];

            for (int i = 0; i < numBlocks; i++)
            {
                int blockStart = i * blockSize;
                int blockLength = Math.Min(blockSize, data.Length - blockStart); // Вычисляем фактическую длину блока, которая может быть меньше blockSize для последнего блока
                blocks[i] = data.Skip(blockStart).Take(blockLength).ToArray();   // Из массива data пропускаем blockStart символов и берем blockLength символов для текущего блока
                blockSizes[i] = blockLength;                                     // Сохраняем фактическую длину блока в массив blockSizes
            }

            // Кодируем каждый блок текста в большое целое число, используя функцию EncodeBlock и словарь charToRank для определения рангов символов.
            // Результаты кодирования сохраняются в массив encodedBlocks.
            var encodedBlocks = blocks
                .Select(b => EncodeBlock(b, charToRank))
                .ToArray();

            // Формируем путь к выходному файлу, изменяя расширение ".ssr" исходного файла, и сохраняем закодированные блоки, размеры блоков,
            // словарь символ->ранг и длину исходного текста в бинарный файл с помощью функции EncodeToFile.
            string outputFilePath = Path.ChangeExtension(filePath, ".ssr");
            EncodeToFile(outputFilePath, encodedBlocks, blockSizes, charToRank, data.Length);
        }

        else if (mode == "decode")
        {
            // Читаем закодированные данные из входного .ssr-файла, восстанавливая массив закодированных блоков, массив размеров блоков,
            var (encodedBlocks, blockSizes, charToRank, originalTextLength) = ReadFromFile(filePath);

            var rankToChar = charToRank
                .ToDictionary(kvp => kvp.Value, kvp => kvp.Key);                // Создаем обратный словарь, сопоставляющий ранг символу, для использования при декодировании блоков обратно в текст.

            // Декодируем все блоки, используя функцию DecodeAllBlocks, которая принимает массив закодированных блоков, массив размеров блоков и словарь ранг->символ.
            char[] decodedText = DecodeAllBlocks(encodedBlocks, blockSizes, rankToChar);
             
            string result = new string(decodedText, 0, originalTextLength);     // Преобразуем массив декодированных символов в строку, учитывая оригинальную длину текста, чтобы исключить возможные лишние символы из последнего блока.
            string outputFilePath = Path.ChangeExtension(filePath, ".decoded.txt");
            File.WriteAllText(outputFilePath, result);
        }

    }

    /// <summary>
    /// Функция для печати справки по использованию программы. Вызывается, если аргументы командной строки не соответствуют ожидаемому формату.
    /// </summary>
    static void PrintHelp()
    {
        Console.WriteLine("Использование:");
        Console.WriteLine("  ssr encode <file>  - закодировать файл");
        Console.WriteLine("  ssr decode <file>  - раскодировать файл .ssr");
    }

    /// <summary>
    /// Кодирует блок текста в большое целое число, используя ранги символов для определения их позиций в алфавите. 
    /// Функция из диссертации по ссылке: "https://sfu.ru/sapi/file-upload/72046a099580fa2c971231c63c3df51d.pdf" (стр. 252. Алгоритм 6.11).
    /// </summary>
    /// <remarks>
    /// Каждый символ в блоке преобразуется в свой ранг в словаре <paramref name="charToRank"/>, затем из этих рангов формируется число:
    /// значение символа используется как цифра в системе с основанием <c>charToRank.Count</c>.
    /// </remarks>
    /// <param name="block">Массив символов, представляющий кодируемый блок текста.</param>
    /// <param name="charToRank">Словарь соответствий символ -> ранг. Должен содержать все символы из <paramref name="block"/>.</param>
    /// <returns>
    /// Возвращает <see cref="BigInteger"/>, представляющее закодированный блок текста.</returns>
    static BigInteger EncodeBlock(char[] block, Dictionary<char, int> charToRank)
    {

        BigInteger result = 0;
        for (int i = block.Length - 1; i >= 0; i--)
        {
            int rank = charToRank[block[i]];
            result = (rank - 1) + result * charToRank.Count;
        }
        return result;
    }

    /// <summary>
    /// Декодирует блок символов из заданного числового значения, используя отображение рангов в символы.
    /// </summary>
    /// <param name="encoded">Числовое значение, представляющее закодированный блок символов для декодирования.</param>
    /// <param name="rankToChar">Словарь, сопоставляющий ранги символам, используемый для преобразования числовых рангов в соответствующие
    /// символы.</param>
    /// <param name="blockSize">Размер блока, определяющий количество символов, которые будут декодированы из значения.</param>
    /// <returns>Массив символов, содержащий декодированный блок. Длина массива соответствует значению параметра blockSize.</returns>
    static char[] DecodeBlock(BigInteger encoded, Dictionary<int, char> rankToChar, int blockSize)
    {
        char[] blockText = new char[blockSize];
        for (int i = 0; i < blockSize; i++)
        {
            int rank = (int)(encoded % rankToChar.Count);
            char c = rankToChar[rank + 1];
            blockText[i] = c;
            encoded = encoded / rankToChar.Count;
        }
        return blockText;
    }

    /// <summary>
    /// Декодирует все закодированные блоки и объединяет результаты в один массив символов.
    /// </summary>
    /// <param name="encodedBlocks">Массив <see cref="BigInteger"/>, каждый элемент которого представляет одно закодированное значение блока текста.</param>
    /// <param name="blockSizes">Массив, содержащий количество символов в каждом соответствующем блоке. Длина этого массива должна совпадать с длиной <paramref name="encodedBlocks"/>.</param>
    /// <param name="rankToChar">Словарь, отображающий числовой ранг символа в сам символ (<c>rank</c> -> <c>char</c>).
    /// Должен содержать все ранги, используемые в закодированных блоках (ранги начинаются с 1).</param>
    /// <returns>
    /// Массив символов, полученный объединением декодированных блоков в порядке их расположения в <paramref name="encodedBlocks"/>.
    /// Длина возвращаемого массива равна сумме значений в <paramref name="blockSizes"/>.
    /// </returns>
    static char[] DecodeAllBlocks(
        BigInteger[] encodedBlocks, 
        int[] blockSizes,
        Dictionary<int, char> rankToChar)
    {
        var result = new List<char>();
        for (int i = 0; i < encodedBlocks.Length; i++)
        {
            char[] blockText = DecodeBlock(encodedBlocks[i], rankToChar, blockSizes[i]);
            result.AddRange(blockText);
        }
        return result.ToArray();
    }

    /// <summary>
    /// Сохраняет закодированные блоки, информацию о размерах блоков, рангах символов и длине исходного текста в
    /// бинарный файл по указанному пути.
    /// </summary>
    /// <remarks>Формат выходного файла включает заголовок, длину исходного текста, количество уникальных
    /// символов с их рангами, количество блоков, размеры блоков и сами закодированные блоки. Метод перезаписывает файл,
    /// если он уже существует.</remarks>
    /// <param name="outputFilePath">Путь к выходному файлу, в который будут записаны закодированные данные. Не может быть равен null или пустой строке.</param>
    /// <param name="encodedBlocks">Массив закодированных блоков, которые будут сохранены в файл. Каждый элемент представляет собой отдельный блок данных.</param>
    /// <param name="blockSizes">Массив размеров для каждого закодированного блока. Длина массива должна соответствовать количеству элементов в <paramref name="encodedBlocks"/>.</param>
    /// <param name="charRanks">Словарь, сопоставляющий символы их рангам, используемый для декодирования. Ключ — символ, значение — ранг.</param>
    /// <param name="originalTextLength">Длина исходного текста, который был закодирован. Используется для восстановления исходных данных при декодировании.</param>
    static void EncodeToFile(
        string outputFilePath,
        BigInteger[] encodedBlocks,
        int[] blockSizes,
        Dictionary<char, int> charRanks,
        int originalTextLength)
    {
        using (var fs = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            // Пишем заголовок
            bw.Write(0x535352);
            // Пишем длину исходного текста
            bw.Write(originalTextLength);
            // Пишем количество уникальных символов
            bw.Write(charRanks.Count);

            // Пишем символы и их ранги
            foreach (var kvp in charRanks)
            {
                bw.Write(kvp.Key);
                bw.Write(kvp.Value);
            }

            // Информация о блоках
            bw.Write(encodedBlocks.Length);
            foreach (var blockSize in blockSizes)
            {
                bw.Write(blockSize);
            }
            foreach (var encoded in encodedBlocks)
            {
                var bytes = encoded.ToByteArray();
                bw.Write(bytes.Length);
                bw.Write(bytes);
            }
            Console.WriteLine($"Данные сохранены в {outputFilePath}");
        }
    }

    /// <summary>
    /// Читает ранее записанный бинарный .ssr-файл и восстанавливает:
    /// - массив закодированных блоков (<see cref="BigInteger"/>[]),
    /// - массив размеров блоков (int[]),
    /// - словарь символ->ранг (Dictionary<char,int>),
    /// - длину исходного текста (int).
    /// </summary>
    /// <param name="inputFilePath">Путь к входному .ssr-файлу для чтения.</param>
    /// <returns>
    /// Кортеж (<see cref="BigInteger[]"/>, <see cref="int[]"/>, <see cref="Dictionary{Char,Int32}"/>, <see cref="int"/>),
    /// содержащий данные в том порядке, в котором они были сохранены методом <see cref="EncodeToFile"/>.
    /// </returns>
    static (BigInteger[], int[], Dictionary<char, int>, int) ReadFromFile(string inputFilePath)
    {
        using (var fs = new FileStream(inputFilePath, FileMode.Open, FileAccess.Read))
        using (var br = new BinaryReader(fs))
        {
            // Чтение заголовка файла и проверка его корректности. Ожидается, что первые 4 байта файла будут равны 0x535352, что служит маркером правильного формата данных.
            int header = br.ReadInt32();
            if (header != 0x535352)
                throw new InvalidDataException("Неверный формат файла");

            // Чтение метаданных из файла (длина исходного текста, количество уникальных символов,
            int originalTextLength = br.ReadInt32();
            int charCount = br.ReadInt32();

            // Чтение таблицы символ->ранг из файла и сохранение ее в словарь charRanks для дальнейшего использования при декодировании.
            Dictionary<char, int> charRanks = new Dictionary<char, int>();
            for (int i = 0; i < charCount; i++)
            {
                char c = br.ReadChar();
                int rank = br.ReadInt32();
                charRanks[c] = rank;
            }

            // Работа с блоками данных:
            // Чтение количества блоков.
            int blockCount = br.ReadInt32();

            // Чтение массива размеров блоков.
            int[] blockSizes = new int[blockCount];
            for (int i = 0; i < blockCount; i++)
            {
                blockSizes[i] = br.ReadInt32();
            }

            // Чтение массива закодированных блоков.
            BigInteger[] encodedBlocks = new BigInteger[blockCount];
            for (int i = 0; i < blockCount; i++)
            {
                int byteLength = br.ReadInt32();
                byte[] bytes = br.ReadBytes(byteLength);
                encodedBlocks[i] = new BigInteger(bytes);
            }
            // Возвращаем прочитанные данные в виде кортежа, который будет использоваться для декодирования текста.
            return (encodedBlocks, blockSizes, charRanks, originalTextLength);
        }
    }


}