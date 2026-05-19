using System;
using System.Collections.Generic;
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
            string baseAddress;

            if (Area == S7Area.DB)
            {
                baseAddress = $"DB{DbNumber}.DB{GetDataTypePrefix(DataType)}{Offset}";
            }
            else
            {
                baseAddress = $"{Area}{GetDataTypePrefix(DataType)}{Offset}";
            }

            // 对于位类型，添加位偏移
            if (IsBitType)
            {
                baseAddress += $".{BitOffset}";
            }

            return baseAddress;
        }

        private string GetDataTypePrefix(DataValueType dataType)
        {
            return dataType switch
            {
                DataValueType.Boolean => "X",
                DataValueType.SByte or DataValueType.Byte => "B",
                DataValueType.Int16 or DataValueType.UInt16 => "W",
                DataValueType.Int32 or DataValueType.UInt32 or DataValueType.Float => "D",
                DataValueType.Int64 or DataValueType.UInt64 or DataValueType.Double => "L",
                _ => "B"
            };
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
