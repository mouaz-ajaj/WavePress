using System.Collections.Generic;

namespace WavePress.Helpers
{
    /// <summary>
    /// يكتب قيماً بأي عدد من البتات (1-16) داخل byte stream.
    /// الكتابة بترتيب LSB-first (Little-endian bit order) داخل كل byte.
    /// 
    /// Writes arbitrary-width integer values (1–16 bits) into a compact byte stream.
    /// Bits are packed LSB-first within each byte, so:
    ///   WriteBits(0b1011, 4) then WriteBits(0b0110, 4) → one byte: 0b01101011
    /// 
    /// Usage:
    ///   var bw = new BitWriter();
    ///   bw.WriteBits(value, bitsCount);
    ///   byte[] result = bw.ToArray();
    /// </summary>
    public sealed class BitWriter
    {
        private readonly List<byte> _buffer    = new();
        private byte                _current   = 0; // البايت الجاري الكتابة فيه
        private int                 _bitsFilled = 0; // عدد البتات المكتوبة في _current (0-7)

        /// <summary>
        /// يكتب <paramref name="count"/> بتاً من <paramref name="value"/> (LSB first).
        /// Writes the least-significant <paramref name="count"/> bits of <paramref name="value"/>.
        /// </summary>
        /// <param name="value">القيمة (unsigned) المراد تخزينها</param>
        /// <param name="count">عدد البتات (1-16)</param>
        /// <exception cref="ArgumentOutOfRangeException">
        ///   إذا كان count خارج نطاق [1, 16].
        ///   Thrown when count is outside [1, 16].
        /// </exception>
        /// <exception cref="ArgumentException">
        ///   إذا كانت value لا تتسع في count bits (أي value >= 2^count).
        ///   Thrown when value does not fit in count bits.
        /// </exception>
        public void WriteBits(int value, int count)
        {
            if (count < 1 || count > 16)
                throw new ArgumentOutOfRangeException(nameof(count),
                    $"count must be between 1 and 16. Got: {count}");

            int maxValue = (1 << count) - 1;
            if (value < 0 || value > maxValue)
                throw new ArgumentException(
                    $"value {value} does not fit in {count} bits (valid range: 0–{maxValue}).",
                    nameof(value));

            // نمر على البتات من الأقل أهمية إلى الأكثر
            for (int b = 0; b < count; b++)
            {
                int bit = (value >> b) & 1;

                if (bit == 1)
                    _current |= (byte)(1 << _bitsFilled);

                _bitsFilled++;

                // إذا امتلأ البايت (8 بتات) → احفظه وابدأ بايتاً جديداً
                if (_bitsFilled == 8)
                {
                    _buffer.Add(_current);
                    _current    = 0;
                    _bitsFilled = 0;
                }
            }
        }

        /// <summary>
        /// يُنهي الكتابة ويحفظ آخر بايت جزئي (إن وجد) مع حشو الأصفار.
        /// Flushes any remaining partial byte (zero-padded) to the buffer.
        /// يجب استدعاؤه قبل ToArray().
        /// </summary>
        public void Flush()
        {
            if (_bitsFilled > 0)
            {
                _buffer.Add(_current);
                _current    = 0;
                _bitsFilled = 0;
            }
        }

        /// <summary>
        /// يعيد مصفوفة البايتات المكتوبة. اتصل بـ Flush() أولاً.
        /// Returns the packed byte array. Call Flush() first.
        /// </summary>
        public byte[] ToArray() => _buffer.ToArray();

        /// <summary>عدد البايتات في الـ buffer حتى الآن (بما في ذلك البايت الجزئي الحالي إن وجد).</summary>
        public int ByteCount => _buffer.Count + (_bitsFilled > 0 ? 1 : 0);
    }
}
