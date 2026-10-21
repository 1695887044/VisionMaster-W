using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UI.Attributes;

namespace VisionMaster.Communications
{
    public class S7Address : DeviceAddressBase<S7Area>
    {
        private int _dbNumber = 1;

        [SuperDisplay(Name = "DB块编号(仅DB区有效)")]
        public int DbNumber
        {
            get => _dbNumber;
            set
            {
                if (value < 1) value = 1;
                if (SetProperty(ref _dbNumber, value))
                {
                    _cachedAddress = null;
                    RaisePropertyChanged(nameof(Address));
                }
            }
        }

        protected override string BuildAddress()
        {
            // 有效起始字节 = 基地址（Offset）+ 地址偏移；解析失败时退回原始字符串，
            // 由 Validate 报错（预览不因非法输入抛异常）。
            int start = EffectiveStart ?? 0;

            // 位访问（Boolean+位偏移）：无类型前缀，如 M0.0 / DB1.DBX0.0 由 DB 分支自带 X 前缀
            if (IsBitType)
            {
                string baseBit = Area == S7Area.DB
                    ? $"DB{DbNumber}.DBX{start}"
                    : $"{Area}{start}";
                return $"{baseBit}.{BitOffset}";
            }

            string baseAddress;

            if (Area == S7Area.DB)
            {
                baseAddress = $"DB{DbNumber}.DB{GetDataTypePrefix(DataType)}{start}";
            }
            else
            {
                baseAddress = $"{Area}{GetDataTypePrefix(DataType)}{start}";
            }

            return baseAddress;
        }

        private string GetDataTypePrefix(DataValueType dataType)
        {
            return dataType switch
            {
                // 无位偏移的 Boolean 按 1 字节读：旧 "DBX"+纯数字 会让 HSL 剥前缀后解析空串炸掉
                DataValueType.Boolean => "B",
                DataValueType.SByte or DataValueType.Byte => "B",
                DataValueType.Int16 or DataValueType.UInt16 => "W",
                DataValueType.Int32 or DataValueType.UInt32 or DataValueType.Float => "D",
                // "L" 不在 HSL 的类型前缀剥离白名单里（DBL0 会解析失败），8 字节类型统一用 "D" 定位字节偏移
                DataValueType.Int64 or DataValueType.UInt64 or DataValueType.Double => "D",
                _ => "B"
            };
        }

        /// <summary>
        /// <para>生成结构化轮询地址：区域分组键 + 字节粒度段前缀（"MB"/"DB7.DBB"/"VB"）。</para>
        /// <para>这些形式均已核对 HSL 的 S7AddressData.ParseFrom 可直接解析。</para>
        /// </summary>
        public override bool TryCreatePollAddress(out PollAddress? poll)
        {
            poll = null;
            if (EffectiveStart is not int start || start < 0) return false;

            string group, prefix;
            if (Area == S7Area.DB)
            {
                group = $"DB{DbNumber}";
                prefix = $"DB{DbNumber}.DBB";
            }
            else
            {
                group = Area.ToString();          // M / I / Q / V
                prefix = $"{Area}B";              // MB / IB / QB / VB
            }

            poll = new PollAddress
            {
                Protocol = PollProtocol.S7,
                GroupKey = group,
                SegmentPrefix = prefix,
                Start = start,
                SpanUnits = IsBitType ? 1 : Math.Max(1, TotalBytes),
                IsBitArea = false,
                IsBitAccess = IsBitType,
                BitOffset = IsBitType ? BitOffset : -1
            };
            return true;
        }

        /// <inheritdoc />
        public override bool TryCreateElementAddress(
            int index, out DeviceAddressBase? elementAddress, out string? error)
        {
            elementAddress = null;
            error = null;

            if (index < 0)
            {
                error = "数组下标不能为负数";
                return false;
            }

            if (index >= Length)
            {
                error = $"下标 {index} 超出数组长度 {Length}";
                return false;
            }

            int start = EffectiveStart ?? 0;
            int elementStart;
            DataValueType elementType;
            int elementLength;

            if (DataType == DataValueType.ByteArray)
            {
                // byte[] 的每个元素就是 1 字节；S7 是字节寻址，可以直接写 DBB<n>。
                elementStart = start + index;
                elementType = DataValueType.Byte;
                elementLength = 1;
            }
            else if (DataType == DataValueType.String && ElementByteLength > 0)
            {
                // string[] 的每个元素是固定宽度字节块；按 ElementByteLength 对齐。
                elementStart = start + index * ElementByteLength;
                elementType = DataValueType.ByteArray;
                elementLength = ElementByteLength;
            }
            else if (DataType == DataValueType.String)
            {
                error = "字符串数组未配置每元素字节数，无法按元素写入";
                return false;
            }
            else
            {
                elementStart = start + index * Math.Max(1, TypeSize);
                elementType = DataType;
                elementLength = 1;
            }

            var element = new S7Address
            {
                Area = Area,
                DbNumber = DbNumber,
                Offset = elementStart.ToString(CultureInfo.InvariantCulture),
                DataType = elementType,
                BitOffset = -1,
                Length = elementLength,
                ElementByteLength = 0,
                AddressOffset = 0,
            };

            element.EnableConversion = false;

            var validation = element.Validate();
            if (!validation.IsValid)
            {
                error = validation.ErrorMessage;
                return false;
            }

            elementAddress = element;
            return true;
        }

        public override (bool IsValid, string ErrorMessage) Validate()
        {
            var baseResult = base.Validate();
            if (!baseResult.IsValid)
                return baseResult;

            if (Area == S7Area.DB && DbNumber < 1)
                return (false, "DB块编号必须大于0");

            return (true, string.Empty);
        }
    }
}
