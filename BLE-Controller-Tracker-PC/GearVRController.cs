using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

using System.Runtime.InteropServices.WindowsRuntime;

using AHRS;

namespace controller_tracker
{
    class GearVRController : BaseController
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        static float NO_DATA_CONNECTED_THRESHOLD_SECONDS = 5f;
        static float KEEP_ALIVE_REQUEST_INTERVAL_SECONDS = 5f;

        static Guid UUID_CUSTOM_SERVICE = Guid.Parse("4f63756c-7573-2054-6872-65656d6f7465");
        static Guid UUID_WRITE_CHARACTERISTIC = Guid.Parse("c8c51726-81bc-483b-a052-f7a14ea3d282");
        static Guid UUID_NOTIFY_CHARACTERISTIC = Guid.Parse("c8c51726-81bc-483b-a052-f7a14ea3d281");

        static short CMD_OFF = 0x0000;
        static short CMD_SENSOR = 0x0100;
        static short CMD_UNKNOWN_FIRMWARE_UPDATE_FUNC = 0x0200;
        static short CMD_CALIBRATE = 0x0300;
        static short CMD_KEEP_ALIVE = 0x0400;
        static short CMD_UNKNOWN_SETTING = 0x0500;
        static short CMD_LPM_ENABLE = 0x0600;
        static short CMD_LPM_DISABLE = 0x0700;
        static short CMD_VR_MODE = 0x0800;

        static float GYRO_FACTOR      = 0.0001f; // to radians / s
        static float ACCEL_FACTOR     = 0.00001f; // to g (9.81 m/s**2)
        static float TIMESTAMP_FACTOR = 0.001f; // to seconds

        static readonly float GEARVR_HZ = 180f;

        DateTime _connectedDateTime;


        // MAIN
        
        // MadgwickAHRS 
        MadgwickAHRS _ahrs;
        

        // SENSOR DATA PARSING
        private byte[] eventData = new byte[60];
        private int[] eventAnalysis = new int[60 * 8];
        private int[] eventBits = new int[60 * 8];
        //int eventAnalysisThr = 80;
           

        // BLE characteristics
        private GattCharacteristic _notifyCharacteristic, _writeCharacteristic;

        // CONTROL bools
        private DateTime _lastTimeReceivedDataFromController = DateTime.MinValue;
        private DateTime _lastTimeKickstarted = DateTime.MinValue;


        // MONITOR
        private Thread _monitorThread;
        

        public GearVRController(ulong bluetoothAddress) {
            _bluetoothAddress = bluetoothAddress;
            


            _ahrs = new MadgwickAHRS(
                samplePeriod: 1 / 68.84681583453657f, // Madgwick is sensitive to this
                beta: 0.352f
            );
            //_ahrs = new MadgwickAHRS(1f / GEARVR_HZ, 0.01f);


            // start MonotorThread
            _monitorThread = new Thread(MonitorThreadWorker);
            _monitorThread.IsBackground = true;
            _monitorThread.Start();
        }

        // PUBLIC
        

        // MONITOR

        async void MonitorThreadWorker() {
            Log("Monitor thread started");

            while (true) {
                if (!_connected && _wantToConnect & !_connectionInProgress) {
                    _Connect();
                }

                if (_connected && (DateTime.Now - _lastTimeReceivedDataFromController).TotalSeconds > 0.2
                                && (DateTime.Now - _lastTimeKickstarted).TotalSeconds > 3) {
                    KickstartReceiving();
                }
            }
        }


        // VALUES PARSING

        void ParseSensorData(IBuffer characteristicValue) {
            

            if (eventData.Length != characteristicValue.Length)
                eventData = new byte[characteristicValue.Length];

            DataReader.FromBuffer(characteristicValue).ReadBytes(eventData);

            var buffer = characteristicValue.ToArray();

            if (buffer.Length < 3) {
                return;
            }

            //Array.Reverse(buffer);

            var accelerometer = new List<float> {
                getAccelerometerFloatWithOffsetFromArrayBufferAtIndex(buffer, 4, 0),
                getAccelerometerFloatWithOffsetFromArrayBufferAtIndex(buffer, 6, 0),
                getAccelerometerFloatWithOffsetFromArrayBufferAtIndex(buffer, 8, 0)
            }.Select(x => x * ACCEL_FACTOR).ToList();

            var gyro = new List<float> {
                getGyroscopeFloatWithOffsetFromArrayBufferAtIndex(buffer, 10, 0),
                getGyroscopeFloatWithOffsetFromArrayBufferAtIndex(buffer, 12, 0),
                getGyroscopeFloatWithOffsetFromArrayBufferAtIndex(buffer, 14, 0)
            }.Select(x => x * GYRO_FACTOR).ToList();

            var mag = new List<float> {
                getMagnetometerFloatWithOffsetFromArrayBufferAtIndex(buffer, 0),
                getMagnetometerFloatWithOffsetFromArrayBufferAtIndex(buffer, 2),
                getMagnetometerFloatWithOffsetFromArrayBufferAtIndex(buffer, 4)
            };

            _ahrs.Update(
                gyro[0],
                gyro[1],
                gyro[2],
                accelerometer[0],
                accelerometer[1],
                accelerometer[2]
                );

            // change quaternion format to Unity one
            _latestTrackingData.quaternion = new float[] {
                -_ahrs.Quaternion[1],
                -_ahrs.Quaternion[3],
                -_ahrs.Quaternion[2],
                _ahrs.Quaternion[0]
            };


            _latestTrackingData.touchpadX = (
                ((eventData[54] & 0xF) << 6) +
                ((eventData[55] & 0xFC) >> 2)
            ) & 0x3FF;

            // Max observed value = 315
            _latestTrackingData.touchpadY = (
                ((eventData[55] & 0x3) << 8) +
                ((eventData[56] & 0xFF) >> 0)
            ) & 0x3FF;

            _latestTrackingData.triggerButton = 0 != (eventData[58] & (1 << 0));
            _latestTrackingData.homeButton = 0 != (eventData[58] & (1 << 1));
            _latestTrackingData.backButton = 0 != (eventData[58] & (1 << 2));
            _latestTrackingData.touchpadButton = 0 != (eventData[58] & (1 << 3));
            _latestTrackingData.volumeDownButton = 0 != (eventData[58] & (1 << 4));
            _latestTrackingData.volumeUpButton = 0 != (eventData[58] & (1 << 5));
            _latestTrackingData.touchpadPressed = _latestTrackingData.touchpadX != 0 && _latestTrackingData.touchpadY != 0;

            var temperature = eventData[57];

            //Log($"Touchpad: ({trackingData.touchpadX}, {trackingData.touchpadY})");


            OnSensorDataUpdated(this);
            //Log($"trigger: {triggerButton}, home: {homeButton}, back: {backButton}, Q: {string.Join(", ", _ahrs.Quaternion)}");
        }

        float getAccelerometerFloatWithOffsetFromArrayBufferAtIndex(byte[] arrayBuffer, int offset, int index) {             
            var arrayOfBytes = arrayBuffer.Slice(16 * index + offset, 16 * index + offset + 2);
            return BitConverter.ToInt16(arrayOfBytes, 0) * 10000.0f * 9.80665f / 2048.0f;
        }

        float getGyroscopeFloatWithOffsetFromArrayBufferAtIndex(byte[] arrayBuffer, int offset, int index) {
            var arrayOfBytes = arrayBuffer.Slice(16 * index + offset, 16 * index + offset + 2);
            return BitConverter.ToInt16(arrayOfBytes, 0) * 10000.0f * 0.017453292f / 14.285f;
        }

        float getMagnetometerFloatWithOffsetFromArrayBufferAtIndex(byte[] arrayBuffer, int offset) {
            var arrayOfBytes = arrayBuffer.Slice(32 + offset, 32 + offset + 2);
            return BitConverter.ToInt16(arrayOfBytes, 0) * 0.06f;
        }

        // CONNECTION

        protected override bool _Connect() {

            lock (ControllersTracker.BLEConnectionLock) {

                //Thread.Sleep(1000);

                base._Connect();

                //if (ControllersTracker.sharedInstance().IsAnyConnecting) {
                //    // wait others to finish
                //    while (ControllersTracker.sharedInstance().IsAnyConnecting) {
                //        Log("wait others to finish conencting...");
                //        Thread.Sleep(100);
                //    }
                //}

                Log("_Connect");

                if (_connectionInProgress) {
                    Log($"Cant start connecting - connection already in progress");
                    return false;
                }
                _connectionInProgress = true;

                Log($"Try to connect untill success");
                // untill reach
                while (true) {
                    Log($"Trying to connect...");

                    try {
                        var res = TryToConnect();

                        if (res) {
                            //Log("break");
                            break;
                        }
                    }catch(Exception ex) {
                        LogException(ex, "Exception while trying to connect");
                    }
                    

                    Thread.Sleep(500);
                }

                //// unpair
                //Log($"Unpairing {_bleDevice.BluetoothAddress}");
                //var deviceUnpairingRes =_bleDevice.DeviceInformation.Pairing.UnpairAsync().AsTask().GetAwaiter().GetResult();
                //Log($"Device unpairing result: {deviceUnpairingRes.Status}");

                ////Log($"_bleDevice.DeviceInformation.Pairing.IsPaired: {_bleDevice.DeviceInformation.Pairing.IsPaired}");


                //// pair
                //Log($"Attempt pairing...");
                //var pairRes = PairAsync().GetAwaiter().GetResult();
                //Log($"PairingResult Protection Level Used: {pairRes.ProtectionLevelUsed}");
                //if (pairRes.Status == DevicePairingResultStatus.AlreadyPaired || pairRes.Status == DevicePairingResultStatus.Paired) {
                //    Log($"Pairing result: {pairRes.Status}");
                //}
                //else {
                //    LogError($"Pairing result: {pairRes.Status} - Connection failed");
                //    _connectionInProgress = false;
                //    return false;                
                //}


                // get service
                Log($"Geting controller service...");
                var getServicesResult = _bleDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
                Log($"Get services status: {getServicesResult.Status}");
                var services = getServicesResult.Services;

                var controllerService = services.Where(x => x.Uuid == UUID_CUSTOM_SERVICE).FirstOrDefault();

                if (controllerService == null) {
                    LogError($"Controller service is NULL - Connection failed");
                    _connectionInProgress = false;
                    return false;
                }

                // get characteristics
                Log($"Geting characteristics...");
                var getNotifyCharacteristicResult = controllerService.GetCharacteristicsForUuidAsync(UUID_NOTIFY_CHARACTERISTIC, BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
                Log($"Getting notify characteristic success: {getNotifyCharacteristicResult.Status}");

                var getWriteCharacteristicResult = controllerService.GetCharacteristicsForUuidAsync(UUID_WRITE_CHARACTERISTIC, BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
                Log($"Getting write characteristic success: {getWriteCharacteristicResult.Status}");

                if (getWriteCharacteristicResult.Status != GattCommunicationStatus.Success
                    || getNotifyCharacteristicResult.Status != GattCommunicationStatus.Success) {
                    LogError("Failed to get both characteristics - Connection failed");
                    _connectionInProgress = false;
                    return false;
                }

                _notifyCharacteristic = getNotifyCharacteristicResult.Characteristics.First();
                _writeCharacteristic = getWriteCharacteristicResult.Characteristics.First();


                try {
                    // Write the ClientCharacteristicConfigurationDescriptor in order for server to send notifications.               
                    var result = SubscribeToCharacteristic().GetAwaiter().GetResult();
                    if (result == GattCommunicationStatus.Success) {
                        _notifyCharacteristic.ValueChanged += _notifyCharacteristic_ValueChanged;

                    }
                    else {

                        LogError($"Failed to subscribe to characteristic: {result}");
                        _connectionInProgress = false;
                        return false;
                    }


                    KickstartReceiving();

                    
                }
                catch (Exception ex) {
                    // This usually happens when not all characteristics are found
                    // or selected characteristic has no Notify.
                    LogException(ex, "Failed to subscribe to GearVR data");
                    _connectionInProgress = false;
                    return false;
                }

                Log($"-> Connected.");
                _connectionInProgress = false;
                _connected = true;
                _lastTimeReceivedDataFromController = DateTime.Now;
                return true;

            }
            
        }

        private async Task<DevicePairingResult> PairAsync() {
            _bleDevice.DeviceInformation.Pairing.Custom.PairingRequested += (s, args) => {
                args.Accept();
            };
            return await _bleDevice.DeviceInformation.Pairing.Custom.PairAsync(DevicePairingKinds.ConfirmOnly | DevicePairingKinds.None, DevicePairingProtectionLevel.None);
        }
        
        // ping to BLE device
        private bool TryToConnect()
        {
            ClearBLEDevice();


            Log($"Trying to get GATT services...");
            //return  _bleDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
            _bleDevice = BluetoothLEDevice.FromBluetoothAddressAsync(_bluetoothAddress).AsTask().GetAwaiter().GetResult();
            _bleDevice.ConnectionStatusChanged += _connectionStatusChanged;

            if (_bleDevice == null) {
                return false;
            }
            Log($"Got BLE device");

            var res = _bleDevice.GetGattServicesForUuidAsync(UUID_CUSTOM_SERVICE, BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();

            Log($"Got services");

            return true;
        }

        private void KickstartReceiving() {
            if (_writeCharacteristic != null) {
                Log("KickstartReceiving...");

                try {
                    Thread.Sleep(800);

                    var successVRMode = SendVRModeCommand().GetAwaiter().GetResult();
                    Log($"SetVRMode success: {successVRMode}");

                    Thread.Sleep(800);


                    var success = SendSensorCommand().GetAwaiter().GetResult();
                    Log($"RequestSensorData success: {success}");
                }
                catch {
                    LogError("Failed to kickstart - exception");
                }
                
            }

            _lastTimeKickstarted = DateTime.Now;
        }

        // COMANDS FOR GearVR Controller
        private async Task<GattCommunicationStatus> SubscribeToCharacteristic() {
            return await _notifyCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                                                            GattClientCharacteristicConfigurationDescriptorValue.Notify);
        }

        private async Task<bool> SendVRModeCommand()
        {
            var writer = new Windows.Storage.Streams.DataWriter();
            short val = CMD_VR_MODE;
            writer.WriteInt16(val);
            GattCommunicationStatus writeResult = await _writeCharacteristic.WriteValueAsync(writer.DetachBuffer());

            bool success = writeResult == GattCommunicationStatus.Success;
            return success;
        }

        private async Task<bool> SendSensorCommand()
        {         
            var writer = new Windows.Storage.Streams.DataWriter();
            short val = CMD_SENSOR;
            writer.WriteInt16(val);
            GattCommunicationStatus writeResult = await _writeCharacteristic.WriteValueAsync(writer.DetachBuffer());

            bool success = writeResult == GattCommunicationStatus.Success;
            return success;
        }
        
        private async Task<bool> SendPowerOffCommand()
        {           
            var writer = new Windows.Storage.Streams.DataWriter();
            short val = CMD_OFF;
            writer.WriteInt16(val);
            GattCommunicationStatus writeResult = await _writeCharacteristic.WriteValueAsync(writer.DetachBuffer());

            bool success = writeResult == GattCommunicationStatus.Success;
            return success;
        }


        private void ClearBLEDevice() {
            if (_bleDevice != null) {
                _bleDevice.ConnectionStatusChanged -= _connectionStatusChanged;
                _bleDevice.Dispose();
                _bleDevice = null;
            }
        }


        protected override void _Disconnect() {
            base._Disconnect();

            if (_connected) {
                try {
                    SendPowerOffCommand().GetAwaiter().GetResult();
                }
                catch { }                
            }
            
            if (_notifyCharacteristic != null) {
                _notifyCharacteristic.ValueChanged -= _notifyCharacteristic_ValueChanged;
                _notifyCharacteristic = null;                
            }

            if (_writeCharacteristic != null) {
                _writeCharacteristic = null;                
            }

            ClearBLEDevice();

            _connected = false;
        }


        


        // EVENTS
        private void _connectionStatusChanged(BluetoothLEDevice bluetoothLEDevice, object e) {
            Log($"Connection status changed to {bluetoothLEDevice.ConnectionStatus}");

            switch (bluetoothLEDevice.ConnectionStatus) {
                case BluetoothConnectionStatus.Connected:
                    _connectedDateTime = DateTime.Now;
                    break;
                case BluetoothConnectionStatus.Disconnected:
                    LogWarning($"-> Disconnected from {Name}");
                    Log($"Connection lasted: {(DateTime.Now - _connectedDateTime).ToString()}");
                    
                    break;
            }


            if (!_connectionInProgress && _connected && bluetoothLEDevice.ConnectionStatus == BluetoothConnectionStatus.Disconnected) {
                _Disconnect();
            }


        }

        private void _notifyCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            _lastTimeReceivedDataFromController = DateTime.Now;
            _connected = true;
            //Log($"changed {DateTime.Now}");
            try {
                ParseSensorData(args.CharacteristicValue);
            }
            catch (Exception ex) {
                LogException(ex, $"Exception while parsing controller sensor data");
            }
            
        }
        
    }
}
