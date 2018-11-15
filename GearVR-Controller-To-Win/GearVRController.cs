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

namespace gearvr_controller_to_win
{
    class GearVRController : IDisposable
    {

        public Action<GearVRController> OnSensorDataUpdated = (_) => { };

        static float SEARCH_TIME_SECONDS = 3;
        static float NO_DATA_CONNECTED_THRESHOLD_SECONDS = 5f;

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


        // MAIN

        public string Name {
            get {
                return _bleDevice != null ? _bleDevice.Name : "NULL";
            }
        }
        
        private BluetoothLEDevice _bleDevice;
        private ulong _bluetoothAddress;

        public GearVRControllerTrackingData trackingData = new GearVRControllerTrackingData();

        // MadgwickAHRS 
        MadgwickAHRS _ahrs;
        
        //public float[] Quaternion {
        //    get {
        //        if (_ahrs != null) {
        //            return _ahrs.Quaternion;
        //        }

        //        return new float[] { 0, 0, 0, 0 };
        //    }
        //}

        // SENSOR DATA PARSING
        private byte[] eventData = new byte[60];
        private int[] eventAnalysis = new int[60 * 8];
        private int[] eventBits = new int[60 * 8];
        int eventAnalysisThr = 80;
           

        // BLE characteristics
        private GattCharacteristic _notifyCharacteristic, _writeCharacteristic;

        // CONTROL bools
        private volatile bool _wantToConnect = false;

        public bool IsConnected {
            get {
                return _connected;
            }
        }
        private volatile bool _connected = false;
        private volatile bool _connectionInProgress = false;

        private DateTime _lastTimeReceivedDataFromController = DateTime.MinValue;


        // MONITOR
        private Thread _monitorThread;
        

        public GearVRController(ulong bluetoothAddress) {
            _bluetoothAddress = bluetoothAddress;
        

            _ahrs = new MadgwickAHRS(
                samplePeriod: 1/68.84681583453657f, // Madgwick is sensitive to this
                beta: 0.352f
            );

            // start MonotorThread
            _monitorThread = new Thread(MonitorThreadWorker);
            _monitorThread.IsBackground = true;
            _monitorThread.Start();
        }

        // PUBLIC
        public void ConnectAsync()
        {
            _wantToConnect = true;
        }

        public void Disconnect() {
            _wantToConnect = false;
            _Disconnect();
        }

        // MONITOR

        void MonitorThreadWorker() {
            Console.WriteLine("Monitor thread started");

            while (true) {

                if (_connected && (DateTime.Now - _lastTimeReceivedDataFromController).TotalSeconds > NO_DATA_CONNECTED_THRESHOLD_SECONDS) {
                    // not receiving data - connection lost
                    Console.WriteLine($"Didn't receive data for {NO_DATA_CONNECTED_THRESHOLD_SECONDS} seconds - disconnected");
                    _Disconnect();
                }

                if (!_connected && _wantToConnect & !_connectionInProgress) {
                    _Connect();
                }
            }
        }


        // VALUES PARSING
        void ParseSensorData(IBuffer characteristicValue) {
            if (eventData.Length != characteristicValue.Length)
                eventData = new byte[characteristicValue.Length];

            DataReader.FromBuffer(characteristicValue).ReadBytes(eventData);

            var buffer = characteristicValue.ToArray();
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

            

            //Console.WriteLine(string.Format("{0,5:###.0} {1,5:###.0} {2,5:###.0}",
            //    gyro[0],
            //    gyro[1],
            //    gyro[2]
            //    ));

            //Console.WriteLine(string.Format("{0,5:###.0} {1,5:###.0} {2,5:###.0}",
            //    accelerometer[0],
            //    accelerometer[1],
            //    accelerometer[2]
            //    ));

            //Console.WriteLine(string.Format("{0,5:###.0} {1,5:###.0} {2,5:###.0}",
            //    mag[0],
            //    mag[1],
            //    mag[2]
            //    ));

            _ahrs.Update(
                gyro[0],
                gyro[1],
                gyro[2],
                accelerometer[0],
                accelerometer[1],
                accelerometer[2]
                );


            // change quaternion format to Unity one
            trackingData.quaternion = new float[] {
                -_ahrs.Quaternion[1],
                -_ahrs.Quaternion[3],
                -_ahrs.Quaternion[2],
                _ahrs.Quaternion[0]
            };


            trackingData.touchpadX = (
                ((eventData[54] & 0xF) << 6) +
                ((eventData[55] & 0xFC) >> 2)
            ) & 0x3FF;

            // Max observed value = 315
            trackingData.touchpadY = (
                ((eventData[55] & 0x3) << 8) +
                ((eventData[56] & 0xFF) >> 0)
            ) & 0x3FF;

            trackingData.triggerButton = 0 != (eventData[58] & (1 << 0));
            trackingData.homeButton = 0 != (eventData[58] & (1 << 1));
            trackingData.backButton = 0 != (eventData[58] & (1 << 2));
            trackingData.touchpadButton = 0 != (eventData[58] & (1 << 3));
            trackingData.volumeDownButton = 0 != (eventData[58] & (1 << 4));
            trackingData.volumeUpButton = 0 != (eventData[58] & (1 << 5));
            trackingData.touchpadPressed = trackingData.touchpadX != 0 && trackingData.touchpadY != 0;

            var temperature = eventData[57];

            //Console.WriteLine($"Touchpad: ({trackingData.touchpadX}, {trackingData.touchpadY})");


            OnSensorDataUpdated(this);
            //Console.WriteLine($"trigger: {triggerButton}, home: {homeButton}, back: {backButton}, Q: {string.Join(", ", _ahrs.Quaternion)}");
        }

        float getAccelerometerFloatWithOffsetFromArrayBufferAtIndex(byte[] arrayBuffer, int offset, int index) {             
            var arrayOfBytes = arrayBuffer.Slice(16 * index + offset, 16 * index + offset + 2);
            //if (BitConverter.IsLittleEndian) {
            //    arrayOfBytes = arrayOfBytes.ToArray();
            //    Array.Reverse(arrayOfBytes);
            //}
            return BitConverter.ToInt16(arrayOfBytes, 0) * 10000.0f * 9.80665f / 2048.0f;
        }

        float getGyroscopeFloatWithOffsetFromArrayBufferAtIndex(byte[] arrayBuffer, int offset, int index) {
            var arrayOfBytes = arrayBuffer.Slice(16 * index + offset, 16 * index + offset + 2);
            //if (BitConverter.IsLittleEndian) {
            //    arrayOfBytes = arrayOfBytes.ToArray();
            //    Array.Reverse(arrayOfBytes);
            //}

            return BitConverter.ToInt16(arrayOfBytes, 0) * 10000.0f * 0.017453292f / 14.285f;
        }

        float getMagnetometerFloatWithOffsetFromArrayBufferAtIndex(byte[] arrayBuffer, int offset) {
            var arrayOfBytes = arrayBuffer.Slice(32 + offset, 32 + offset + 2);
            //if (BitConverter.IsLittleEndian) {
            //    arrayOfBytes = arrayOfBytes.ToArray();
            //    Array.Reverse(arrayOfBytes);
            //}
                
            return BitConverter.ToInt16(arrayOfBytes, 0) * 0.06f;
        }

       



        // CONNECTION


        private bool _Connect() {
            if (_connectionInProgress) {
                Console.WriteLine("Cant start connecting - connection already in progress");
                return false;
            }

            _connectionInProgress = true;

            // untill reach
            while (true) {
                Console.WriteLine($"Trying to connect to {_bluetoothAddress}...");

            

                var res = TryGetGattServices();

                if (res) {
                    Console.WriteLine("break");
                    break;
                }

                Thread.Sleep(3000);
            }
         
            Console.WriteLine($"Device connection status: {_bleDevice.ConnectionStatus}");

            // unpair
            var deviceUnpairingRes =_bleDevice.DeviceInformation.Pairing.UnpairAsync().AsTask().GetAwaiter().GetResult();
            Console.WriteLine($"Device unpairing result: {deviceUnpairingRes.Status}");

            Console.WriteLine($"_bleDevice.DeviceInformation.Pairing.IsPaired: {_bleDevice.DeviceInformation.Pairing.IsPaired}");
            

            // pair
            Console.WriteLine("Attempt pairing...");
            var pairRes = PairAsync().GetAwaiter().GetResult();
            Console.WriteLine($"Pairing result: {pairRes.Status}");


            // get service
            var getServicesResult = _bleDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
            Console.WriteLine($"Get services status: {getServicesResult.Status}");
            var services = getServicesResult.Services;
            
            var controllerService = services.Where(x => x.Uuid == UUID_CUSTOM_SERVICE).FirstOrDefault();
            
            if (controllerService == null) {
                _connectionInProgress = false;
                return false;
            }

            // get characteristics
            
            var getNotifyCharacteristicResult = controllerService.GetCharacteristicsForUuidAsync(UUID_NOTIFY_CHARACTERISTIC, BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
            Console.WriteLine($"Getting notify characteristic success: {getNotifyCharacteristicResult.Status}");

            var getWriteCharacteristicResult = controllerService.GetCharacteristicsForUuidAsync(UUID_WRITE_CHARACTERISTIC, BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
            Console.WriteLine($"Getting write characteristic success: {getWriteCharacteristicResult.Status}");

            if (getWriteCharacteristicResult.Status != GattCommunicationStatus.Success
                || getNotifyCharacteristicResult.Status != GattCommunicationStatus.Success) 
            {
                _connectionInProgress = false;
                return false;
            }

            _notifyCharacteristic = getNotifyCharacteristicResult.Characteristics.First();
            _writeCharacteristic = getWriteCharacteristicResult.Characteristics.First();
             
            
            try {
                // Write the ClientCharacteristicConfigurationDescriptor in order for server to send notifications.               
                var result = _notifyCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                                                            GattClientCharacteristicConfigurationDescriptorValue.Notify)
                                                            .AsTask().GetAwaiter().GetResult();
                if (result == GattCommunicationStatus.Success) {
                    _notifyCharacteristic.ValueChanged += _notifyCharacteristic_ValueChanged;
                    
                }
                else {
                    
                    Console.WriteLine($"Failed to subscribe to characteristic: {result}");
                    _connectionInProgress = false;
                    return false;
                }

                var success = RequestSensorData().GetAwaiter().GetResult();
                Console.WriteLine($"Kick Events success: {success}");

                success = SetKeepAlive().GetAwaiter().GetResult();
                Console.WriteLine($"KeepAlive success: {success}");
                
            }
            catch (Exception ex) {
                // This usually happens when not all characteristics are found
                // or selected characteristic has no Notify.
                Console.WriteLine($"Subscribing Exception: {ex.Message}");
                _connectionInProgress = false;
                return false;
            }

            _connectionInProgress = false;
            return true;
        }


        private async Task<DevicePairingResult> PairAsync() {
            _bleDevice.DeviceInformation.Pairing.Custom.PairingRequested += (s, args) => {
                args.Accept();
            };
            return await _bleDevice.DeviceInformation.Pairing.Custom.PairAsync(DevicePairingKinds.ConfirmOnly, DevicePairingProtectionLevel.None);
        }
        
        // ping to BLE device
        private bool TryGetGattServices()
        {
            Console.WriteLine("Trying to get GATT services...");
            //return  _bleDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
            _bleDevice = BluetoothLEDevice.FromBluetoothAddressAsync(_bluetoothAddress).AsTask().GetAwaiter().GetResult();
                      
            if (_bleDevice == null) {
                return false;
            }
            Console.WriteLine("Got BLE device");

            var res = _bleDevice.GetGattServicesForUuidAsync(UUID_CUSTOM_SERVICE, BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();

            Console.WriteLine("Got services");

            return true;
        }
        
        // COMANDS FOR GearVR Controller
        private async Task<bool> RequestSensorData()
        {
            var writer = new Windows.Storage.Streams.DataWriter();
            short val = CMD_SENSOR;
            writer.WriteInt16(val);
            GattCommunicationStatus writeResult = await _writeCharacteristic.WriteValueAsync(writer.DetachBuffer());

            bool success = writeResult == GattCommunicationStatus.Success;
            return success;
        }

        private async Task<bool> SetKeepAlive()
        {
            var writer = new Windows.Storage.Streams.DataWriter();
            short val = CMD_KEEP_ALIVE;
            writer.WriteInt16(val);
            GattCommunicationStatus writeResult = await _writeCharacteristic.WriteValueAsync(writer.DetachBuffer());

            bool success = writeResult == GattCommunicationStatus.Success;
            return success;
        }

        private async Task<bool> SendPowerOff()
        {
            if (_writeCharacteristic == null) {
                return false;
            }

            var writer = new Windows.Storage.Streams.DataWriter();
            short val = CMD_OFF;
            writer.WriteInt16(val);
            GattCommunicationStatus writeResult = await _writeCharacteristic.WriteValueAsync(writer.DetachBuffer());

            bool success = writeResult == GattCommunicationStatus.Success;
            return success;
        }


        private void _Disconnect() {
            if (_connected) {
                try {
                    SendPowerOff().GetAwaiter().GetResult();
                }
                catch { }                
            }
            
            if (_notifyCharacteristic != null) {
                _notifyCharacteristic.ValueChanged -= _notifyCharacteristic_ValueChanged;
                _notifyCharacteristic = null;
                _writeCharacteristic = null;
                _bleDevice.Dispose();
                _bleDevice = null;
            }
            _connected = false;
        }


        // ENVENTS
        private void _notifyCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            _lastTimeReceivedDataFromController = DateTime.Now;
            _connected = true;
            //Console.WriteLine($"changed {DateTime.Now}");
            ParseSensorData(args.CharacteristicValue);
        }

        public void Dispose()
        {
            if (_connected) {
                _Disconnect();               

            }
            
        }



        // STATIC

        //public static async Task<List<GearVRController>> FindPairedGearVRControllersAsync()
        //{
        //    var result = new List<GearVRController>();

        //    var devices = await DeviceInformation.FindAllAsync(GattDeviceService.GetDeviceSelectorFromUuid(UUID_CUSTOM_SERVICE), null);

        //    foreach (var device in devices) {
        //        var bleDevice = await BluetoothLEDevice.FromIdAsync(device.Id);
        //        var controller = new GearVRController(bleDevice.BluetoothAddress);
        //        result.Add(controller);
        //    }

        //    return result;
        //}


        


        public static List<ulong> FindUnpairedControllersAddresses(List<string> acceptNames = null)
        {
            var result = new List<ulong>();

            var _bleWatcher = new BluetoothLEAdvertisementWatcher {
                ScanningMode = BluetoothLEScanningMode.Active
            };


            _bleWatcher.Received += (w, btAdv) => {


                if (acceptNames != null) {
                    if (!acceptNames.Any(x => btAdv.Advertisement.LocalName == x)) {
                        return;
                    }
                }
                else {
                    if (!btAdv.Advertisement.LocalName.Contains("Gear VR Controller")) {
                        return;
                    }
                }

                

                

                if (result.Any(x => x == btAdv.BluetoothAddress)) {
                    // already added
                    return;
                }

                Console.WriteLine("Found " + btAdv.Advertisement.LocalName);

                result.Add(btAdv.BluetoothAddress);

            };

            _bleWatcher.Start();

            var sw = new Stopwatch();
            sw.Start();
            while (result.Count == 0 || sw.Elapsed.TotalSeconds < SEARCH_TIME_SECONDS) {
                Thread.Sleep(100);
            }
            _bleWatcher.Stop();



            return result;
        }


    }
}
