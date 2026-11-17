namespace TorrentWatcher.Domain.Parsing;

/// <summary>
/// Строгий разбор bencode (BEP 3): поддерживаются целые, строки, списки и словари.
/// Документ должен занимать все переданные байты; глубокая вложенность ограничена.
/// </summary>
public static class BencodeReader
{
    /// <summary>Предел вложенности: у настоящих .torrent она не превышает 5–6 уровней.</summary>
    public const int MaxDepth = 64;

    /// <summary>Разбирает ровно один bencode-документ.</summary>
    /// <exception cref="BencodeFormatException">Документ пуст, обрезан или не является корректным bencode.</exception>
    public static BencodeValue Parse(ReadOnlyMemory<byte> data)
    {
        Parser parser = new(data);
        BencodeValue value = parser.ReadValue(0);
        parser.EnsureEnd();
        return value;
    }

    private sealed class Parser
    {
        private readonly ReadOnlyMemory<byte> _data;
        private int _position;

        public Parser(ReadOnlyMemory<byte> data)
        {
            _data = data;
        }

        private ReadOnlySpan<byte> Span => _data.Span;

        public BencodeValue ReadValue(int depth)
        {
            if (depth > MaxDepth)
            {
                throw new BencodeFormatException($"Слишком глубокая вложенность bencode (больше {MaxDepth}).");
            }

            if (_position >= _data.Length)
            {
                throw new BencodeFormatException("Неожиданный конец bencode-документа.");
            }

            byte marker = Span[_position];
            if (marker == (byte)'i')
            {
                return ReadInteger();
            }

            if (marker == (byte)'l')
            {
                return ReadList(depth);
            }

            if (marker == (byte)'d')
            {
                return ReadDictionary(depth);
            }

            if (marker is >= (byte)'0' and <= (byte)'9')
            {
                return ReadString();
            }

            throw new BencodeFormatException($"Неизвестный маркер bencode 0x{marker:X2} на позиции {_position}.");
        }

        public void EnsureEnd()
        {
            if (_position != _data.Length)
            {
                throw new BencodeFormatException($"Лишние данные после bencode-значения, позиция {_position}.");
            }
        }

        private BencodeString ReadString()
        {
            int start = _position;
            int length = ReadLength();

            if (length > _data.Length - _position)
            {
                throw new BencodeFormatException($"Строка длиной {length} выходит за границы документа, позиция {start}.");
            }

            ReadOnlyMemory<byte> bytes = _data.Slice(_position, length);
            _position += length;
            return new BencodeString(start, _position, bytes);
        }

        private int ReadLength()
        {
            int start = _position;
            while (_position < _data.Length && Span[_position] is >= (byte)'0' and <= (byte)'9')
            {
                _position++;
            }

            if (_position == start)
            {
                throw new BencodeFormatException($"Ожидалась длина строки на позиции {start}.");
            }

            if (_position >= _data.Length || Span[_position] != (byte)':')
            {
                throw new BencodeFormatException($"Ожидался ':' после длины строки, позиция {_position}.");
            }

            ReadOnlySpan<byte> digits = Span[start.._position];
            _position++;

            if (digits.Length > 1 && digits[0] == (byte)'0')
            {
                throw new BencodeFormatException($"Длина строки не может начинаться с нуля, позиция {start}.");
            }

            int length = 0;
            foreach (byte digit in digits)
            {
                int value = digit - '0';
                if (length > (int.MaxValue - value) / 10)
                {
                    throw new BencodeFormatException($"Слишком большая длина строки, позиция {start}.");
                }

                length = (length * 10) + value;
            }

            return length;
        }

        private BencodeInteger ReadInteger()
        {
            int start = _position;
            _position++;

            bool negative = false;
            if (_position < _data.Length && Span[_position] == (byte)'-')
            {
                negative = true;
                _position++;
            }

            int digitsStart = _position;
            while (_position < _data.Length && Span[_position] is >= (byte)'0' and <= (byte)'9')
            {
                _position++;
            }

            if (_position == digitsStart)
            {
                throw new BencodeFormatException($"Ожидались цифры целого числа, позиция {start}.");
            }

            if (_position >= _data.Length || Span[_position] != (byte)'e')
            {
                throw new BencodeFormatException($"Целое число не закрыто 'e', позиция {start}.");
            }

            ReadOnlySpan<byte> digits = Span[digitsStart.._position];
            _position++;

            if (digits.Length > 1 && digits[0] == (byte)'0')
            {
                throw new BencodeFormatException($"Целое число не может начинаться с нуля, позиция {start}.");
            }

            if (negative && digits.Length == 1 && digits[0] == (byte)'0')
            {
                throw new BencodeFormatException($"Запись '-0' недопустима, позиция {start}.");
            }

            ulong magnitude = 0;
            foreach (byte digit in digits)
            {
                ulong value = (ulong)(digit - '0');
                if (magnitude > (ulong.MaxValue - value) / 10)
                {
                    throw new BencodeFormatException($"Слишком большое целое число, позиция {start}.");
                }

                magnitude = (magnitude * 10) + value;
            }

            long result;
            if (negative)
            {
                const ulong minMagnitude = 9223372036854775808UL;
                if (magnitude > minMagnitude)
                {
                    throw new BencodeFormatException($"Целое число не помещается в long, позиция {start}.");
                }

                result = magnitude == minMagnitude ? long.MinValue : -(long)magnitude;
            }
            else
            {
                if (magnitude > long.MaxValue)
                {
                    throw new BencodeFormatException($"Целое число не помещается в long, позиция {start}.");
                }

                result = (long)magnitude;
            }

            return new BencodeInteger(start, _position, result);
        }

        private BencodeList ReadList(int depth)
        {
            int start = _position;
            _position++;

            List<BencodeValue> items = [];
            while (true)
            {
                if (_position >= _data.Length)
                {
                    throw new BencodeFormatException($"Список не закрыт 'e', позиция {start}.");
                }

                if (Span[_position] == (byte)'e')
                {
                    _position++;
                    return new BencodeList(start, _position, items);
                }

                items.Add(ReadValue(depth + 1));
            }
        }

        private BencodeDictionary ReadDictionary(int depth)
        {
            int start = _position;
            _position++;

            Dictionary<string, BencodeValue> entries = new(StringComparer.Ordinal);
            while (true)
            {
                if (_position >= _data.Length)
                {
                    throw new BencodeFormatException($"Словарь не закрыт 'e', позиция {start}.");
                }

                if (Span[_position] == (byte)'e')
                {
                    _position++;
                    return new BencodeDictionary(start, _position, entries);
                }

                if (Span[_position] is not (>= (byte)'0' and <= (byte)'9'))
                {
                    throw new BencodeFormatException($"Ключ словаря должен быть строкой, позиция {_position}.");
                }

                BencodeString key = ReadString();
                string name = key.AsUtf8();
                if (entries.ContainsKey(name))
                {
                    throw new BencodeFormatException($"Повторяющийся ключ '{name}' в словаре, позиция {key.Start}.");
                }

                entries.Add(name, ReadValue(depth + 1));
            }
        }
    }
}
