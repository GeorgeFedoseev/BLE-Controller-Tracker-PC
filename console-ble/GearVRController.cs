using System;
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

namespace console_ble
{
    class GearVRController: IDisposable
    {

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

        private string _winDeviceId;
        private BluetoothLEDevice _bleDevice;

        private GattCharacteristic _notifyCharacteristic, _writeCharacteristic;

        private volatile bool _wantToConnect = false;

        private volatile bool _connected = false;
        private volatile bool _connectionInProgress = false;

        private DateTime _lastTimeReceivedDataFromController = DateTime.MinValue;

        private Thread _monitorThread;
        

        public GearVRController() {
            // start MonotorThread
            _monitorThread = new Thread(MonitorThreadWorker);
            _monitorThread.IsBackground = true;
            _monitorThread.Start();
        }

        // PUBLIC
        public void Connect()
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


        // CONNECTION


        private bool _Connect() {
            if (_connectionInProgress) {
                Console.WriteLine("Cant start connecting - connection already in progress");
                return false;
            }

            _connectionInProgress = true;

            // untill reach
            while (true) {
                Console.WriteLine("Trying to connect...");
                var res = TryGetGattServices();

                if (res.Status == GattCommunicationStatus.Success) {
                    break;
                }

                Thread.Sleep(3000);
            }
         
            Console.WriteLine($"Device connection status: {_bleDevice.ConnectionStatus}");


            // get service
            var getServicesResult = _bleDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
            Console.WriteLine($"Get services status: {getServicesResult.Status}");
            var services = getServicesResult.Services;
            
            GattDeviceService controllerService = services.Where(x => x.Uuid == UUID_CUSTOM_SERVICE).FirstOrDefault();
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
        
        // ping to BLE device
        private GattDeviceServicesResult TryGetGattServices()
        {
            Console.WriteLine("Trying to get GATT services...");
            //return  _bleDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
            _bleDevice = BluetoothLEDevice.FromIdAsync(_winDeviceId).AsTask().GetAwaiter().GetResult();
            Console.WriteLine("Got BLE device");
            var res = _bleDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
            Console.WriteLine($"Result: {res.Status}");

            if (res.Status == GattCommunicationStatus.Unreachable) {
                _bleDevice.Dispose();
                _bleDevice = null;
            }

            return res;
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
            var writer = new Windows.Storage.Streams.DataWriter();
            short val = CMD_OFF;
            writer.WriteInt16(val);
            GattCommunicationStatus writeResult = await _writeCharacteristic.WriteValueAsync(writer.DetachBuffer());

            bool success = writeResult == GattCommunicationStatus.Success;
            return success;
        }

        private void _Disconnect() {
            if (_connected) {
                SendPowerOff().GetAwaiter().GetResult();
            }
            _connected = false;
            if (_notifyCharacteristic != null) {
                _notifyCharacteristic.ValueChanged -= _notifyCharacteristic_ValueChanged;
                _notifyCharacteristic = null;
                _writeCharacteristic = null;
                _bleDevice.Dispose();
                _bleDevice = null;
            }
        }


        // ENVENTS
        private void _notifyCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            _lastTimeReceivedDataFromController = DateTime.Now;
            _connected = true;
            Console.WriteLine($"changed {DateTime.Now}");
        }

        public void Dispose()
        {
            if (_connected) {
                _Disconnect();               

            }
            
        }



        // STATIC

        public static async Task<List<GearVRController>> FindPairedGearVRControllersAsync()
        {
            var result = new List<GearVRController>();
            
            var devices = await DeviceInformation.FindAllAsync(GattDeviceService.GetDeviceSelectorFromUuid(UUID_CUSTOM_SERVICE), null);

            foreach (var device in devices) {
                var controller = new GearVRController();
                
                controller._winDeviceId = device.Id;
                result.Add(controller);
            }

            return result;
        }

       
    }
}
