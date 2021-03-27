using ProtoBuf;
using System.Collections.Generic;

namespace Shared
{
    [ProtoContract]
    public class VehicleData
    {
        [ProtoMember(1)]
        public long Id { get; set; }

        [ProtoMember(2)]
        public string Name { get; set; }

        [ProtoMember(3)]
        public int VehicleModelHash { get; set; }

        [ProtoMember(4)]
        public int PedModelHash { get; set; }

        [ProtoMember(5)]
        public int PrimaryColor { get; set; }

        [ProtoMember(6)]
        public int SecondaryColor { get; set; }

        [ProtoMember(7)]
        public LVector3 Position { get; set; }

        [ProtoMember(8)]
        public LVector3 Quaternion { get; set; }

        [ProtoMember(9)]
        public int VehicleSeat { get; set; }

        [ProtoMember(10)]
        public float VehicleHealth { get; set; }

        [ProtoMember(11)]
        public int PlayerHealth { get; set; }

        [ProtoMember(12)]
        public float Latency { get; set; }

        [ProtoMember(13)]
        public Dictionary<int, int> VehicleMods { get; set; }

        [ProtoMember(14)]
        public float Speed { get; set; }

        [ProtoMember(15)]
        public LVector3 Velocity { get; set; }

        [ProtoMember(16)]
        public float Steering { get; set; }

        [ProtoMember(17)]
        public int RadioStation { get; set; }

        [ProtoMember(18)]
        public string Plate { get; set; }

        [ProtoMember(19)]
        public float RPM { get; set; }

        [ProtoMember(20)]
        public byte? Flag { get; set; }
    }

    [ProtoContract]
    public class PedData
    {
        [ProtoMember(1)]
        public long Id { get; set; }

        [ProtoMember(2)]
        public string Name { get; set; }

        [ProtoMember(3)]
        public int PedModelHash { get; set; }

        [ProtoMember(4)]
        public LVector3 Position { get; set; }

        [ProtoMember(5)]
        public LVector3 Quaternion { get; set; }

        [ProtoMember(6)]
        public LVector3 AimCoords { get; set; }
        [ProtoMember(7)]
        public int WeaponHash { get; set; }

        [ProtoMember(8)]
        public int PlayerHealth { get; set; }

        [ProtoMember(9)]
        public float Latency { get; set; }

        [ProtoMember(10)]
        public Dictionary<int, int> PedProps { get; set; }

        [ProtoMember(11)]
        public byte? Flag { get; set; }
    }
}
