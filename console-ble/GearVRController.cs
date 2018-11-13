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
    class GearVRController
    {

        static float SEARCH_TIME_SECONDS = 3;

        static Guid UUID_CUSTOM_SERVICE = Guid.Parse("4f63756c-7573-2054-6872-65656d6f7465");
        static Guid UUID_WRITE_CHARACTERISTIC = Guid.Parse("c8c51726-81bc-483b-a052-f7a14ea3d282");
        static Guid UUID_NOTIFY_CHARACTERISTIC = Guid.Parse("c8c51726-81bc-483b-a052-f7a14ea3d281");


        private string _winDeviceId;
        private BluetoothLEDevice _bleDevice;

        private GattCharacteristic _notifyCharacteristic, _writeCharacteristic;

        Thread _keepAliveThread;
        

        public GearVRController() {
        }

        public async Task<bool> ConnectAsync() {
            StopKeepAlive();

            // get BLE device
            _bleDevice = await BluetoothLEDevice.FromIdAsync(_winDeviceId);

            

            Console.WriteLine($"Connection status: {_bleDevice.ConnectionStatus}");
            if (_bleDevice.ConnectionStatus == BluetoothConnectionStatus.Connected) {
                StartKeepAlive();
            }
            else {
                Console.WriteLine("Not connected, waiting for connection...");
                _bleDevice.ConnectionStatusChanged += OnConnectionAppeared;
                await PingAsync();
            }



            while (_keepAliveThread == null) {
                Thread.Sleep(100);
            }

            Console.WriteLine($"Device connection status: {_bleDevice.ConnectionStatus}");


            // get service
            var getServicesResult = await _bleDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            Console.WriteLine($"Get services status: {getServicesResult.Status}");
            var services = getServicesResult.Services;
            GattDeviceService controllerService = services.Single(x => x.Uuid == UUID_CUSTOM_SERVICE);

            // get characteristics
            
            var getNotifyCharacteristicResult = await controllerService.GetCharacteristicsForUuidAsync(UUID_NOTIFY_CHARACTERISTIC);
            Console.WriteLine($"Getting notify characteristic success: {getNotifyCharacteristicResult.Status}");

            var getWriteCharacteristicResult = await controllerService.GetCharacteristicsForUuidAsync(UUID_WRITE_CHARACTERISTIC);
            Console.WriteLine($"Getting write characteristic success: {getWriteCharacteristicResult.Status}");

            if (getWriteCharacteristicResult.Status != GattCommunicationStatus.Success
                || getNotifyCharacteristicResult.Status != GattCommunicationStatus.Success) {
                return false;
            }

            _notifyCharacteristic = getNotifyCharacteristicResult.Characteristics.First();
            _writeCharacteristic = getWriteCharacteristicResult.Characteristics.First();
             
            
            try {
                // Write the ClientCharacteristicConfigurationDescriptor in order for server to send notifications.               
                var result = await _notifyCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                                                            GattClientCharacteristicConfigurationDescriptorValue.Notify);
                if (result == GattCommunicationStatus.Success) {
                    _notifyCharacteristic.ValueChanged += _notifyCharacteristic_ValueChanged;
                    
                }
                else {
                    
                    Console.WriteLine($"Failed to subscribe to characteristic: {result}");
                    return false;
                }

                // Console.WriteLine($"WriteClientCharacteristicConfigurationDescriptorAsync success: {success}");

                
                var success = await InitialKickEvents();
                Console.WriteLine($"Kick Events success: {success}");
                

            }
            catch (Exception ex) {
                // This usually happens when not all characteristics are found
                // or selected characteristic has no Notify.
                Console.WriteLine($"Subscribing Exception: {ex.Message}");
                return false;
            }

            return true;
        }


        private void OnConnectionAppeared(BluetoothLEDevice device, object args) {

            Console.WriteLine($"Connection status changed: {device.ConnectionStatus}");
            if (_bleDevice.ConnectionStatus == BluetoothConnectionStatus.Connected) {
                StartKeepAlive();
            }

            _bleDevice.ConnectionStatusChanged -= OnConnectionAppeared;
        }

        private async Task<GattDeviceServicesResult> PingAsync()
        {
            return await _bleDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached);
        }


        private void StartKeepAlive() {
            _keepAliveThread = new Thread(async () => {
                Console.WriteLine("Start KeepAlive");

                while (true) {
                    var getServicesResult = await PingAsync();
                    Console.WriteLine($"KeepAlive result: {getServicesResult.Status}");

                    if (_bleDevice.ConnectionStatus == BluetoothConnectionStatus.Disconnected) {
                        _bleDevice.ConnectionStatusChanged += OnConnectionAppeared;
                        break;
                    }

                    Thread.Sleep(1000);
                }
            });
            _keepAliveThread.IsBackground = true;
            _keepAliveThread.Start();
            
        }

        private void StopKeepAlive() {
            if (_keepAliveThread != null) {
                _keepAliveThread.Abort();
                _keepAliveThread = null;
            }
        }

        private void _notifyCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            Console.WriteLine("changed");
        }

        private async Task<bool> InitialKickEvents()
        {
            var writer = new Windows.Storage.Streams.DataWriter();
            short val = 0x0100;
            writer.WriteInt16(val);
            GattCommunicationStatus writeResult = await _writeCharacteristic.WriteValueAsync(writer.DetachBuffer());

            bool success = writeResult == GattCommunicationStatus.Success;
            return success;
        }

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
