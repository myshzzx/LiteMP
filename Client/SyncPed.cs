using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using NativeUI;
using Font = GTA.Font;

namespace LiteClient
{
    public class SyncPed
    {
        public long Host;
        public Ped Character;
        public Vector3 Position, _rotation, AimCoords;
        public int ModelHash, CurrentWeapon;
        public bool IsShooting, _lastShooting, IsReloading, IsAiming, IsJumping, _lastJumping, IsInVehicle, _lastVehicle;
        private bool _lastBurnout;
        public float Latency;
        public bool IsHornPressed, _lastHorn, LightsOn, highbeamsOn;
        public Vehicle MainVehicle { get; set; }

        public int VehicleSeat, PedHealth;

        public float VehicleHealth, Steering, VehicleRPM;
        public int VehicleHash, VehiclePrimaryColor, VehicleSecondaryColor;
        public Vector3 _vehicleRotation;
        public string Name;
        public bool Siren, IsInBurnout, IsEngineRunning;
        public string Plate;
        public int RadioStation;

        public static bool Debug;

        private DateTime _stopTime;
        public float Speed
        {
            get { return _speed; }
            set
            {
                _lastSpeed = _speed;
                _speed = value;
            }
        }

        public bool IsParachuteOpen;

        public double AverageLatency
        {
            get { return _latencyAverager.Count == 0 ? 0 : _latencyAverager.Average(); }
        }

        public int LastUpdateReceived
        {
            get { return _lastUpdateReceived; }
            set
            {
                if (_lastUpdateReceived != 0)
                {
                    _latencyAverager.Enqueue(value - _lastUpdateReceived);
                    if (_latencyAverager.Count >= 10)
                        _latencyAverager.Dequeue();
                }

                _lastUpdateReceived = value;
            }
        }

        public int TicksSinceLastUpdate
        {
            get { return Environment.TickCount - LastUpdateReceived; }
        }

        public Dictionary<int, int> VehicleMods
        {
            get { return _vehicleMods; }
            set
            {
                if (value == null) return;
                _vehicleMods = value;
            }
        }

        public Dictionary<int, int> PedProps
        {
            get { return _pedProps; }
            set
            {
                if (value == null) return;
                _pedProps = value;
            }
        }

        private Vector3 _lastVehiclePos;
        private Vector3 _carPosOnUpdate;
        public Vector3 VehiclePosition
        {
            get { return _vehiclePosition; }
            set
            {
                _lastVehiclePos = _vehiclePosition;
                _vehiclePosition = value;

                if (MainVehicle != null)
                    _carPosOnUpdate = MainVehicle.Position;
            }
        }

        public Vector3 VehicleVelocity
        {
            get { return _vehicleVelocity; }
            set { _vehicleVelocity = value; }
        }

        private Vector3? _lastVehicleRotation;
        public Vector3 VehicleRotation
        {
            get { return _vehicleRotation; }
            set
            {
                _lastVehicleRotation = _vehicleRotation;
                _vehicleRotation = value;
            }
        }

        public Vector3 Rotation
        {
            get { return _rotation; }
            set { _rotation = value; }
        }

        private uint _switch;
        private float _lastSpeed;
        private bool _blip;
        private bool _justEnteredVeh;
        private int _relGroup;
        private DateTime _enterVehicleStarted;
        private Vector3 _vehiclePosition;
        private Dictionary<int, int> _vehicleMods;
        private Dictionary<int, int> _pedProps;

        private Queue<double> _latencyAverager;

        private bool _isStreamedIn;
        private Blip _mainBlip;
        private Prop _parachuteProp;

        public SyncPed(int hash, Vector3 pos, Vector3 rot, bool blip = true)
        {
            Position = pos;
            Rotation = rot;
            ModelHash = hash;
            _blip = blip;

            _latencyAverager = new Queue<double>();

            _relGroup = World.AddRelationshipGroup("SYNCPED");
            World.SetRelationshipBetweenGroups(Relationship.Neutral, _relGroup, Game.Player.Character.RelationshipGroup);
            World.SetRelationshipBetweenGroups(Relationship.Neutral, Game.Player.Character.RelationshipGroup, _relGroup);
        }

        public void SetBlipNameFromTextFile(Blip blip, string text)
        {
            Function.Call(Hash._0xF9113A30DE5C6670, "STRING");
            Function.Call(Hash._ADD_TEXT_COMPONENT_STRING, text);
            Function.Call(Hash._0xBC38B49BCB83BC9B, blip);
        }

        private int _modSwitch = 0;
        private int _clothSwitch = 0;
        private int _lastUpdateReceived;
        private float _speed;
        private Vector3 _vehicleVelocity;

        public void DisplayLocally()
        {
            try
            {
                float hRange = IsInVehicle ? 200f : 200f;
                Vector3 gPos = IsInVehicle ? VehiclePosition : Position;
                bool inRange = Game.Player.Character.IsInRangeOf(gPos, hRange);

                if (inRange && !_isStreamedIn)
                {
                    _isStreamedIn = true;
                    if (_mainBlip != null)
                    {
                        _mainBlip.Remove();
                        _mainBlip = null;
                    }
                }
                else if (!inRange && _isStreamedIn)
                {
                    Clear();
                    _isStreamedIn = false;
                }

                if (!inRange)
                {
                    if (_mainBlip == null && _blip)
                    {
                        _mainBlip = World.CreateBlip(gPos);
                        _mainBlip.Color = BlipColor.White;
                        _mainBlip.Scale = 0.8f;
                        SetBlipNameFromTextFile(_mainBlip, Name ?? "<nameless>");
                    }
                    if (_blip && _mainBlip != null)
                        _mainBlip.Position = gPos;
                    return;
                }


                if (Character == null || !Character.Exists() || (!Character.IsInRangeOf(gPos, hRange) && Environment.TickCount - LastUpdateReceived < 5000) || Character.Model.Hash != ModelHash || (Character.IsDead && PedHealth > 0))
                {
                    if (Character != null) Character.Delete();

                    Character = World.CreatePed(new Model(ModelHash), gPos, Rotation.Z);
                    if (Character == null) return;

                    Character.BlockPermanentEvents = true;
                    Character.IsInvincible = true;
                    Character.CanRagdoll = false;
                    Character.RelationshipGroup = _relGroup;
                    if (_blip)
                    {
                        Character.AddBlip();
                        if (Character.CurrentBlip == null) return;
                        Character.CurrentBlip.Color = BlipColor.White;
                        Character.CurrentBlip.Scale = 0.8f;
                        SetBlipNameFromTextFile(Character.CurrentBlip, Name);
                    }
                    return;
                }

                if (!Character.IsOccluded && Character.IsInRangeOf(Game.Player.Character.Position, 20f))
                {
                    Vector3 targetPos = Character.GetBoneCoord(Bone.IK_Head) + new Vector3(0, 0, 0.5f);

                    targetPos += Character.Velocity / Game.FPS;

                    Function.Call(Hash.SET_DRAW_ORIGIN, targetPos.X, targetPos.Y, targetPos.Z, 0);

                    string nameText = (TicksSinceLastUpdate > 10000) ? ("~r~AFK~w~~n~" + Name ?? "<nameless>") : (Name ?? "<nameless>");

                    float sizeOffset = Math.Max(1f - ((GameplayCamera.Position - Character.Position).Length() / 30f), 0.3f);

                    new UIResText(nameText, new Point(0, 0), 0.4f * sizeOffset, Color.WhiteSmoke, Font.ChaletLondon, UIResText.Alignment.Centered)
                    {
                        Outline = true,
                    }.Draw();

                    Function.Call(Hash.CLEAR_DRAW_ORIGIN);
                }

                if ((!_lastVehicle && IsInVehicle && VehicleHash != 0) || (_lastVehicle && IsInVehicle && (MainVehicle == null || !Character.IsInVehicle(MainVehicle) || MainVehicle.Model.Hash != VehicleHash || VehicleSeat != Util.GetPedSeat(Character))))
                {
                    if (MainVehicle != null && Util.IsVehicleEmpty(MainVehicle))
                        MainVehicle.Delete();

                    List<Vehicle> vehs = World.GetAllVehicles().OrderBy(v =>
                    {
                        return v == null ? float.MaxValue : (v.Position - Character.Position).Length();
                    }).ToList();


                    if (vehs.Any() && vehs[0].Model.Hash == VehicleHash && vehs[0].IsInRangeOf(gPos, 3f))
                    {
                        if (Debug)
                        {
                            if (MainVehicle != null)
                                MainVehicle.Delete();

                            MainVehicle = World.CreateVehicle(new Model(VehicleHash), VehiclePosition, VehicleRotation.Z);
                        }
                        else
                            MainVehicle = vehs[0];

                        if (Game.Player.Character.IsInVehicle(MainVehicle) && VehicleSeat == Util.GetPedSeat(Game.Player.Character))
                        {
                            Game.Player.Character.Task.WarpOutOfVehicle(MainVehicle);
                            UI.Notify("~r~Car jacked!");
                        }
                    }
                    else
                        MainVehicle = World.CreateVehicle(new Model(VehicleHash), gPos, 0);

                    if (MainVehicle != null)
                    {
                        if (VehicleSeat == -1)
                            MainVehicle.Position = VehiclePosition;
                        MainVehicle.EngineRunning = IsEngineRunning;
                        MainVehicle.PrimaryColor = (VehicleColor)VehiclePrimaryColor;
                        MainVehicle.SecondaryColor = (VehicleColor)VehicleSecondaryColor;
                        MainVehicle.IsInvincible = true;
                        Character.SetIntoVehicle(MainVehicle, (VehicleSeat)VehicleSeat);
                    }

                    _lastVehicle = true;
                    _justEnteredVeh = true;
                    _enterVehicleStarted = DateTime.Now;
                    return;
                }

                if (_lastVehicle && _justEnteredVeh && IsInVehicle && !Character.IsInVehicle(MainVehicle) && DateTime.Now.Subtract(_enterVehicleStarted).TotalSeconds <= 4)
                    return;

                _justEnteredVeh = false;

                if (_lastVehicle && !IsInVehicle && MainVehicle != null && Character != null)
                    Character.Task.LeaveVehicle(MainVehicle, true);

                if (Character != null)
                    Character.Health = (int)((PedHealth / (float)100) * Character.MaxHealth);

                _switch++;

                if (!inRange)
                {
                    if (Character != null && Environment.TickCount - LastUpdateReceived < 10000)
                    {
                        if (!IsInVehicle)
                        {
                            Character.PositionNoOffset = gPos;
                        }
                        else if (MainVehicle != null && GetResponsiblePed(MainVehicle).Handle == Character.Handle)
                        {
                            MainVehicle.Position = VehiclePosition;
                            MainVehicle.Rotation = VehicleRotation;
                        }
                    }
                    return;
                }

                if (IsInVehicle)
                {
                    if (GetResponsiblePed(MainVehicle).Handle == Character.Handle)
                    {
                        MainVehicle.EngineRunning = IsEngineRunning;

                        MainVehicle.EngineHealth = VehicleHealth;
                        if (MainVehicle.Health <= 0)
                        {
                            MainVehicle.IsInvincible = false;
                            //_mainVehicle.Explode();
                        }
                        else
                        {
                            MainVehicle.IsInvincible = true;
                            if (MainVehicle.IsDead)
                                MainVehicle.Repair();
                        }

                        MainVehicle.PrimaryColor = (VehicleColor)VehiclePrimaryColor;
                        MainVehicle.SecondaryColor = (VehicleColor)VehicleSecondaryColor;

                        if (Plate != null)
                            MainVehicle.NumberPlate = Plate;

                        string[] radioStations = Util.GetRadioStations();

                        if (radioStations?.ElementAtOrDefault(RadioStation) != null)
                            Function.Call(Hash.SET_VEH_RADIO_STATION, radioStations[RadioStation]);

                        if (VehicleMods != null && _modSwitch % 50 == 0 && Game.Player.Character.IsInRangeOf(VehiclePosition, 30f))
                        {
                            int id = _modSwitch / 50;

                            if (VehicleMods.ContainsKey(id) && VehicleMods[id] != MainVehicle.GetMod((VehicleMod)id))
                            {
                                Function.Call(Hash.SET_VEHICLE_MOD_KIT, MainVehicle.Handle, 0);
                                MainVehicle.SetMod((VehicleMod)id, VehicleMods[id], false);
                                Function.Call(Hash.RELEASE_PRELOAD_MODS, id);
                            }
                        }
                        _modSwitch++;

                        if (_modSwitch >= 2500)
                            _modSwitch = 0;

                        if (IsHornPressed && !_lastHorn)
                        {
                            _lastHorn = true;
                            MainVehicle.SoundHorn(99999);
                        }
                        else if (!IsHornPressed && _lastHorn)
                        {
                            _lastHorn = false;
                            MainVehicle.SoundHorn(1);
                        }
                            

                        if (IsInBurnout && !_lastBurnout)
                        {
                            Function.Call(Hash.SET_VEHICLE_BURNOUT, MainVehicle, true);
                            Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, Character, MainVehicle, 23, 120000); // 30 - burnout
                        }
                        else if (!IsInBurnout && _lastBurnout)
                        {
                            Function.Call(Hash.SET_VEHICLE_BURNOUT, MainVehicle, false);
                            Character.Task.ClearAll();
                        }

                        _lastBurnout = IsInBurnout;

                        Function.Call(Hash.SET_VEHICLE_BRAKE_LIGHTS, MainVehicle, Speed > 0.2 && _lastSpeed > Speed);

                        MainVehicle.LightsOn = LightsOn;
                        MainVehicle.HighBeamsOn = highbeamsOn;
                        MainVehicle.SirenActive = Siren;
                        MainVehicle.CurrentRPM = VehicleRPM;
                        MainVehicle.SteeringAngle = (float)(Math.PI / 180) * Steering;

                        if (Speed > 0.2f || IsInBurnout)
                        {
                            int currentTime = Environment.TickCount;
                            float alpha = Util.Unlerp(currentInterop.StartTime, currentTime, currentInterop.FinishTime);

                            alpha = Util.Clamp(0f, alpha, 1.5f);

                            float cAlpha = alpha - currentInterop.LastAlpha;
                            currentInterop.LastAlpha = alpha;

                            if (alpha == 1.5f)
                                currentInterop.FinishTime = 0;

                            MainVehicle.Velocity = VehicleVelocity + (VehiclePosition + Util.Lerp(new Vector3(), cAlpha, currentInterop.vecError) - MainVehicle.Position);

                            _stopTime = DateTime.Now;
                            _carPosOnUpdate = MainVehicle.Position;
                        }
                        else if (DateTime.Now.Subtract(_stopTime).TotalMilliseconds <= 1000)
                        {
                            Vector3 posTarget = Util.LinearVectorLerp(_carPosOnUpdate, VehiclePosition + (VehiclePosition - _lastVehiclePos), (int)DateTime.Now.Subtract(_stopTime).TotalMilliseconds, 1000);

                            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, MainVehicle, posTarget.X, posTarget.Y, posTarget.Z, 0, 0, 0, 0);
                        }
                        else
                        {
                            MainVehicle.PositionNoOffset = VehiclePosition;
                        }

                        if (_lastVehicleRotation != null)
                        {
                            MainVehicle.Quaternion = GTA.Math.Quaternion.Slerp(_lastVehicleRotation.Value.ToQuaternion(),
                                _vehicleRotation.ToQuaternion(),
                                Math.Min(1.5f, TicksSinceLastUpdate / (float)AverageLatency));
                        }
                        else
                        {
                            MainVehicle.Quaternion = _vehicleRotation.ToQuaternion();
                        }
                    }
                }
                else
                {
                    if (PedProps != null && _clothSwitch % 50 == 0 && Game.Player.Character.IsInRangeOf(Position, 30f))
                    {
                        int id = _clothSwitch / 50;

                        if (PedProps.ContainsKey(id) && PedProps[id] != Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, Character.Handle, id))
                            Function.Call(Hash.SET_PED_COMPONENT_VARIATION, Character.Handle, id, PedProps[id], 0, 0);
                    }

                    _clothSwitch++;
                    if (_clothSwitch >= 750)
                        _clothSwitch = 0;

                    if (Character.Weapons.Current.Hash != (WeaponHash)CurrentWeapon)
                        Character.Weapons.Select(Character.Weapons.Give((WeaponHash)CurrentWeapon, 9999, true, true));

                    if (!_lastJumping && IsJumping)
                        Character.Task.Jump();

                    if (IsParachuteOpen)
                    {
                        if (_parachuteProp == null)
                        {
                            _parachuteProp = World.CreateProp(new Model(1740193300), Character.Position,
                                Character.Rotation, false, false);
                            _parachuteProp.FreezePosition = true;
                            Function.Call(Hash.SET_ENTITY_COLLISION, _parachuteProp.Handle, false, 0);
                        }
                        Character.FreezePosition = true;
                        Character.Position = Position - new Vector3(0, 0, 1);
                        Character.Quaternion = _rotation.ToQuaternion();
                        _parachuteProp.Position = Character.Position + new Vector3(0, 0, 3.7f);
                        _parachuteProp.Quaternion = Character.Quaternion;

                        Character.Task.PlayAnimation("skydive@parachute@first_person", "chute_idle_right", 8f, 5000, false, 8f);
                    }
                    else
                    {
                        Character.FreezePosition = false;

                        if (_parachuteProp != null)
                        {
                            _parachuteProp.Delete();
                            _parachuteProp = null;
                        }

                        Function.Call(Hash.SET_AI_WEAPON_DAMAGE_MODIFIER, 1f);
                        Function.Call(Hash.SET_PED_SHOOT_RATE, Character.Handle, 100);
                        Function.Call(Hash.SET_PED_INFINITE_AMMO_CLIP, Character.Handle, true);

                        // We have to revise that at some point
                        if (IsReloading)
                        {
                            if (!Character.IsReloading)
                                Character.Task.ReloadWeapon();

                            if (!Character.IsInRangeOf(Position, 0.5f))
                            {
                                Character.Position = Position - new Vector3(0, 0, 1f);
                                Character.Quaternion = _rotation.ToQuaternion();
                            }
                        } //
                        else
                        {
                            if (Character.Weapons.Current.Hash != WeaponHash.Unarmed)
                            {
                                const int threshold = 50;

                                if (IsAiming && !IsShooting)
                                {
                                    if (!Character.IsInRangeOf(Position, 0.5f) && _switch % threshold == 0)
                                    {
                                        Function.Call(Hash.TASK_GO_TO_COORD_WHILE_AIMING_AT_COORD, Character.Handle, Position.X, Position.Y,
                                        Position.Z, AimCoords.X, AimCoords.Y, AimCoords.Z, 2f, 0, 0x3F000000, 0x40800000, 1, 512, 0,
                                        (uint)FiringPattern.FullAuto);
                                    }
                                    else if (Character.IsInRangeOf(Position, 0.5f))
                                    {
                                        Character.Task.AimAt(AimCoords, -1);
                                    }
                                }

                                if (!Character.IsInRangeOf(Position, 0.5f) && ((IsShooting && !_lastShooting) || (IsShooting && _lastShooting && _switch % (threshold * 2) == 0)))
                                {
                                    Function.Call(Hash.TASK_GO_TO_COORD_WHILE_AIMING_AT_COORD, Character.Handle, Position.X, Position.Y,
                                        Position.Z, AimCoords.X, AimCoords.Y, AimCoords.Z, 2f, 1, 0x3F000000, 0x40800000, 1, 0, 0,
                                        (uint)FiringPattern.FullAuto);
                                }
                                else if ((IsShooting && !_lastShooting) || (IsShooting && _lastShooting && _switch % (threshold / 2) == 0))
                                {
                                    Function.Call(Hash.TASK_SHOOT_AT_COORD, Character.Handle, AimCoords.X, AimCoords.Y, AimCoords.Z, 1500, (uint)FiringPattern.FullAuto);
                                }
                            }

                            if ((Character.Weapons.Current.Hash == WeaponHash.Unarmed || !IsAiming && !IsShooting) && !IsJumping)
                            {
                                if (!Character.IsInRangeOf(Position, 5f))
                                {
                                    Character.Position = Position - new Vector3(0, 0, 1f);
                                    Character.Quaternion = _rotation.ToQuaternion();
                                }
                                else if (!Character.IsInRangeOf(Position, 0.5f))
                                {
                                    Character.Task.RunTo(Position, true, 500);
                                }
                            }
                        }
                    }

                    _lastJumping = IsJumping;
                    _lastShooting = IsShooting;
                }

                _lastVehicle = IsInVehicle;
            }
            catch (Exception ex)
            {
                UI.Notify("Caught unhandled exception in PedThread for player " + Name);
                UI.Notify(ex.Message);
            }
        }

        struct Interpolation
        {
            public Vector3 vecTarget;
            public Vector3 vecError;
            public int StartTime;
            public int FinishTime;
            public float LastAlpha;
        }

        private Interpolation currentInterop = new Interpolation();

        public void StartInterpolation()
        {
            currentInterop = new Interpolation
            {
                vecTarget = VehiclePosition,
                vecError = (VehiclePosition - _lastVehiclePos) * Util.Lerp(0.25f, Util.Unlerp(100, 100, 400), 1f),
                StartTime = Environment.TickCount,
                FinishTime = Environment.TickCount + 100,
                LastAlpha = 0f
            };
        }

        public static Ped GetResponsiblePed(Vehicle veh)
        {
            if (veh == null || veh.Handle == 0 || !veh.Exists())
                return new Ped(0);
            else if (veh.GetPedOnSeat(GTA.VehicleSeat.Driver).Handle != 0)
                return veh.GetPedOnSeat(GTA.VehicleSeat.Driver);

            for (int i = 0; i < veh.PassengerSeats; i++)
                if (veh.GetPedOnSeat((VehicleSeat)i).Handle != 0)
                    return veh.GetPedOnSeat((VehicleSeat)i);

            return new Ped(0);
        }

        public void Clear()
        {
            if (Character != null)
            {
                Character.Model.MarkAsNoLongerNeeded();
                Character.Delete();
            }

            if (_mainBlip != null)
            {
                _mainBlip.Remove();
                _mainBlip = null;
            }

            if (MainVehicle != null && Util.IsVehicleEmpty(MainVehicle))
            {
                MainVehicle.Model.MarkAsNoLongerNeeded();
                MainVehicle.Delete();
            }

            if (_parachuteProp != null)
            {
                _parachuteProp.Delete();
                _parachuteProp = null;
            }
        }
    }
}