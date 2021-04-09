using System;
using System.Collections.Generic;
using GTA.Math;
using ProtoBuf;

namespace Shared
{
    public enum PacketType
    {
        VehiclePositionData = 0,
        ChatData = 1,
        PlayerDisconnect = 2,
        PedPositionData = 3,
        NpcVehPositionData = 4,
        NpcPedPositionData = 5,
        WorldSharingStop = 6,
        DiscoveryResponse = 7,
        ConnectionRequest = 8,
        NativeCall = 9,
        NativeResponse = 10,
        PlayerKilled = 11,
        NativeTick = 12,
        NativeTickRecall = 13,
        NativeOnDisconnect = 14,
        NativeOnDisconnectRecall = 15,
    }

    public enum ScriptVersion
    {
        Unknown = 0,
        VERSION_0_1_0 = 1,
        VERSION_0_1_1 = 2,
        VERSION_0_1_2 = 3,
        VERSION_0_1_2_1 = 4,
    }

    [Flags]
    public enum VehicleDataFlags
    {
        PressingHorn = 1 << 0,
        SirenActive = 1 << 1,
        LightsOn = 1 << 2,
        HighbeamsOn = 1 << 3,
        IsInBurnout = 1 << 4,
        IsEngineRunning = 1 << 5,
    }

    [Flags]
    public enum PedDataFlags
    {
        IsAiming = 1 << 0,
        IsShooting = 1 << 1,
        IsReloading = 1 << 2,
        IsJumping = 1 << 3,
        IsParachuteOpen = 1 << 4,
    }

    [ProtoContract]
    public class DiscoveryResponse
    {
        [ProtoMember(1)]
        public string ServerName { get; set; }
        [ProtoMember(2)]
        public int MaxPlayers { get; set; }
        [ProtoMember(3)]
        public int PlayerCount { get; set; }
        [ProtoMember(4)]
        public bool PasswordProtected { get; set; }
        [ProtoMember(5)]
        public int Port { get; set; }
        [ProtoMember(6)]
        public string Gamemode { get; set; }
        [ProtoMember(7)]
        public bool LAN { get; set; }
    }

    [ProtoContract]
    public class ConnectionRequest
    {
        [ProtoMember(1)]
        public string Name { get; set; }

        [ProtoMember(2)]
        public string Password { get; set; }

        [ProtoMember(3)]
        public string DisplayName { get; set; }

        [ProtoMember(4)]
        public int GameVersion { get; set; }

        [ProtoMember(5)]
        public byte ScriptVersion { get; set; }
    }

    [ProtoContract]
    public class PlayerDisconnect
    {
        [ProtoMember(1)]
        public long Id { get; set; }
    }

    [ProtoContract]
    public class LVector3
    {
        [ProtoMember(1)]
        public float X { get; set; }
        [ProtoMember(2)]
        public float Y { get; set; }
        [ProtoMember(3)]
        public float Z { get; set; }

        public Vector3 ToVector()
        {
            return new Vector3(X, Y, Z);
        }

        public LVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public LVector3()
        {

        }
    }

    [ProtoContract]
    public class LQuaternion
    {
        [ProtoMember(1)]
        public float X { get; set; }
        [ProtoMember(2)]
        public float Y { get; set; }
        [ProtoMember(3)]
        public float Z { get; set; }
        [ProtoMember(4)]
        public float W { get; set; }

        public Quaternion ToQuaternion()
        {
            return new Quaternion(X, Y, Z, W);
        }
    }

    public static class VectorExtensions
    {
        public static LVector3 ToLVector(this Vector3 vec)
        {
            return new LVector3()
            {
                X = vec.X,
                Y = vec.Y,
                Z = vec.Z,
            };
        }

        public static LQuaternion ToLQuaternion(this Quaternion vec)
        {
            return new LQuaternion()
            {
                X = vec.X,
                Y = vec.Y,
                Z = vec.Z,
                W = vec.W,
            };
        }
    }
}