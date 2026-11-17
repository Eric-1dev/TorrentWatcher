using System.Security.Cryptography;

namespace TorrentWatcher.Domain.Parsing;

/// <summary>
/// Извлекает из .torrent название, infohash v1, суммарный размер, число файлов и флаг private.
/// Infohash считается по исходным байтам info-словаря, без повторной сериализации.
/// </summary>
public static class TorrentMetadataParser
{
    private const string InfoKey = "info";
    private const string NameKey = "name";
    private const string Utf8NameKey = "name.utf-8";
    private const string FilesKey = "files";
    private const string LengthKey = "length";
    private const string PrivateKey = "private";

    /// <exception cref="BencodeFormatException">Документ не является корректным bencode.</exception>
    /// <exception cref="TorrentFormatException">В документе нет ожидаемых полей v1-торрента.</exception>
    public static TorrentMetadata Parse(ReadOnlyMemory<byte> data)
    {
        BencodeValue root = BencodeReader.Parse(data);
        if (root is not BencodeDictionary torrent)
        {
            throw new TorrentFormatException("Корень .torrent должен быть словарём bencode.");
        }

        if (!torrent.Entries.TryGetValue(InfoKey, out BencodeValue? infoValue) || infoValue is not BencodeDictionary info)
        {
            throw new TorrentFormatException("В .torrent отсутствует info-словарь.");
        }

        string name = ReadName(info);
        (long sizeBytes, int fileCount) = ReadContent(info);
        bool isPrivate = ReadPrivate(info);
        string infoHash = ComputeInfoHash(data, info);

        return new TorrentMetadata(name, infoHash, sizeBytes, fileCount, isPrivate);
    }

    private static string ReadName(BencodeDictionary info)
    {
        BencodeString? nameValue = FindString(info, Utf8NameKey) ?? FindString(info, NameKey);
        if (nameValue is null || nameValue.Bytes.IsEmpty)
        {
            throw new TorrentFormatException("В info-словаре нет непустого name.");
        }

        string name = nameValue.AsUtf8();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new TorrentFormatException("Название торрента состоит только из пробелов.");
        }

        return name;
    }

    private static (long SizeBytes, int FileCount) ReadContent(BencodeDictionary info)
    {
        if (info.Entries.TryGetValue(FilesKey, out BencodeValue? filesValue))
        {
            if (filesValue is not BencodeList files || files.Items.Count == 0)
            {
                throw new TorrentFormatException("info.files должен быть непустым списком.");
            }

            long total = 0;
            foreach (BencodeValue item in files.Items)
            {
                if (item is not BencodeDictionary file)
                {
                    throw new TorrentFormatException("Элемент info.files должен быть словарём.");
                }

                if (!file.Entries.TryGetValue(LengthKey, out BencodeValue? lengthValue) || lengthValue is not BencodeInteger length)
                {
                    throw new TorrentFormatException("У файла в info.files нет целого length.");
                }

                if (length.Value < 0)
                {
                    throw new TorrentFormatException("Длина файла в info.files не может быть отрицательной.");
                }

                try
                {
                    total = checked(total + length.Value);
                }
                catch (OverflowException exception)
                {
                    throw new TorrentFormatException("Суммарный размер торрента не помещается в long.", exception);
                }
            }

            return (total, files.Items.Count);
        }

        if (info.Entries.TryGetValue(LengthKey, out BencodeValue? singleLengthValue))
        {
            if (singleLengthValue is not BencodeInteger singleLength || singleLength.Value < 0)
            {
                throw new TorrentFormatException("info.length должен быть неотрицательным целым.");
            }

            return (singleLength.Value, 1);
        }

        throw new TorrentFormatException("В info нет ни length, ни files — это не v1-торрент.");
    }

    private static bool ReadPrivate(BencodeDictionary info) =>
        info.Entries.TryGetValue(PrivateKey, out BencodeValue? privateValue)
        && privateValue is BencodeInteger privateFlag
        && privateFlag.Value != 0;

    private static BencodeString? FindString(BencodeDictionary dictionary, string key) =>
        dictionary.Entries.TryGetValue(key, out BencodeValue? value) && value is BencodeString text
            ? text
            : null;

    private static string ComputeInfoHash(ReadOnlyMemory<byte> data, BencodeValue info)
    {
        Span<byte> hash = stackalloc byte[SHA1.HashSizeInBytes];
        SHA1.HashData(data.Span[info.Start..info.End], hash);
        return Convert.ToHexStringLower(hash);
    }
}
