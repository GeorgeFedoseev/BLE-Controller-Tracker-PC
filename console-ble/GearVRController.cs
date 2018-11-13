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


        public GearVRController() {
        }

        public async Task ConnectAsync() {

            // get BLE device
            _bleDevice = await BluetoothLEDevice.FromIdAsync(_winDeviceId);

            _bleDevice.ConnectionStatusChanged += (s, args) => {
                Console.WriteLine($"Connection status changed: {s.ConnectionStatus}");
            };

        }

        public static async Task<List<GearVRController>> FindPairedGearVRControllersAsync()
        {
            var result = new List<GearVRController>();

            var gearVRFilter = "System.DeviceInterface.Bluetooth.ServiceGuid:= \"{00001800-0000-1000-8000-00805f9b34fb}\"";
            var gearVRs = await DeviceInformation.FindAllAsync(gearVRFilter);

            foreach (var device in gearVRs) {
                var controller = new GearVRController();
                
                controller._winDeviceId = device.Id;
                result.Add(controller);
            }


            return result;
        }

        
    }
}
