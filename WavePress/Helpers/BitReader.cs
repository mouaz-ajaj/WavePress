namespace WavePress.Helpers
{
    /// <summary>
    /// يقرأ قيماً بأي عدد من البتات (1-16) من byte stream مُنتج بواسطة BitWriter.
    /// يجب استخدامه مع نفس إعدادات BitWriter (نفس عدد البتات لكل قيمة).
    /// 
    /// Reads arbitrary-width integer values (1–16 bits) from a byte stream
    /// produced by <see cref="BitWriter"/>. Uses the same LSB-first, byte-aligned
    /// bit ordering so that ReadBits(n) exactly reverses WriteBits(value, n).
    /// 
    /// Usage:
    ///   var br = new BitReader(data);
    ///   int value = br.ReadBits(bitsCount);
    /// </summary>
    public sealed class BitReader
    {
        private readonly byte[] _data;
        private int _bytePos = 0; // موضع البايت الحالي في _data
        private int _bitsRead = 0; // عدد البتات المقروءة من البايت الحالي (0-7)

        /// <param name="data">بيانات مكتوبة مسبقاً بـ BitWriter</param>
        /// <param name="startOffset">موضع البداية في المصفوفة (0 افتراضياً)</param>
        public BitReader(byte[] data, int startOffset = 0)
        {
            _data    = data;
            _bytePos = startOffset;
        }

        /// <summary>
        /// يقرأ <paramref name="count"/> بتاً ويعيدها كـ unsigned integer (LSB first).
        /// Reads <paramref name="count"/> bits and returns them as an unsigned int (LSB first).
        /// </summary>
        /// <param name="count">عدد البتات (1-16)</param>
        /// <returns>القيمة المقروءة كـ unsigned</returns>
        public int ReadBits(int count)
        {
            int result = 0;

            for (int b = 0; b < count; b++)
            {
                if (_bytePos >= _data.Length)
                    break; // نهاية البيانات — تُرجع ما تم جمعه

                // استخراج البت من موضعه في البايت الحالي
                int bit = (_data[_bytePos] >> _bitsRead) & 1;
                result |= (bit << b);

                _bitsRead++;

                // إذا انتهت بتات البايت الحالي، انتقل للتالي
                if (_bitsRead == 8)
                {
                    _bytePos++;
                    _bitsRead = 0;
                }
            }

            return result;
        }

        /// <summary>
        /// هل وصلنا لنهاية البيانات؟
        /// Returns true if all data has been consumed.
        /// </summary>
        public bool IsEnd => _bytePos >= _data.Length;

        /// <summary>موضع البايت الحالي في المصفوفة.</summary>
        public int BytePosition => _bytePos;
    }
}
