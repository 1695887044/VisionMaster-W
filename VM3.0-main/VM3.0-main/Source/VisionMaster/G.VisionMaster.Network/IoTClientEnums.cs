using System.ComponentModel.DataAnnotations;

namespace G.VisionMaster.Network
{
    public enum IoTClientProtocol
    {
        [Display(Name = "Modbus TCP")]
        ModBusTcp,
        [Display(Name = "Siemens S7")]
        Siemens,
        [Display(Name = "Mitsubishi MC")]
        Mitsubishi,
        [Display(Name = "Omron FINS")]
        OmronFins,
        [Display(Name = "AllenBradley CIP")]
        AllenBradley
    }

    public enum IoTDataType
    {
        Int16,
        UInt16,
        Int32,
        UInt32,
        Int64,
        UInt64,
        Float,
        Double,
        Bool,
        String
    }
}
